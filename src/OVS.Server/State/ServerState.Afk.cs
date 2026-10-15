using OVS.Server.Logging;
using OVS.Shared.Permissions;

namespace OVS.Server.State;

/// <summary>Package 169: idle users go into the AFK channel, where nobody is heard.</summary>
internal sealed partial class ServerState
{
    /// <summary>The timer of the idle check while the AFK move is on, on the injected clock.</summary>
    ITimer? afkCheck;

    /// <summary>
    /// The AFK channel while the move is on: the chosen one, or the default channel when none is chosen or it is no
    /// longer a voice channel; null while the move is off.
    /// </summary>
    Guid? ActiveAfkChannel
    {
        get
        {
            if (data.Settings.AfkMinutes <= 0) return null;
            return data.Settings.AfkChannelId is { } chosen && FindChannel(chosen) is { } channel && ChannelAccess.IsJoinable(channel.Kind) ? chosen : data.DefaultChannelId;
        }
    }

    /// <summary>Runs the idle check every <see cref="ServerTuning.AfkCheckInterval"/> while the move is on, and stops it while it is off.</summary>
    void ScheduleAfkCheck()
    {
        if (data.Settings.AfkMinutes <= 0)
        {
            afkCheck?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }
        afkCheck ??= time.CreateTimer(_ =>
        {
            lock (gate) MoveIdleToAfk();
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        afkCheck.Change(ServerTuning.AfkCheckInterval, ServerTuning.AfkCheckInterval);
    }

    /// <summary>Moves every session idle for the set minutes into the AFK channel, muted and deafened ones too; one already there stays.</summary>
    void MoveIdleToAfk()
    {
        if (ActiveAfkChannel is not { } afk) return;
        var idleSince = time.GetUtcNow().AddMinutes(-data.Settings.AfkMinutes);
        foreach (var session in sessions.Values.Where(session => session.ChannelId != afk && session.LastActivity <= idleSince).ToList())
        {
            var from = session.ChannelId;
            #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
            ChannelLogger(from)?.LeftTo(session.Nickname, ChannelName(afk));
            ChannelLogger(afk)?.JoinedFrom(session.Nickname, ChannelName(from));
            logs.Server.MovedToAfk(session.Nickname, ChannelName(from), ChannelName(afk), data.Settings.AfkMinutes);
            #pragma warning restore CA1873
            session.ChannelId = afk;
            BroadcastUser(session);
        }
    }
}
