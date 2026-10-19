namespace OVS.Manager.Mail;

/// <summary>Package M26: a plain text mail.</summary>
/// <param name="To">The address.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Body">The text.</param>
internal sealed record OutgoingMail(string To, string Subject, string Body);
