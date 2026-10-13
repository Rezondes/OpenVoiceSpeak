using System.Collections.ObjectModel;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Sounds;
using OVS.Client.Collections;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.ViewModels;

/// <summary>
/// The sidebar's channel tree of one server: the channel and user rows, their order (Packages 36 and 98) and their
/// speaking marks (Packages 57, 60 and 122). The rows still call their requests on the server view model that owns the tree.
/// </summary>
internal sealed class ChannelTreeViewModel
{
    readonly ServerViewModel owner;
    readonly Action<SoundEvent> requestSound;
    readonly Dictionary<Guid, ChannelViewModel> channelVms = [];
    readonly Dictionary<uint, UserViewModel> userVms = [];
    /// <summary>Packages 57, 60 and 122: who is shown speaking and when a link tone is due.</summary>
    readonly SpeakingMarks speakingMarks;
    /// <summary>The order sent last, shown until the server confirms or refuses it.</summary>
    List<Guid>? pendingOrder;

    /// <param name="owner">The server whose mirror, rights, leave timer, clock and requests the tree uses.</param>
    /// <param name="requestSound">Plays a tone (the link tones).</param>
    internal ChannelTreeViewModel(ServerViewModel owner, Action<SoundEvent> requestSound)
    {
        this.owner = owner;
        this.requestSound = requestSound;
        speakingMarks = new SpeakingMarks(owner.Time);
    }

    /// <summary>The channel rows in the server's order (a channel that is gone folds away first, Package 102).</summary>
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

    /// <summary>Package 122: how often a user's view model was updated, so tests can count the work a message does.</summary>
    internal int UserUpdates { get; set; }

    /// <summary>Package 122: how often a user's speaking state was set (tests count it).</summary>
    internal int SpeakingTouches { get; private set; }

    /// <summary>Every user row known, also one still folding away.</summary>
    internal IEnumerable<UserViewModel> Users => userVms.Values;

    StateMirror Mirror => owner.Mirror;

    /// <summary>The row of the channel <paramref name="channelId"/>, also one still folding away; null when there is none.</summary>
    internal ChannelViewModel? FindChannel(Guid channelId) => channelVms.GetValueOrDefault(channelId);

    /// <summary>The row of the session <paramref name="sessionId"/>, also one still folding away; null when there is none.</summary>
    internal UserViewModel? FindUser(uint sessionId) => userVms.GetValueOrDefault(sessionId);

    /// <summary>Package 122: one user changed; updates that row and the lists it leaves or comes into, and returns what changed.</summary>
    internal StateChange ShowUser(UserInfo? now, UserInfo? before)
    {
        var change = StateChange.None;
        if (now is not null)
        {
            if (!userVms.TryGetValue(now.SessionId, out var user)) userVms[now.SessionId] = user = new UserViewModel(owner, now.SessionId);
            user.Update(now, now.SessionId == Mirror.SelfId, owner.SelfPermissions, Mirror.Groups);
            user.UpdateMoveTargets(Channels.Live().ToList());
        }
        if (now is null || before is null) change |= StateChange.Members;
        if (now?.Nickname != before?.Nickname) change |= StateChange.Names; // also its place in the list
        if (now?.ChannelId != before?.ChannelId || (change.HasFlag(StateChange.Names) && now?.SessionId == Mirror.SelfId))
            change |= StateChange.Sidebar; // the counts, or my name below the tree
        if ((change & (StateChange.Names | StateChange.Sidebar)) != 0)
            foreach (var id in new[] { before?.ChannelId, now?.ChannelId }.OfType<Guid>().Distinct()) ShowUsers(id);
        if (now is null) ForgetGoneUsers();
        RefreshSpeaking(); // a new row shows whether its user talks
        return change;
    }

    /// <summary>Builds the rows from the mirror: every channel in the server's order (or the order sent last) with its users.</summary>
    internal void Rebuild(UserInfo? self)
    {
        var groups = Mirror.Groups;
        // Package 102: one that is still folding away stays known, so coming back meanwhile is the same entry again
        foreach (var id in channelVms.Keys.Except(Mirror.Channels.Keys).Where(id => !Channels.Contains(channelVms[id])).ToList()) channelVms.Remove(id);
        ForgetGoneUsers();

        var byChannel = Mirror.Users.Values.ToLookup(user => user.ChannelId); // Package 122: not every user per channel
        var desired = new List<ChannelViewModel>();
        foreach (var channelInfo in ServerOrder().OrderBy(channel => pendingOrder?.IndexOf(channel.Id) is >= 0 and var index ? index : int.MaxValue)) // Package 98
        {
            if (!channelVms.TryGetValue(channelInfo.Id, out var channel)) channelVms[channelInfo.Id] = channel = new ChannelViewModel(owner, channelInfo.Id);
            var linked = Mirror.LinkedChannels(channelInfo.Id).Select(id => Mirror.Channels.GetValueOrDefault(id)?.Name).OfType<string>().Order().ToList();
            channel.Update(channelInfo, linked, channelInfo.Id == Mirror.DefaultChannelId, self?.ChannelId == channelInfo.Id, owner.SelfPermissions);
            channel.UpdateLock(channelInfo, groups, self?.GroupIds ?? []);

            ShowUsers(channel, channelInfo, byChannel[channelInfo.Id], userInfo =>
            {
                if (!userVms.TryGetValue(userInfo.SessionId, out var user)) userVms[userInfo.SessionId] = user = new UserViewModel(owner, userInfo.SessionId);
                user.Update(userInfo, userInfo.SessionId == Mirror.SelfId, owner.SelfPermissions, groups);
                return user;
            });
            desired.Add(channel);
        }
        CollectionSync.Sync(Channels, desired, owner.Leave);
        for (int index = 0; index < desired.Count; index++) desired[index].SetPosition(first: index == 0, last: index == desired.Count - 1);
        foreach (var user in userVms.Values) user.UpdateMoveTargets(desired); // Package 120: in the new order of the tree
    }

