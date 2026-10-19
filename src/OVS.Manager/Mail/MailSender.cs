using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Mail;

/// <summary>Package M26 (D5): sends mail over the SMTP server of <see cref="SmtpOptions"/> with MailKit.</summary>
internal sealed class MailSender(SmtpOptions options) : IMailSender
{
    /// <summary>How long a step of the SMTP talk may take.</summary>
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellation = default)
    {
        if (!options.IsConfigured) throw new MailSendException("SMTP_HOST and SMTP_FROM are not set.");
        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            using var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(options.From!));
            message.To.Add(MailboxAddress.Parse(mail.To));
            message.Subject = mail.Subject;
            message.Body = new TextPart("plain") { Text = mail.Body };
            await client.ConnectAsync(options.Host!, options.Port, options.Security switch
            {
                SmtpSecurity.None => SecureSocketOptions.None,
                SmtpSecurity.Tls => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTls,
            }, cancellation);
            if (options.User is { } user) await client.AuthenticateAsync(user, options.Password ?? "", cancellation);
            await client.SendAsync(message, cancellation);
            await client.DisconnectAsync(true, cancellation);
        }
        catch (Exception exception) when (exception is CommandException or ProtocolException or AuthenticationException or SslHandshakeException
            or ParseException or System.Net.Sockets.SocketException or IOException or TimeoutException)
        {
            throw new MailSendException(exception.Message, exception);
        }
    }
}
