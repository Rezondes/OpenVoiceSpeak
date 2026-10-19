using System.Globalization;

namespace OVS.Manager.Mail;

/// <summary>Package M26 (D5): the SMTP server from the compose file; without host and sender no mail goes out.</summary>
/// <param name="Host">SMTP_HOST.</param>
/// <param name="Port">SMTP_PORT, else the default of <paramref name="Security"/>.</param>
/// <param name="User">SMTP_USER, null for no login.</param>
/// <param name="Password">SMTP_PASSWORD.</param>
/// <param name="From">SMTP_FROM, the sender.</param>
/// <param name="Security">SMTP_TLS: none, starttls (the default) or tls.</param>
internal sealed record SmtpOptions(string? Host, int Port, string? User, string? Password, string? From, SmtpSecurity Security)
{
    /// <summary>Mail can be sent.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);

    /// <summary>The settings in <paramref name="configuration"/>.</summary>
    /// <exception cref="InvalidOperationException">SMTP_TLS or SMTP_PORT is set but invalid.</exception>
    public static SmtpOptions Read(IConfiguration configuration)
    {
        var security = configuration["SMTP_TLS"]?.Trim().ToUpperInvariant() switch
        {
            null or "" or "STARTTLS" => SmtpSecurity.StartTls,
            "NONE" => SmtpSecurity.None,
            "TLS" => SmtpSecurity.Tls,
            var other => throw new InvalidOperationException($"SMTP_TLS must be none, starttls or tls, but is '{other}'."),
        };
        int port = configuration["SMTP_PORT"] switch
        {
            null or "" => security switch { SmtpSecurity.None => 25, SmtpSecurity.Tls => 465, _ => 587 },
            var text when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is > 0 and < 65536 => value,
            var text => throw new InvalidOperationException($"SMTP_PORT must be a port, but is '{text}'."),
        };
        static string? Value(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return new SmtpOptions(Value(configuration["SMTP_HOST"]), port, Value(configuration["SMTP_USER"]), configuration["SMTP_PASSWORD"], Value(configuration["SMTP_FROM"]), security);
    }
}
