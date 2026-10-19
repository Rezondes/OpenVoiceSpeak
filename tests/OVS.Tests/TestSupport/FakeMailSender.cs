using System.Collections.Concurrent;
using OVS.Manager.Exceptions;
using OVS.Manager.Mail;

namespace OVS.Tests.TestSupport;

/// <summary>Package M26: keeps the mails instead of sending them, or fails with an SMTP error a test sets.</summary>
internal sealed class FakeMailSender : IMailSender
{
    /// <summary>The mails sent, in order.</summary>
    public ConcurrentQueue<OutgoingMail> Sent { get; } = new();

    /// <summary>When set, every send fails with this SMTP error.</summary>
    public string? Failure { get; set; }

    public Task SendAsync(OutgoingMail mail, CancellationToken cancellation = default)
    {
        if (Failure is { } failure) throw new MailSendException(failure);
        Sent.Enqueue(mail);
        return Task.CompletedTask;
    }
}
