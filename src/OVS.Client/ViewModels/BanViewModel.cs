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
public sealed partial class BanViewModel(BanInfo ban, DateTimeOffset now, bool canUnban, Func<Task> unban, PendingActionViewModel busy) : ObservableObject, IMotionKey
{
    public object MotionKey => Ban.Id;
    public BanInfo Ban { get; } = ban;
    public PendingActionViewModel Busy { get; } = busy;
    public string Nickname => Ban.Nickname;
    public string Fingerprint => Ban.Fingerprint;
    public string ShortFingerprint => Fingerprint.Length > 16 ? Fingerprint[..16] : Fingerprint;
    public bool HasIp => Ban.Ip is not null;
    public string Ip => Ban.Ip ?? "";
    public string ReasonText => Ban.Reason.Length > 0 ? Ban.Reason : Strings.Ui_BanNoReason;
    public string CreatedBy => Ban.CreatedBy;

    public BanStatus Status { get; } = EvaluateStatus(ban, now);
    static BanStatus EvaluateStatus(BanInfo info, DateTimeOffset now) => BanStatusRule.Evaluate(info.LiftedAt, info.ExpiresAt, now);
    public string StatusText => Status switch
    {
        BanStatus.Lifted => string.Format(Strings.Ui_BanLifted, Ban.LiftedBy, Local(Ban.LiftedAt)),
        BanStatus.Expired => Strings.Ui_BanExpired,
        _ => Strings.Ui_BanActive,
    };
    public bool IsActive => Status == BanStatus.Active;

    // Bans saved before Package 80 have no creation time, creator fingerprint or duration: shown as unknown
    static string Unknown => Strings.Ui_Unknown;
    static string Local(DateTimeOffset? at) => at?.ToLocalTime().ToString("g") ?? Unknown;
    public string CreatedAtText => Local(Ban.CreatedAt);
    public string CreatedByFingerprintText => Ban.CreatedByFingerprint ?? Unknown;
    public string DurationText => Ban.ExpiresAt is null ? Strings.Ban_Forever
        : Ban.DurationMinutes is { } minutes ? Formats.Duration(TimeSpan.FromMinutes(minutes)) : Unknown;
    public string EndText => Ban.ExpiresAt is null ? Strings.Ban_Forever : Local(Ban.ExpiresAt);
    public bool HasRemaining => IsActive && Ban.ExpiresAt is not null;
    public string RemainingText => HasRemaining ? Formats.Duration(Ban.ExpiresAt!.Value - now) : "";
    public string AttemptsText => Ban.BlockedAttempts == 0 ? Strings.Ui_None
        : string.Format(Strings.Ui_BanAttemptsFmt, Ban.BlockedAttempts, Local(Ban.LastAttempt), Ban.LastAttemptIp ?? Unknown);

    /// <summary>Case-insensitive, in nickname, fingerprint, IP, reason, creator and lifter.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || new[] { Nickname, Fingerprint, Ban.Ip, Ban.Reason, Ban.CreatedBy, Ban.LiftedBy }.Any(text => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Package 76: the list is readable with BansView, lifting a ban needs UserBan; Package 80: only active ones.</summary>
    public bool CanUnban { get; } = canUnban && EvaluateStatus(ban, now) == BanStatus.Active;

    [RelayCommand(CanExecute = nameof(CanUnban))]
    Task UnbanAsync() => unban();
}
