using Microsoft.AspNetCore.Components;
using OVS.Manager.Accounts;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages;

/// <summary>Package M26 (AC1, AC4): a new password with the mailed link (user and token in the address).</summary>
sealed partial class ResetPassword
{
    [Inject]
    PasswordReset Reset { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "user")]
    string? UserId { get; set; }

    [SupplyParameterFromQuery(Name = "token")]
    string? Token { get; set; }

    [SupplyParameterFromForm]
    ResetInput? Model { get; set; }

    /// <summary>The password was set.</summary>
    bool Done { get; set; }

    /// <summary>Why it was not, null before a try.</summary>
    string? Error { get; set; }

    /// <summary>A fresh form with the link's user and token on the first render; a post fills it.</summary>
    protected override void OnInitialized() => Model ??= new() { UserId = UserId ?? "", Token = Token ?? "" };

    async Task ResetAsync()
    {
        var result = await Reset.ResetAsync(Model!.UserId, Model.Token, Model.NewPassword);
        Done = result.Succeeded;
        Error = result.Succeeded ? null : result.Errors.Any(error => error.Code == "InvalidToken") ? Strings.Reset_InvalidLink : AccountErrors.Describe(result);
    }

    /// <summary>The fields of the form.</summary>
    sealed class ResetInput
    {
        public string UserId { get; set; } = "";
        public string Token { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }
}
