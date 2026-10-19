using System.Net;
using System.Threading.Channels;
using OVS.Client.Exceptions;
using OVS.Shared.Exceptions;
using OVS.Shared.Identity;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.VoiceRelay;
using OVS.Shared.Voice;

namespace OVS.Client.Net;

/// <summary>
/// The client's connection: the shared TLS control connection (<see cref="ControlConnection"/>, Package 194) with TOFU
/// pinning of the server certificate, and voice over TCP on top of it (Package 141).
/// </summary>
internal sealed class ClientConnection : IAsyncDisposable
{
    readonly ControlConnection control;
    readonly CancellationTokenSource voiceStop = new(); // stopped on dispose before the control connection closes

    // Package 141: at most 10 frames (200 ms) wait for the TLS stream, in order; more is dropped
    readonly Channel<VoiceUp> voiceOut = Channel.CreateBounded<VoiceUp>(
        new BoundedChannelOptions(10) { FullMode = BoundedChannelFullMode.DropWrite }); // two readers: the loop and DropPendingVoice
    readonly Task voiceLoop;

    ClientConnection(ControlConnection control, byte[] voiceKey)
    {
        this.control = control;
        VoiceKey = voiceKey;
        control.MessageReceived += (_, received) =>
        {
            if (received.Message is VoiceDown voice) VoiceDownReceived?.Invoke(voice);
            else MessageReceived?.Invoke(received.Message);
        };
        control.Disconnected += (_, ended) => Disconnected?.Invoke(ended.Reason, ended.Detail);
        voiceLoop = Task.Run(VoiceLoopAsync);
    }

    /// <summary>The server's welcome: the session id, the voice key and the server's state.</summary>
    public Welcome Welcome => control.Welcome;

    /// <summary>The welcome's voice key, decoded once at the handshake (always <see cref="VoiceCrypto.KeySize"/> bytes).</summary>
    internal byte[] VoiceKey { get; }
    /// <summary>Server address for UDP. Dual-mode TCP sockets report IPv4 servers as ::ffff:a.b.c.d, which UDP cannot use.</summary>
    public IPAddress RemoteAddress => control.RemoteAddress;
    /// <summary>The last ping's round trip, null before the first answer.</summary>
    public TimeSpan? LastRoundTrip => control.LastRoundTrip;

    /// <summary>Raised on the receive thread for every message after Welcome.</summary>
    public event Action<Message>? MessageReceived;

    /// <summary>Package 141: voice over TCP, raised on the receive thread and kept out of MessageReceived (the UI thread).</summary>
    public event Action<VoiceDown>? VoiceDownReceived;

    /// <summary>Raised once: server Disconnected reason, or ConnectionLost.</summary>
    public event Action<string, string?>? Disconnected;

    /// <summary>The clock of the ping's round trip.</summary>
    internal TimeProvider Time => control.Time;

    /// <param name="time">
    /// The clock of the ping's round trip (tests pass a manual one). The ping itself goes every 5 s of real time: it keeps
    /// the connection alive against the server's idle timeout, which runs in real time.
    /// </param>
    /// <param name="host">The server's host.</param>
    /// <param name="port">The server's port.</param>
    /// <param name="identity">The own key pair.</param>
    /// <param name="nickname">The nickname to log in with.</param>
    /// <param name="password">The server password, null for none.</param>
    /// <param name="known">The trusted certificates.</param>
    /// <param name="confirm">Asks the user about an unknown or changed certificate.</param>
    /// <param name="cancellation">Ends the attempt.</param>
    public static async Task<ClientConnection> ConnectAsync(string host, int port, ClientIdentity identity, string nickname,
        string? password, TrustedServerFingerprints known, Func<TofuPrompt, Task<bool>> confirm, TimeProvider time, CancellationToken cancellation = default)
    {
        async Task CheckAsync(string fingerprint)
        {
            var check = known.Check(host, port, fingerprint);
            if (check == TofuResult.Known) return;
            if (!await confirm(new TofuPrompt(host, port, fingerprint, check))) throw new TofuRejectedException();
            known.Trust(host, port, fingerprint);
        }

        var control = await ControlConnection.ConnectAsync(host, port, identity, nickname, password, service: false, CheckAsync, time, cancellation);
        try
        {
            return new ClientConnection(control, ReadVoiceKey(control.Welcome));
        }
        catch
        {
            await control.DisposeAsync();
            throw;
        }
    }

    /// <summary>The welcome's voice key; anything but base64 of exactly <see cref="VoiceCrypto.KeySize"/> bytes fails the connect.</summary>
    internal static byte[] ReadVoiceKey(Welcome welcome) =>
        Base64Text.TryDecode(welcome.VoiceKey, out var key) && key.Length == VoiceCrypto.KeySize
            ? key
            : throw new ProtocolException("Server sent a malformed voice key");

    /// <summary>Sends one message.</summary>
    public Task SendAsync(Message message) => control.SendAsync(message);

    /// <summary>Package 141 (A150): voice over the TLS connection from the audio thread; never waits, drops while the stream is behind.</summary>
    public void SendVoice(VoiceUp up) => voiceOut.Writer.TryWrite(up);

    /// <summary>Package 141: voice went back to UDP; what still waits for TCP is dropped, so it cannot arrive after the switch.</summary>
    public void DropPendingVoice()
    {
        while (voiceOut.Reader.TryRead(out _))
        {
        }
    }

    async Task VoiceLoopAsync()
    {
        try
        {
            await foreach (var up in voiceOut.Reader.ReadAllAsync(voiceStop.Token)) await control.SendAsync(up, voiceStop.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // the connection is going; its end is reported by the receive loop
        }
    }

    /// <summary>Closes the connection: voice stops first, so nothing is written after the close (Package 141).</summary>
    public async ValueTask DisposeAsync()
    {
        if (voiceStop.IsCancellationRequested) return; // a second dispose
        await voiceStop.CancelAsync();
        await voiceLoop;
        await control.DisposeAsync();
        voiceStop.Dispose();
    }
}
