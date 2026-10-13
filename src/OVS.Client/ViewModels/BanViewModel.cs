using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Moderation;
using OVS.Shared.Protocol.Messages.Moderation;

namespace OVS.Client.ViewModels;

/// <summary>Package 80: one card of the ban overview with everything the server stores about the ban (A97).</summary>
/// <param name="now">The client's time when the list was built: status and remaining time as of then.</param>
/// <param name="busy">Package 98: the unban, running until the new ban list comes.</param>
/// <param name="ban">The ban.</param>
/// <param name="canUnban">The unban button shows.</param>
/// <param name="unban">Lifts the ban.</param>
public sealed partial class BanViewModel(BanInfo ban, DateTimeOffset now, bool canUnban, Func<Task> unban, PendingActionViewModel busy) : ObservableObject, IMotionKey
{
    /// <summary>The ban's id: the same ban keeps its motion when the list is rebuilt.</summary>
    public object MotionKey => Ban.Id;
    /// <summary>The ban as the server lists it.</summary>
    public BanInfo Ban { get; } = ban;
    /// <summary>The unban, running until the new ban list comes.</summary>
    public PendingActionViewModel Busy { get; } = busy;
    /// <summary>The banned user's nickname.</summary>
    public string Nickname => Ban.Nickname;
    /// <summary>The banned identity.</summary>
    public string Fingerprint => Ban.Fingerprint;
    /// <summary>The fingerprint's first 16 characters.</summary>
    public string ShortFingerprint => Fingerprint.Length > 16 ? Fingerprint[..16] : Fingerprint;
    /// <summary>The ban covers an address.</summary>
    public bool HasIp => Ban.Ip is not null;
    /// <summary>The banned address, empty when none.</summary>
    public string Ip => Ban.Ip ?? "";
    /// <summary>The reason, or a placeholder when none was given.</summary>
    public string ReasonText => Ban.Reason.Length > 0 ? Ban.Reason : Strings.Ui_BanNoReason;
    /// <summary>The moderator's nickname.</summary>
    public string CreatedBy => Ban.CreatedBy;

    /// <summary>Active, expired or lifted as of the list's time.</summary>
    public BanStatus Status { get; } = EvaluateStatus(ban, now);
    static BanStatus EvaluateStatus(BanInfo info, DateTimeOffset now) => BanStatusRule.Evaluate(info.LiftedAt, info.ExpiresAt, now);
    /// <summary>The status as shown.</summary>
    public string StatusText => Status switch
    {
        BanStatus.Lifted => string.Format(CultureInfo.CurrentCulture, Strings.Ui_BanLifted, Ban.LiftedBy, Local(Ban.LiftedAt)),
        BanStatus.Expired => Strings.Ui_BanExpired,
        _ => Strings.Ui_BanActive,
    };
    /// <summary>The ban holds.</summary>
    public bool IsActive => Status == BanStatus.Active;

    // Bans saved before Package 80 have no creation time, creator fingerprint or duration: shown as unknown
    static string Unknown => Strings.Ui_Unknown;
    static string Local(DateTimeOffset? at) => at?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? Unknown;
    /// <summary>When the ban was made, local time.</summary>
    public string CreatedAtText => Local(Ban.CreatedAt);
    /// <summary>The moderator's fingerprint, or unknown.</summary>
    public string CreatedByFingerprintText => Ban.CreatedByFingerprint ?? Unknown;
    /// <summary>The ban's duration as shown.</summary>
    public string DurationText => Ban.ExpiresAt is null ? Strings.Ban_Forever
        : Ban.DurationMinutes is { } minutes ? Formats.Duration(TimeSpan.FromMinutes(minutes)) : Unknown;
    /// <summary>When the ban ends, local time, or "forever".</summary>
    public string EndText => Ban.ExpiresAt is null ? Strings.Ban_Forever : Local(Ban.ExpiresAt);
    /// <summary>An active ban with an end.</summary>
    public bool HasRemaining => IsActive && Ban.ExpiresAt is not null;
    /// <summary>The time left of an active ban with an end.</summary>
    public string RemainingText => HasRemaining ? Formats.Duration(Ban.ExpiresAt!.Value - now) : "";
    /// <summary>How often the ban turned someone away, and the last time.</summary>
    public string AttemptsText => Ban.BlockedAttempts == 0 ? Strings.Ui_None
        : string.Format(CultureInfo.CurrentCulture, Strings.Ui_BanAttemptsFmt, Ban.BlockedAttempts, Local(Ban.LastAttempt), Ban.LastAttemptIp ?? Unknown);

    /// <summary>Case-insensitive, in nickname, fingerprint, IP, reason, creator and lifter.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || new[] { Nickname, Fingerprint, Ban.Ip, Ban.Reason, Ban.CreatedBy, Ban.LiftedBy }.Any(text => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Package 76: the list is readable with BansView, lifting a ban needs UserBan; Package 80: only active ones.</summary>
    public bool CanUnban { get; } = canUnban && EvaluateStatus(ban, now) == BanStatus.Active;

    [RelayCommand(CanExecute = nameof(CanUnban))]
    Task UnbanAsync() => unban();
}
