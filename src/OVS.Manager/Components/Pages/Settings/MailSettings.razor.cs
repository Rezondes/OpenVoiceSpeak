using System.Globalization;
using Microsoft.AspNetCore.Components;
using OVS.Manager.Audit;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Mail;

namespace OVS.Manager.Components.Pages.Settings;

/// <summary>Package M26 (AC3): the SMTP settings of the compose file (without the password) and a test mail; a failure shows the SMTP error.</summary>
sealed partial class MailSettings
{
    [Inject]
    SmtpOptions Smtp { get; set; } = null!;

    [Inject]
    IMailSender Mail { get; set; } = null!;

    [Inject]
    AuditLog Audit { get; set; } = null!;

    [CascadingParameter]
    Microsoft.AspNetCore.Http.HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromForm]
    TestInput? Model { get; set; }

    string Actor => HttpContext.User.Identity?.Name ?? "";

    string? Result { get; set; }

    bool Failed { get; set; }

    string ResultClass => Failed ? "form-error" : "form-ok";

    string HostText => $"{Smtp.Host}:{Smtp.Port.ToString(CultureInfo.InvariantCulture)}";

    string SecurityText => Smtp.Security switch { SmtpSecurity.None => "none", SmtpSecurity.Tls => "tls", _ => "starttls" };

    /// <summary>A fresh form on the first render; a post fills it.</summary>
    protected override void OnInitialized() => Model ??= new();

    async Task SendAsync()
    {
        var to = Model!.To.Trim();
        try
        {
            await Mail.SendAsync(MailTemplates.Test(to, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
            (Failed, Result) = (false, TextFormat.Of(Strings.MailSettings_Sent, to));
            await Audit.WriteAsync(Actor, AuditActions.TestMailSent, to);
        }
        catch (MailSendException exception)
        {
            (Failed, Result) = (true, TextFormat.Of(Strings.MailSettings_Failed, exception.Message));
            await Audit.WriteAsync(Actor, AuditActions.TestMailSent, to, detail: exception.Message);
        }
    }

    /// <summary>The field of the form.</summary>
    sealed class TestInput
    {
        public string To { get; set; } = "";
    }
}
