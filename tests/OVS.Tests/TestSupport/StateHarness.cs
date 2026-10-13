using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Logging;
using OVS.Server.State;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;

namespace OVS.Tests.TestSupport;

/// <summary>
/// A <see cref="ServerState"/> without sockets: its own temporary data folder, a <see cref="ManualTimeProvider"/>, a
/// seeded <see cref="DataStore"/>, sessions admitted and requests handled directly, and what a session was sent read
/// decoded from its outbox.
/// </summary>
internal sealed class StateHarness : IDisposable
{
    int lastAddress;

    public StateHarness(Action<ServerData>? seed = null, ManualTimeProvider? time = null)
    {
        DataDir = Directory.CreateTempSubdirectory("ovs-harness-").FullName;
        Time = time ?? new ManualTimeProvider();
        var config = new ServerConfig(0, DataDir, 50, "Testserver", "");
        if (seed is not null)
        {
            var store = new DataStore(Path.Combine(DataDir, DataStore.FileName));
            var data = store.LoadOrCreate(() => ServerData.CreateDefault(config));
            seed(data);
            store.Save(data);
        }
        State = new ServerState(config, Time, new ServerLogWriter(DataDir, config.LogDays, Time, Log.Enqueue), ServerStores.Open(config, Time));
    }

    public string DataDir { get; }
    public ManualTimeProvider Time { get; }
    public ServerState State { get; }
    public ConcurrentQueue<string> Log { get; } = new();

    /// <summary>
    /// A session as after a handshake. Without <paramref name="ip"/> every session comes from an address of its own, so
    /// the limit of new identities per address never counts; without <paramref name="fingerprint"/> it is a new identity.
    /// </summary>
    public Session Admit(string nickname, string? fingerprint = null, IPAddress? ip = null, string? password = null)
    {
        fingerprint ??= Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        ip ??= new IPAddress([10, 0, (byte)(++lastAddress >> 8), (byte)lastAddress]);
        var (session, rejection) = State.Admit(fingerprint, nickname, ip, password);
        Assert.True(session is not null, $"Admit refused: {rejection}");
        return session;
    }

    public void Handle(Session session, Request request) => State.Handle(session, request);

    /// <summary>Changes the stored data under the server's lock and applies the rights to the online sessions, as after a group change.</summary>
    public void ChangeStoredData(Action<ServerData> change) => ChangeStoredData(State, change);

    /// <inheritdoc cref="ChangeStoredData(Action{ServerData})"/>
    public static void ChangeStoredData(ServerState state, Action<ServerData> change) => state.ChangeStoredData(change);

    /// <summary>Everything queued for the session so far, decoded, oldest first.</summary>
    public static List<Message> Drain(Session session)
    {
        var messages = new List<Message>();
        while (session.Outbox.Outgoing.TryRead(out var frame)) messages.Add(Decode(session, frame));
        return messages;
    }

    /// <summary>
    /// The next message of type <typeparamref name="T"/> that matches, for answers from the thread pool (log jobs);
    /// the messages before it are dropped. Fails after 10 s.
    /// </summary>
    public static async Task<T> WaitForAsync<T>(Session session, Func<T, bool>? match = null) where T : Message
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (Decode(session, await session.Outbox.Outgoing.ReadAsync(timeout.Token)) is T message && (match is null || match(message))) return message;
        }
    }

    static Message Decode(Session session, byte[] frame)
    {
        session.Outbox.Written(frame);
        return JsonSerializer.Deserialize<Message>(frame.AsSpan(4), ProtocolJson.Options)!;
    }

    public void Dispose()
    {
        State.CloseAll(new Disconnected(ReasonCodes.ServerShutdown));
        try
        {
            Directory.Delete(DataDir, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a log job may still hold a file open; the folder is in the temp directory
        }
    }
}
