namespace OVS.Manager.Mail;

/// <summary>Package M26 (D5): how the SMTP connection is protected, <c>SMTP_TLS</c>.</summary>
internal enum SmtpSecurity
{
    /// <summary>"none": plain, port 25 by default.</summary>
    None,
    /// <summary>"starttls": plain, then STARTTLS, port 587 by default.</summary>
    StartTls,
    /// <summary>"tls": TLS from the start, port 465 by default.</summary>
    Tls,
}
