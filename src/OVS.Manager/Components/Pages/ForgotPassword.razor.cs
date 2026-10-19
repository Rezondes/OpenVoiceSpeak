using Microsoft.AspNetCore.Components;
using OVS.Manager.Accounts;
using OVS.Manager.Mail;

namespace OVS.Manager.Components.Pages;

/// <summary>Package M26 (AC1, AC2): asks for a reset link; the answer is always the same text. Without SMTP it sends the user to the admin.</summary>
sealed partial class ForgotPassword
{
    [Inject]
    PasswordReset Reset { get; set; } = null!;

    [Inject]
    SmtpOptions Smtp { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [SupplyParameterFromForm]
    ForgotInput? Model { get; set; }

    /// <summary>The request was taken.</summary>
    bool Sent { get; set; }

    /// <summary>A fresh form on the first render; a post fills it.</summary>
    protected override void OnInitialized() => Model ??= new();

    async Task SendAsync()
    {
        await Reset.RequestAsync(Model!.NameOrEmail, new Uri(Navigation.BaseUri));
        Sent = true;
    }

    /// <summary>The field of the form.</summary>
    sealed class ForgotInput
    {
        public string NameOrEmail { get; set; } = "";
    }
}
