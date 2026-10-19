using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Exceptions;
using OVS.Manager.Mail;

namespace OVS.Manager.Accounts;

/// <summary>
/// Package M26 (AC1, AC4, D5): "Passwort vergessen" by mail. A user with an e-mail gets a link with an Identity reset token,
/// valid for <see cref="LinkLifetime"/> and good once (the reset changes the security stamp the token holds). A reset ends
/// every session and writes the audit line.
/// </summary>
internal sealed partial class PasswordReset(UserManager<ManagerUser> users, SessionStore sessions, IMailSender mail, SmtpOptions smtp, AuditLog audit,
    ILogger<PasswordReset> logger)
{
    /// <summary>How long a link is valid.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(1);

    /// <summary>The page the link opens.</summary>
    public const string ResetPath = "/reset-password";

    /// <summary>
    /// AC1: sends the link to the account with the user name or e-mail <paramref name="nameOrEmail"/>, when it has an e-mail
    /// and SMTP is set up; tells nothing either way, so the page does not show which accounts exist.
    /// </summary>
    public async Task RequestAsync(string nameOrEmail, Uri baseUri, CancellationToken cancellation = default)
    {
        var key = nameOrEmail.Trim();
        if (!smtp.IsConfigured || key.Length == 0) return;
        var user = await users.FindByNameAsync(key) ?? await users.Users.FirstOrDefaultAsync(row => row.NormalizedEmail == users.NormalizeEmail(key), cancellation);
        if (user?.Email is not { Length: > 0 } email) return;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = new Uri(baseUri, $"{ResetPath}?user={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}");
        try
        {
            await mail.SendAsync(MailTemplates.Reset(email, user.UserName ?? "", user.Language, link.ToString()), cancellation);
            await audit.WriteAsync(user.UserName ?? user.Id, AuditActions.PasswordResetMailSent, user.UserName, cancellation: cancellation);
        }
        catch (MailSendException exception)
        {
            LogFailed(logger, user.UserName ?? user.Id, exception.Message); // the page still tells nothing
        }
    }

    /// <summary>AC1, AC4: sets <paramref name="newPassword"/> with the link's token; ends every session and writes the audit line.</summary>
    public async Task<IdentityResult> ResetAsync(string userId, string token, string newPassword, CancellationToken cancellation = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return IdentityResult.Failed(new IdentityError { Code = "InvalidToken" });
        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded) return result;
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await users.UpdateAsync(user);
        }
        await sessions.EndAllAsync(user.Id, cancellation);
        await audit.WriteAsync(user.UserName ?? user.Id, AuditActions.PasswordResetByMail, user.UserName, cancellation: cancellation);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The password reset mail to {UserName} did not go out: {Reason}")]
    static partial void LogFailed(ILogger logger, string userName, string reason);
}
