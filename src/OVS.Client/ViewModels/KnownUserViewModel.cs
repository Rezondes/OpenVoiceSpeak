using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Localization;
using OVS.Client.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.ViewModels;

/// <summary>Package 71: one card of the user overview with everything the server stores about the user (A86).</summary>
/// <param name="busy">Package 98: the card's actions, running until a user list shows their result.</param>
/// <param name="info">The user as the server lists them.</param>
/// <param name="isOnline">The user is connected.</param>
/// <param name="toggles">The group boxes.</param>
/// <param name="bans">The active bans.</param>
/// <param name="actions">What the card's buttons do.</param>
/// <param name="canBan">The ban button shows.</param>
/// <param name="canUnban">The unban button shows.</param>
/// <param name="canDelete">The delete button shows.</param>
/// <param name="canLiftMute">The lift mute button shows.</param>
/// <param name="isExpanded">Package 155: the row starts with its details open (it was open before the list came again).</param>
/// <param name="expandedChanged">Package 155: told when the row opens or closes, so the list keeps it open.</param>
internal sealed partial class KnownUserViewModel(KnownUserInfo info, bool isOnline, IReadOnlyList<GroupAssignmentViewModel> toggles, IReadOnlyList<BanInfo> bans,
    KnownUserActions actions, PendingActionViewModel busy, bool canBan = false, bool canUnban = false, bool canDelete = false, bool canLiftMute = false,
    bool isExpanded = false, Action<bool>? expandedChanged = null) : ObservableObject, IMotionKey
{
    /// <summary>The user as the server lists them.</summary>
    public KnownUserInfo Info { get; } = info;
    /// <summary>The fingerprint: the same user keeps the motion when the list is rebuilt.</summary>
    public object MotionKey => Info.Fingerprint;
    /// <summary>The card's actions, running until a user list shows their result.</summary>
    public PendingActionViewModel Busy { get; } = busy;
    /// <summary>The user's id.</summary>
    public string Fingerprint => Info.Fingerprint;
    /// <summary>The nickname of the last login.</summary>
    public string Nickname => Info.LastNickname;
    /// <summary>The fingerprint's first 16 characters.</summary>
    public string ShortFingerprint => Fingerprint.Length > 16 ? Fingerprint[..16] : Fingerprint;
    /// <summary>The group boxes.</summary>
    public IReadOnlyList<GroupAssignmentViewModel> Toggles { get; } = toggles;
    /// <summary>The user is connected.</summary>
    public bool IsOnline { get; } = isOnline;
    /// <summary>Online or offline, as shown.</summary>
    public string StatusText => IsOnline ? Strings.Ui_StatusOnline : Strings.Ui_StatusOffline;
    /// <summary>The active bans.</summary>
    public IReadOnlyList<BanInfo> Bans { get; } = bans;
    /// <summary>The user has an active ban.</summary>
    public bool IsBanned => Bans.Count > 0;

    // Package 72: only the actions the viewer may take show on the card
    /// <summary>The ban button shows.</summary>
    public bool CanBan { get; } = canBan;
    /// <summary>The unban button shows.</summary>
    public bool CanUnban { get; } = canUnban;
    /// <summary>The delete button shows.</summary>
    public bool CanDelete { get; } = canDelete;
    /// <summary>Package 85: the stored server mute, applied again on every login.</summary>
    public bool IsServerMuted => Info.ServerMuted;

    /// <summary>Package 168: the user's picture on the card, null while there is none or it is still coming.</summary>
    [ObservableProperty] byte[]? avatarPng;

    /// <summary>Package 166: the mute banner's text, with the end of a timed mute.</summary>
    public string ServerMutedText => Info.ServerMutedUntil is { } until
        ? TextFormat.Of(Strings.Ui_ServerMutedUntil, until.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : Strings.Ui_ServerMuted;
    /// <summary>The lift mute button shows.</summary>
    public bool CanLiftMute { get; } = canLiftMute;
    /// <summary>The card has any button.</summary>
    public bool HasActions => CanBan || CanUnban || CanDelete || CanLiftMute;
    /// <summary>Package 156: the "..." menu has an entry (ban, delete).</summary>
    public bool HasMenuActions => CanBan || CanDelete;

    /// <summary>Package 155: the row shows its details, groups and actions.</summary>
    [ObservableProperty] bool isExpanded = isExpanded;

    partial void OnIsExpandedChanged(bool value) => expandedChanged?.Invoke(value);

    /// <summary>Package 155: a click or Enter on the row opens or closes its details.</summary>
    [RelayCommand]
    void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Package 155: the names of the user's groups, one chip each in the collapsed row.</summary>
    public IReadOnlyList<string> GroupNames => Toggles.Where(toggle => toggle.IsChecked).Select(toggle => toggle.Name).ToList();

    [RelayCommand(CanExecute = nameof(CanLiftMute))]
    Task LiftMuteAsync() => actions.LiftMute(this);

    [RelayCommand(CanExecute = nameof(CanBan))]
    Task BanAsync() => actions.Ban(this);

    [RelayCommand(CanExecute = nameof(CanUnban))]
    Task UnbanAsync() => actions.Unban(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    Task DeleteAsync() => actions.Delete(this);

    /// <summary>The longest running ban.</summary>
    public string BanText => Bans.OrderBy(banInfo => banInfo.ExpiresAt ?? DateTimeOffset.MaxValue).LastOrDefault() is not { } ban ? ""
        : TextFormat.Of(Strings.Ui_BanDetail, ban.ExpiresAt is { } until ? TextFormat.Of(Strings.Ban_Until, until.ToLocalTime()) : Strings.Ban_Forever)
          + (ban.Reason.Length > 0 ? ". " + TextFormat.Of(Strings.Ui_BanReason, ban.Reason) : "");

    /// <summary>Users saved before Package 70 have no statistics yet (LoginCount 0): shown as unknown.</summary>
    bool HasStats => Info.LoginCount > 0;
    static string Unknown => Strings.Ui_Unknown;
    /// <summary>The first login, local time.</summary>
    public string FirstSeenText => Info.FirstSeen == default ? Unknown : Info.FirstSeen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    /// <summary>The last login, local time.</summary>
    public string LastLoginText => Info.LastLogin is { } at ? at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : Unknown;
    /// <summary>How often the user logged in.</summary>
    public string LoginCountText => HasStats ? Info.LoginCount.ToString(CultureInfo.CurrentCulture) : Unknown;
    /// <summary>The time connected.</summary>
    public string OnlineTimeText => HasStats ? Formats.Duration(Info.OnlineTime) : Unknown;
    /// <summary>The time talked.</summary>
    public string SpeechTimeText => HasStats ? Formats.Duration(Info.SpeechTime) : Unknown;
    /// <summary>The chat messages written.</summary>
    public string ChatMessagesText => HasStats ? Info.ChatMessages.ToString(CultureInfo.CurrentCulture) : Unknown;
    /// <summary>The last address.</summary>
    public string LastIpText => Info.LastIp ?? Unknown;
    /// <summary>The earlier nicknames.</summary>
    public string PreviousNicknamesText => Info.PreviousNicknames is { Count: > 0 } names ? string.Join(", ", names) : HasStats ? Strings.Ui_None : Unknown;

    /// <summary>Case-insensitive, in nickname, previous nicknames, fingerprint and IP.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || new[] { Nickname, Fingerprint, Info.LastIp ?? "" }.Concat(Info.PreviousNicknames ?? []).Any(text => text.Contains(search, StringComparison.OrdinalIgnoreCase));
}
