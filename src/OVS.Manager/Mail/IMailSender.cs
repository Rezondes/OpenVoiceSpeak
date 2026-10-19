using OVS.Manager.Exceptions;

namespace OVS.Manager.Mail;

/// <summary>Package M26: sends mail (the SMTP server is IO, tests use a fake).</summary>
internal interface IMailSender
{
    /// <summary>Sends <paramref name="mail"/>.</summary>
    /// <exception cref="MailSendException">It did not go out; the text is the SMTP server's error.</exception>
    Task SendAsync(OutgoingMail mail, CancellationToken cancellation = default);
}
