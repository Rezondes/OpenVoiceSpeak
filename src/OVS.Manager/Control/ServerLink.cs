using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Servers;
using OVS.Shared.Exceptions;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Manager.Control;

/// <summary>
/// Package M11 (A205, PLAN 191, 194): the manager's invisible service session to one server, as its server admin, with
/// the server's state as a mirror. It reconnects with backoff after a loss, pins the server's certificate at the first
/// connect and stops when it changes, and backs off for minutes from a server of another protocol.
/// </summary>
internal sealed partial class ServerLink : IAsyncDisposable
{
    /// <summary>D14 2: the service session's nickname, reserved for it.</summary>
    public const string Nickname = "Serververwaltung";

    /// <summary>The first wait after a loss; it doubles up to <see cref="MaxBackoff"/>.</summary>
    public static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

    /// <summary>AC1: the longest wait between two connects, so a started server is reached within 10 s.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);

    /// <summary>Package M12: how long a request waits for the server's answer.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>AC2: how long an outdated server is left alone before the next try (it may have been updated).</summary>
    public static readonly TimeSpan OutdatedRetry = TimeSpan.FromMinutes(5);

    readonly string serverId;
    readonly IServiceScopeFactory scopes;
    readonly ManagerOptions options;
    readonly TimeProvider time;
    readonly ILogger logger;
    readonly Lock gate = new();
    readonly CancellationTokenSource stop = new();
    Task running = Task.CompletedTask;
    readonly List<PendingRequest> pending = [];
    StateMirror? mirror;
    ControlConnection? connection;

    /// <summary>A link to <paramref name="serverId"/>; <see cref="Start"/> begins it.</summary>
    public ServerLink(string serverId, IServiceScopeFactory scopes, ManagerOptions options, TimeProvider time, ILogger logger)
    {
        this.serverId = serverId;
        this.scopes = scopes;
        this.options = options;
        this.time = time;
        this.logger = logger;
    }

    /// <summary>Raised when the status or the mirror changed; on any thread.</summary>
    public event Action? Changed;

    /// <summary>The server's id.</summary>
    public string ServerId => serverId;

    /// <summary>The link's state.</summary>
    public ServerLinkStatus Status { get; private set; }

    /// <summary>AC2: the protocol version an outdated server speaks, when it said so.</summary>
    public int? OutdatedProtocol { get; private set; }

    /// <summary>AC3: the changed certificate's fingerprint waiting for the admin.</summary>
    public string? OfferedCertificate { get; private set; }

    /// <summary>Begins connecting.</summary>
    public void Start() => running = RunAsync(stop.Token);

    /// <summary>AC4: reads the mirror under its lock; <paramref name="otherwise"/> while not connected.</summary>
    public T Read<T>(Func<StateMirror, T> read, T otherwise)
    {
        lock (gate) return mirror is null ? otherwise : read(mirror);
    }

    /// <summary>Sends <paramref name="message"/> through the service session (M12 to M16).</summary>
    /// <exception cref="InvalidOperationException">The link is not connected.</exception>
    public Task SendAsync(Message message, CancellationToken cancellation = default)
    {
        ControlConnection? open;
        lock (gate) open = connection;
        return open?.SendAsync(message, cancellation) ?? throw new InvalidOperationException($"Server {serverId} is not connected.");
    }

    /// <summary>
    /// Package M12: sends <paramref name="request"/> with a new request id and returns the server's answer: its
    /// <see cref="Error"/>, a list carrying the id (M13), or the message <paramref name="done"/> accepts.
    /// </summary>
    /// <exception cref="InvalidOperationException">The link is not connected.</exception>
    /// <exception cref="TimeoutException">No answer within <see cref="RequestTimeout"/>.</exception>
    public async Task<Message> RequestAsync(Request request, Func<Message, bool>? done = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = new PendingRequest(Guid.NewGuid().ToString("N"), done);
        lock (gate) pending.Add(answer);
        try
        {
            await SendAsync(request with { RequestId = answer.RequestId }, cancellation);
            return await answer.Result.Task.WaitAsync(RequestTimeout, time, cancellation);
        }
        finally
        {
            lock (gate) pending.Remove(answer);
        }
    }

    /// <summary>
    /// Package M13: sends <paramref name="action"/>, which the server confirms to nobody, and then a cheap request it
    /// always answers (an avatar that does not exist; lists are throttled to 2 a second); the server answers in order,
    /// so that answer shows the action was taken. Returns the action's <see cref="Error"/> when it was refused, else null.
    /// </summary>
    /// <exception cref="InvalidOperationException">The link is not connected.</exception>
    /// <exception cref="TimeoutException">No answer within <see cref="RequestTimeout"/>.</exception>
    public async Task<Error?> RequestConfirmedAsync(Request action, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var actionAnswer = new PendingRequest(Guid.NewGuid().ToString("N"), null);
        var barrier = new PendingRequest(Guid.NewGuid().ToString("N"), null);
        lock (gate) pending.AddRange([actionAnswer, barrier]);
        try
        {
            await SendAsync(action with { RequestId = actionAnswer.RequestId }, cancellation);
            await SendAsync(new GetAvatar(AvatarHash.Of([])) { RequestId = barrier.RequestId }, cancellation);
            await barrier.Result.Task.WaitAsync(RequestTimeout, time, cancellation);
            return actionAnswer.Result.Task.IsCompleted ? await actionAnswer.Result.Task as Error : null;
        }
        finally
        {
            lock (gate)
            {
                pending.Remove(actionAnswer);
                pending.Remove(barrier);
            }
        }
    }

