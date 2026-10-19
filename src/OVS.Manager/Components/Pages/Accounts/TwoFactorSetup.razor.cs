using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Localization;
using QRCoder;

namespace OVS.Manager.Components.Pages.Accounts;

/// <summary>
/// Package M28 (AC1, AC3): TOTP of RFC 6238 with Identity's authenticator key. The QR code is an SVG made here (no outside
/// service); a right code turns it on and shows 10 recovery codes once.
/// </summary>
sealed partial class TwoFactorSetup
{
    /// <summary>How many recovery codes a user gets.</summary>
    public const int RecoveryCodeCount = 10;

    [Inject]
    UserManager<ManagerUser> Users { get; set; } = null!;

    [Inject]
    SignInManager<ManagerUser> SignIn { get; set; } = null!;

    [Inject]
    AuditLog Audit { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromForm]
    CodeInput? Model { get; set; }

    bool Required => HttpContext.User.HasClaim(claim => claim.Type == AccountClaims.MustSetUpTwoFactor);

    bool IsEnabled { get; set; }

    string Key { get; set; } = "";

    MarkupString Qr { get; set; }

    string ActiveText { get; set; } = "";

    /// <summary>The codes, only right after turning it on.</summary>
    IReadOnlyList<string>? RecoveryCodes { get; set; }

    string? Error { get; set; }

    /// <summary>Loads the state; a user without a key gets one.</summary>
    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        var user = await Users.GetUserAsync(HttpContext.User) ?? throw new InvalidOperationException("No user is signed in.");
        IsEnabled = await Users.GetTwoFactorEnabledAsync(user);
        if (IsEnabled)
        {
            ActiveText = TextFormat.Of(Strings.TwoFactor_Active, await Users.CountRecoveryCodesAsync(user));
            return;
        }
        var key = await Users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await Users.ResetAuthenticatorKeyAsync(user);
            key = await Users.GetAuthenticatorKeyAsync(user) ?? "";
        }
        Key = key;
        var uri = $"otpauth://totp/OpenVoiceSpeak:{Uri.EscapeDataString(user.UserName ?? "")}?secret={key}&issuer=OpenVoiceSpeak&digits=6";
        using var data = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var svg = new SvgQRCode(data);
        Qr = new MarkupString(svg.GetGraphic(4)); // made here from the key alone
    }

    async Task EnableAsync()
    {
        var user = await Users.GetUserAsync(HttpContext.User) ?? throw new InvalidOperationException("No user is signed in.");
        var code = string.Concat(Model!.Code.Where(char.IsDigit));
        if (!await Users.VerifyTwoFactorTokenAsync(user, Users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            Error = Strings.TwoFactor_WrongCode;
            return;
        }
        await Users.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = [.. await Users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? []];
        await Audit.WriteAsync(user.UserName ?? user.Id, AuditActions.TwoFactorEnabled, user.UserName, detail: RecoveryCodeCount.ToString(CultureInfo.InvariantCulture));
        await SignIn.RefreshSignInAsync(user); // the requirement's claim goes, the session stays
    }

    /// <summary>The field of the form.</summary>
    sealed class CodeInput
    {
        public string Code { get; set; } = "";
    }
}
