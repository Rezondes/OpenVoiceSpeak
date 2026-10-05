using OVS.Client.Collections;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.ViewModels;

/// <summary>Package 71: one card of the user overview with everything the server stores about the user (A86).</summary>
/// <param name="busy">Package 98: the card's actions, running until a user list shows their result.</param>
public sealed partial class KnownUserViewModel(KnownUserInfo info, bool isOnline, IReadOnlyList<GroupToggleViewModel> toggles, IReadOnlyList<BanInfo> bans,
    AdminViewModel? owner = null, PendingViewModel? busy = null, bool canBan = false, bool canUnban = false, bool canDelete = false, bool canLiftMute = false) : IMotionKey
{
    public KnownUserInfo Info { get; } = info;
    public object MotionKey => Info.Fingerprint;
    public PendingViewModel Busy { get; } = busy ?? new PendingViewModel(TimeProvider.System);
    public string Fingerprint => Info.Fingerprint;
    public string Nickname => Info.LastNickname;
    public string ShortFingerprint => Fingerprint.Length > 16 ? Fingerprint[..16] : Fingerprint;
    public IReadOnlyList<GroupToggleViewModel> Toggles { get; } = toggles;
    public bool IsOnline { get; } = isOnline;
    public string StatusText => IsOnline ? Strings.Ui_StatusOnline : Strings.Ui_StatusOffline;
    public IReadOnlyList<BanInfo> Bans { get; } = bans;
    public bool IsBanned => Bans.Count > 0;

    // Package 72: only the actions the viewer may take show on the card
    public bool CanBan { get; } = canBan;
    public bool CanUnban { get; } = canUnban;
    public bool CanDelete { get; } = canDelete;
    /// <summary>Package 85: the stored server mute, applied again on every login.</summary>
    public bool IsServerMuted => Info.ServerMuted;
    public bool CanLiftMute { get; } = canLiftMute;
    public bool HasActions => CanBan || CanUnban || CanDelete || CanLiftMute;

    [RelayCommand(CanExecute = nameof(CanLiftMute))]
    Task LiftMute() => owner?.LiftMuteAsync(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBan))]
    Task Ban() => owner?.BanUserAsync(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanUnban))]
    Task Unban() => owner?.UnbanUserAsync(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    Task Delete() => owner?.DeleteUserAsync(this) ?? Task.CompletedTask;

    /// <summary>The longest running ban.</summary>
    public string BanText => Bans.OrderBy(b => b.ExpiresAt ?? DateTimeOffset.MaxValue).LastOrDefault() is not { } ban ? ""
        : string.Format(Strings.Ui_BanDetail, ban.ExpiresAt is { } until ? string.Format(Strings.Ban_Until, until.ToLocalTime()) : Strings.Ban_Forever)
          + (ban.Reason.Length > 0 ? ". " + string.Format(Strings.Ui_BanReason, ban.Reason) : "");

    /// <summary>Users saved before Package 70 have no statistics yet (LoginCount 0): shown as unknown.</summary>
    bool HasStats => Info.LoginCount > 0;
    static string Unknown => Strings.Ui_Unknown;
    public string FirstSeenText => Info.FirstSeen == default ? Unknown : Info.FirstSeen.ToLocalTime().ToString("g");
    public string LastLoginText => Info.LastLogin is { } at ? at.ToLocalTime().ToString("g") : Unknown;
    public string LoginCountText => HasStats ? Info.LoginCount.ToString() : Unknown;
    public string OnlineTimeText => HasStats ? Duration(Info.OnlineTime) : Unknown;
    public string SpeechTimeText => HasStats ? Duration(Info.SpeechTime) : Unknown;
    public string ChatMessagesText => HasStats ? Info.ChatMessages.ToString() : Unknown;
    public string LastIpText => Info.LastIp ?? Unknown;
    public string PreviousNicknamesText => Info.PreviousNicknames is { Count: > 0 } names ? string.Join(", ", names) : HasStats ? Strings.Ui_None : Unknown;

    /// <summary>Case-insensitive, in nickname, previous nicknames, fingerprint and IP.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || new[] { Nickname, Fingerprint, Info.LastIp ?? "" }.Concat(Info.PreviousNicknames ?? []).Any(v => v.Contains(search, StringComparison.OrdinalIgnoreCase));

    /// <summary>"45 s", "1 min 15 s", "1 h 30 min", "2 T 3 h".</summary>
    public static string Duration(TimeSpan t) => t switch
    {
        { TotalMinutes: < 1 } => $"{t.Seconds} s",
        { TotalHours: < 1 } => t.Seconds == 0 ? $"{t.Minutes} min" : $"{t.Minutes} min {t.Seconds} s",
        { TotalDays: < 1 } => t.Minutes == 0 ? $"{t.Hours} h" : $"{t.Hours} h {t.Minutes} min",
        _ => string.Format(Strings.Ui_DurationDays, (int)t.TotalDays, t.Hours),
    };
}