    /// <summary>Ends the session and stops connecting.</summary>
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
        }
        stop.Dispose();
    }

    async Task RunAsync(CancellationToken cancellation)
    {
        await Task.Yield();
        var backoff = FirstBackoff;
        while (!cancellation.IsCancellationRequested)
        {
            var wait = backoff;
            try
            {
                await using var open = await ConnectAsync(cancellation);
                backoff = FirstBackoff;
                await FollowAsync(open, cancellation);
            }
            catch (ConnectionRejectedException rejected) when (rejected.Code == ReasonCodes.VersionMismatch)
            {
                OutdatedProtocol = int.TryParse(rejected.Detail?.Split(' ')[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
                SetStatus(ServerLinkStatus.Outdated);
                wait = OutdatedRetry;
            }
            catch (CertificateChangedException changed)
            {
                OfferedCertificate = changed.Fingerprint;
                SetStatus(ServerLinkStatus.CertificateChanged);
                LogCertificateChanged(logger, serverId);
                return; // AC3: until the admin accepts it, which starts a new link
            }
            catch (Exception exception) when (exception is IOException or SocketException or AuthenticationException or ConnectionRejectedException
                                                  or ProtocolException or InvalidOperationException
                                              || (exception is OperationCanceledException && !cancellation.IsCancellationRequested))
            {
                SetStatus(ServerLinkStatus.Connecting);
            }
            await Task.Delay(wait, time, cancellation);
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    /// <summary>Connects as the server admin's service session with the server's own identity of the manager (MA4).</summary>
    async Task<ControlConnection> ConnectAsync(CancellationToken cancellation)
    {
        await using var scope = scopes.CreateAsyncScope();
        var server = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == serverId, cancellation)
                     ?? throw new InvalidOperationException($"Server {serverId} is gone.");
        using var identity = await scope.ServiceProvider.GetRequiredService<ServerIdentityStore>().LoadAsync(serverId, cancellation);
        return await ControlConnection.ConnectAsync(options.HostOf(serverId), server.Port, identity, Nickname, null, service: true,
            fingerprint => CheckCertificateAsync(server.CertificateFingerprint, fingerprint, cancellation), time, cancellation);
    }

    /// <summary>AC3: the first certificate is pinned; another one later stops the link.</summary>
    async Task CheckCertificateAsync(string? pinned, string fingerprint, CancellationToken cancellation)
    {
        if (pinned == fingerprint) return;
        if (pinned is not null) throw new CertificateChangedException($"Server {serverId} shows another certificate.") { Fingerprint = fingerprint };
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.Where(row => row.Id == serverId && row.CertificateFingerprint == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CertificateFingerprint, fingerprint), cancellation);
    }

    /// <summary>AC4: keeps the mirror on the server's messages until the session ends.</summary>
    async Task FollowAsync(ControlConnection open, CancellationToken cancellation)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(object? sender, MessageReceivedEventArgs received)
        {
            bool changed;
            List<PendingRequest> answered;
            lock (gate)
            {
                changed = mirror?.Apply(received.Message) ?? false;
                answered = [.. pending.Where(request => request.Answers(received.Message))];
            }
            if (changed) Changed?.Invoke();
            foreach (var request in answered) request.Result.TrySetResult(received.Message);
        }
        void OnDisconnected(object? sender, DisconnectedEventArgs disconnected) => ended.TrySetResult();
        open.MessageReceived += OnMessage;
        open.Disconnected += OnDisconnected;
        lock (gate)
        {
            mirror = new StateMirror(open.Welcome);
            connection = open;
        }
        OutdatedProtocol = null;
        SetStatus(ServerLinkStatus.Connected);
        try
        {
            await ended.Task.WaitAsync(cancellation);
        }
        finally
        {
            open.MessageReceived -= OnMessage;
            open.Disconnected -= OnDisconnected;
            lock (gate)
            {
                mirror = null;
                connection = null;
            }
            if (!cancellation.IsCancellationRequested) SetStatus(ServerLinkStatus.Connecting);
        }
    }

    void SetStatus(ServerLinkStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {ServerId} shows another certificate; the service session waits for the admin")]
    static partial void LogCertificateChanged(ILogger logger, string serverId);

    /// <summary>Package M12: a request waiting for its answer: an <see cref="Error"/> or list with its id, or the message that shows it was done.</summary>
    sealed class PendingRequest(string requestId, Func<Message, bool>? done)
    {
        /// <summary>The request's id.</summary>
        public string RequestId => requestId;

        /// <summary>The answer.</summary>
        public TaskCompletionSource<Message> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>True when <paramref name="message"/> ends the request.</summary>
        public bool Answers(Message message) => message switch
        {
            Error error => error.RequestId == requestId,
            UserList list => list.RequestId == requestId,
            BanList list => list.RequestId == requestId,
            Avatar avatar => avatar.RequestId == requestId,
            _ => done?.Invoke(message) ?? false,
        };
    }
}
