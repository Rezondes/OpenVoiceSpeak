using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;

namespace OVS.Manager.Control;

/// <summary>
/// Package M19 (A203 of PLAN.md): tells a server's users in its chat, as the service session, before a restart, a stop or
/// an update with a lead time and once more a minute before, lets it be cancelled, and sends a free text. A pending action
/// runs on its own in its own scope, so it does not end with the page that asked for it.
/// </summary>
internal sealed partial class Announcement(ServerConnections connections, IServiceScopeFactory scopes, TimeProvider time, ILogger<Announcement> logger)
{
    /// <summary>AC1: the lead times offered, in minutes; 0 is "sofort".</summary>
    public static readonly IReadOnlyList<int> LeadOptions = [0, 1, 5, 15];

    static readonly TimeSpan ReminderBefore = TimeSpan.FromMinutes(1);

    readonly ConcurrentDictionary<string, Pending> pending = new();

    /// <summary>A server's pending action came or went; the argument is the server's id.</summary>
    public event Action<string>? Changed;

    /// <summary>The pending action of <paramref name="serverId"/>, null when there is none.</summary>
    public PendingAnnouncement? PendingOf(string serverId) => pending.TryGetValue(serverId, out var entry) ? entry.Shown : null;

    /// <summary>
    /// AC1: announces <paramref name="action"/> in <paramref name="minutes"/> minutes (at least 1), reminds a minute before
    /// and then runs <paramref name="run"/>; false when an action is pending for the server already.
    /// </summary>
    public bool Schedule(string serverId, AnnouncedAction action, int minutes, Func<IServiceProvider, CancellationToken, Task> run)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        var entry = new Pending(new PendingAnnouncement(action, time.GetUtcNow() + TimeSpan.FromMinutes(minutes)));
        if (!pending.TryAdd(serverId, entry)) return false;
        Changed?.Invoke(serverId);
        entry.Running = RunAsync(serverId, entry, minutes, run);
        return true;
    }

    /// <summary>AC2: cancels the pending action of <paramref name="serverId"/> and says so in the chat; false when none was pending.</summary>
    public async Task<bool> CancelAsync(string serverId, CancellationToken cancellation = default)
    {
        if (!pending.TryRemove(serverId, out var entry)) return false;
        entry.Cancelled.TrySetResult();
        Changed?.Invoke(serverId);
        await SendAsync(serverId, CancelledText(entry.Shown.Action), cancellation);
        return true;
    }

    /// <summary>AC3: sends <paramref name="text"/> to the server chat and writes the audit line; returns null, or why it was not sent.</summary>
    public async Task<string?> SayAsync(string serverId, string text, string actor, CancellationToken cancellation = default)
    {
        text = text.Trim();
        if (text.Length == 0) return Strings.Announce_Empty;
        if (await SendAsync(serverId, text, cancellation) is { } failed) return failed;
        await using var scope = scopes.CreateAsyncScope();
        var name = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.Where(server => server.Id == serverId)
            .Select(server => server.Name).SingleOrDefaultAsync(cancellation);
        await scope.ServiceProvider.GetRequiredService<AuditLog>().WriteAsync(actor, AuditActions.ServerChatSent, name ?? serverId, serverId, text, cancellation);
        return null;
    }

    async Task RunAsync(string serverId, Pending entry, int minutes, Func<IServiceProvider, CancellationToken, Task> run)
    {
        var due = entry.Shown.DueUtc;
        try
        {
            await SendAsync(serverId, LeadText(entry.Shown.Action, minutes), CancellationToken.None);
            if (minutes > 1)
            {
                await WaitUntilAsync(due - ReminderBefore, entry.Cancelled.Task);
                await SendAsync(serverId, LeadText(entry.Shown.Action, 1), CancellationToken.None);
            }
            await WaitUntilAsync(due, entry.Cancelled.Task);
        }
        catch (OperationCanceledException)
        {
            return; // AC2: cancelled, CancelAsync says so
        }
        if (!pending.TryRemove(new KeyValuePair<string, Pending>(serverId, entry))) return; // cancelled just now
        Changed?.Invoke(serverId);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await run(scope.ServiceProvider, CancellationToken.None);
        }
        catch (Exception exception) when (exception is DockerCallException or ServerRequestException or PortInUseException)
        {
            LogFailed(logger, serverId, entry.Shown.Action, exception.Message);
        }
        Changed?.Invoke(serverId);
    }

    /// <summary>Waits until <paramref name="until"/>, at once when it is past (a clock moved on meanwhile); throws when <paramref name="cancelled"/> completes first.</summary>
    /// <exception cref="OperationCanceledException">The action was cancelled.</exception>
    async Task WaitUntilAsync(DateTimeOffset until, Task cancelled)
    {
        var wait = until - time.GetUtcNow();
        if (wait > TimeSpan.Zero) await Task.WhenAny(Task.Delay(wait, time), cancelled);
        if (cancelled.IsCompleted) throw new OperationCanceledException();
    }

    /// <summary>Sends <paramref name="text"/> to the server chat; null when the server took it, else why not.</summary>
    async Task<string?> SendAsync(string serverId, string text, CancellationToken cancellation)
    {
        if (connections.LinkOf(serverId) is not { Status: ServerLinkStatus.Connected } link) return Strings.Link_NotConnected;
        try
        {
            // the server sends the message back to its sender as well, which shows it went out
            var answer = await link.RequestAsync(new SendChat(ChatTarget.Server, null, text), message => message is ChatMessage { FromNickname: ServerLink.Nickname }, cancellation);
            return answer is Error error ? TextFormat.Of(Strings.Announce_Refused, error.Detail ?? error.Code) : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            return Strings.Link_NotConnected;
        }
    }

    static string LeadText(AnnouncedAction action, int minutes)
    {
        var lead = minutes == 1 ? Strings.Announce_OneMinute : TextFormat.Of(Strings.Announce_Minutes, minutes);
        return TextFormat.Of(action switch
        {
            AnnouncedAction.Restart => Strings.Announce_Restart,
            AnnouncedAction.Stop => Strings.Announce_Stop,
            _ => Strings.Announce_Update,
        }, lead);
    }

    static string CancelledText(AnnouncedAction action) => action switch
    {
        AnnouncedAction.Restart => Strings.Announce_RestartCancelled,
        AnnouncedAction.Stop => Strings.Announce_StopCancelled,
        _ => Strings.Announce_UpdateCancelled,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The announced {Action} of server {ServerId} failed: {Reason}")]
    static partial void LogFailed(ILogger logger, string serverId, AnnouncedAction action, string reason);

    /// <summary>A pending action with its cancel signal.</summary>
    sealed class Pending(PendingAnnouncement shown)
    {
        public PendingAnnouncement Shown => shown;

        /// <summary>Completed when the action is cancelled (AC2).</summary>
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The countdown and the action (kept so it is not lost).</summary>
        public Task? Running { get; set; }
    }
}
