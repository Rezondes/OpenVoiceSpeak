using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using OVS.Manager.Exceptions;
using OVS.Manager.Mail;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Mail;

/// <summary>Package M26: the SMTP settings, the real sender's error, and the admin's test mail.</summary>
public sealed class MailSenderTests
{
    static SmtpOptions Read(params (string Key, string Value)[] values) =>
        SmtpOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value))).Build());

    /// <summary>AC3: SMTP_TLS picks the protection and its default port; host and sender make it set up.</summary>
    [Fact]
    public void Options_Variables_SecurityAndPort()
    {
        Assert.False(Read().IsConfigured);
        Assert.Equal((SmtpSecurity.StartTls, 587, true), Read(("SMTP_HOST", "mail"), ("SMTP_FROM", "a@b.c")) is var plain ? (plain.Security, plain.Port, plain.IsConfigured) : default);
        Assert.Equal((SmtpSecurity.Tls, 465), Read(("SMTP_TLS", "tls")) is var tls ? (tls.Security, tls.Port) : default);
        Assert.Equal((SmtpSecurity.None, 2525), Read(("SMTP_TLS", "None"), ("SMTP_PORT", "2525")) is var none ? (none.Security, none.Port) : default);
        Assert.Throws<InvalidOperationException>(() => Read(("SMTP_TLS", "ssl")));
    }

    /// <summary>AC3: the real sender turns a refused connection into the error the admin sees.</summary>
    [Fact]
    public async Task Sender_ClosedPort_SmtpError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // nothing listens there now
        var sender = new MailSender(new SmtpOptions("127.0.0.1", port, null, null, "ovs@example.org", SmtpSecurity.None));

        var failure = await Assert.ThrowsAsync<MailSendException>(() => sender.SendAsync(new OutgoingMail("anna@example.org", "Test", "Test")));
        Assert.NotEmpty(failure.Message);
    }

    /// <summary>AC3: the admin's test mail goes out, and a failure shows the SMTP error.</summary>
    [Fact]
    public async Task TestMail_SentOrFailed_ResultShown()
    {
        await using var manager = new ManagerFactory((OVS.Manager.Accounts.FirstAdmin.UserVariable, "chef"), (OVS.Manager.Accounts.FirstAdmin.PasswordVariable, FirstAdminTests.Password),
            ("SMTP_HOST", "smtp.example.org"), ("SMTP_FROM", "ovs@example.org"));
        using var admin = manager.CreateClient();
        (await LoginTests.LoginAsync(admin, "chef", FirstAdminTests.Password)).Dispose();

        using (var sent = await ManagerForm.SubmitAsync(admin, "/settings/mail", "test-mail", ("Model.To", "chef@example.org")))
            Assert.Contains("chef@example.org", await sent.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("chef@example.org", Assert.Single(manager.Mail.Sent).To);

        manager.Mail.Failure = "535 5.7.8 Authentication credentials invalid";
        using var failed = await ManagerForm.SubmitAsync(admin, "/settings/mail", "test-mail", ("Model.To", "chef@example.org"));
        Assert.Contains("535 5.7.8 Authentication credentials invalid", await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