    /// <summary>Package 111: the channels <paramref name="channel"/> can be linked with.</summary>
    public IReadOnlyList<ChannelViewModel> LinkCandidates(ChannelViewModel channel)
    {
        var linked = Mirror.LinkedChannels(channel.Id).ToHashSet();
        return Channels.Live().Where(channelViewModel => channelViewModel != channel && !channelViewModel.IsSeparator && !linked.Contains(channelViewModel.Id)).ToList(); // Package 111
    }

    /// <summary>The channels <paramref name="channel"/> is linked with.</summary>
    public IReadOnlyList<ChannelViewModel> LinkedChannelVms(ChannelViewModel channel)
    {
        var linked = Mirror.LinkedChannels(channel.Id).ToHashSet();
        return Channels.Live().Where(channelViewModel => linked.Contains(channelViewModel.Id)).ToList();
    }

    // ---- Speaking indicators ----

    /// <summary>The engine heard these speakers: a link tone for someone talking in over a link, then the rows (Package 57).</summary>
    public void OnSpeakers(IEnumerable<ActiveSpeaker> active)
    {
        foreach (var speaker in active)
        {
            if (speakingMarks.Hear(speaker, speaker.SessionId != Mirror.SelfId && Mirror.Users.ContainsKey(speaker.SessionId)))
                requestSound(SoundEvent.LinkVoice);
        }
        RefreshSpeaking();
    }

    /// <summary>Package 60: the own voice goes to this target (null: silent); its link tone, then the rows.</summary>
    public void SetSelfTransmitting(byte? target)
    {
        if (speakingMarks.SetOwnTarget(target, owner.CurrentChannel is { IsLinked: true })) requestSound(SoundEvent.OwnLinkVoice);
        RefreshSpeaking();
    }

    /// <summary>Package 122: shows who speaks; a row is touched only when its state changed.</summary>
    public void RefreshSpeaking()
    {
        foreach (var state in speakingMarks.Refresh(Mirror.SelfId))
        {
            if (!userVms.TryGetValue(state.SessionId, out var user)
                || (user.IsSpeaking == (state.On && !state.ViaLink) && user.IsSpeakingViaLink == (state.On && state.ViaLink))) continue;
            user.SetSpeaking(state.On, state.ViaLink);
            SpeakingTouches++;
        }
    }

    // ---- Order ----

    /// <summary>
    /// Package 36: puts source right before or after target and sends the complete new order. Package 98: the new order
    /// shows at once and the moved row spins until the server's updates match it; refused or unanswered, it goes back.
    /// </summary>
    public Task MoveChannelAsync(ChannelViewModel source, ChannelViewModel target, bool after)
    {
        if (source == target) return Task.CompletedTask;
        var live = Channels.Live().ToList();
        if (!live.Contains(source) || !live.Contains(target)) return Task.CompletedTask; // Package 102: folding away
        var order = live.Where(channel => channel != source).ToList();
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        if (order.SequenceEqual(live)) return Task.CompletedTask;
        var ids = order.Select(channel => channel.Id).ToList();
        pendingOrder = ids;
        owner.Rebuild();
        var sent = owner.SendConfirmed(new ReorderChannels(ids), _ => ServerOrder().Select(channel => channel.Id).SequenceEqual(ids), busy: on => source.IsBusy = on);
        _ = EndReorderAsync(sent, ids);
        return Task.CompletedTask;
    }

    async Task EndReorderAsync(PendingActionViewModel sent, List<Guid> ids)
    {
        await sent.Completion;
        if (pendingOrder != ids) return; // a newer order is on its way
        pendingOrder = null;
        owner.Rebuild(); // done: the server's order is the same; refused or no answer: back to it
    }

    IEnumerable<ChannelInfo> ServerOrder() => Mirror.Channels.Values.OrderBy(channel => channel.Order).ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Package 122: one channel's list, from the users' view models as they are.</summary>
    void ShowUsers(Guid channelId)
    {
        if (!channelVms.TryGetValue(channelId, out var channel) || Mirror.Channels.GetValueOrDefault(channelId) is not { } info) return;
        ShowUsers(channel, info, Mirror.Users.Values.Where(user => user.ChannelId == channelId), user => userVms[user.SessionId]);
    }

    /// <summary>The users inside a channel by name into its list (Package 102: one who left folds away) and its slot counter.</summary>
    void ShowUsers(ChannelViewModel channel, ChannelInfo info, IEnumerable<UserInfo> inside, Func<UserInfo, UserViewModel> viewModelOf)
    {
        var users = inside.OrderBy(user => user.Nickname, StringComparer.CurrentCultureIgnoreCase).Select(viewModelOf).ToList();
        CollectionSync.Sync(channel.Users, users, owner.Leave);
        channel.Slots = new ChannelSlots(users.Count, info.MaxUsers);
    }

    /// <summary>Package 102: one that is still folding away stays known, so coming back meanwhile is the same entry again.</summary>
    void ForgetGoneUsers()
    {
        var shownUsers = Channels.SelectMany(channel => channel.Users).ToHashSet();
        foreach (var id in userVms.Keys.Except(Mirror.Users.Keys).Where(id => !shownUsers.Contains(userVms[id])).ToList()) userVms.Remove(id);
    }
}
