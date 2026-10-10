using System.Net;
using OVS.Client.Net;

namespace OVS.Client.Services;

/// <summary>
/// The voice and UDP lifetime of the current connection: the voice client the audio engine sends through and hears from,
/// and the log of whether UDP gets through. It reaches the log through a delegate and knows no view model.
/// </summary>
internal sealed class VoiceLink
{
    readonly Action<uint, uint, byte, byte[], byte[]?> hear;
    readonly Action<Action<byte[], byte>?> setSend;
    readonly TimeProvider time;
    readonly Action<string> log;
    long connectedAt; // a timestamp of time
    bool? udpLogged; // null: nothing logged yet; true: "reachable" was logged last; false: "not reachable" was
    bool udpWasReachable; // Package 126

    /// <param name="hear">Takes what the voice client receives (speaker, frame number, target, Opus frame, the copy of the frame before).</param>
    /// <param name="setSend">Sets the engine's send to the voice client's, and to null when the client goes.</param>
    /// <param name="time">The clock of the 5 s without an answer.</param>
    /// <param name="log">Where the reachability lines are written.</param>
    internal VoiceLink(Action<uint, uint, byte, byte[], byte[]?> hear, Action<Action<byte[], byte>?> setSend, TimeProvider time, Action<string> log)
    {
        this.hear = hear;
        this.setSend = setSend;
        this.time = time;
        this.log = log;
    }

    /// <summary>Package 126: tests send UDP through a lossy relay instead of to the server.</summary>
    internal IPEndPoint? Relay { get; set; }

    /// <summary>The current voice client, null while there is none; tests also wait on it.</summary>
    internal VoiceClient? Client { get; private set; }

    /// <summary>Whether UDP gets an answer; null without a voice client.</summary>
    internal bool? Reachable => Client?.Reachable;

    /// <summary>Package 141: voice goes over the control connection right now; false without a voice client.</summary>
    internal bool OverTcp => Client?.OverTcp == true;

    /// <summary>
    /// Takes over a voice client: what it receives goes to the engine (on the receive thread, unposted), the engine sends
    /// through it, it starts, and the reachability log starts over. The order matters: the capture thread reads the engine's send
    /// without a lock.
    /// </summary>
    internal void Start(VoiceClient client)
    {
        Client = client;
        client.VoiceReceived += hear;
        setSend(client.SendVoice);
        client.Start();
        connectedAt = time.GetTimestamp();
        udpLogged = null;
        udpWasReachable = false;
    }

    /// <summary>Clears the engine's send first, then disposes and forgets the voice client, if there is one.</summary>
    internal void Stop()
    {
        setSend(null);
        Client?.Dispose();
        Client = null;
    }

    /// <summary>
    /// Logs "reachable" once, or "not reachable" when nothing came back within 5 s (then "reachable" if it recovers).
    /// Package 126: UDP lost after it worked logs "not reachable" and, when it is back, "reachable again".
    /// </summary>
    internal void LogReachability()
    {
        if (Client is not { } client) return;
        if (client.Reachable)
        {
            if (udpLogged == true) return;
            log(udpWasReachable ? "UDP reachable again" : "UDP now reachable");
            udpLogged = udpWasReachable = true;
        }
        else if (udpLogged == true || (udpLogged is null && time.GetElapsedTime(connectedAt) > TimeSpan.FromSeconds(5)))
        {
            log("UDP not reachable: voice does not arrive (firewall or NAT?)");
            udpLogged = false;
        }
    }

    /// <summary>
    /// The voice connection's round trip and loss figures. The branch without a voice client is not reached in the app: the statistics
    /// card asks only behind its reachable gate, which is null without one.
    /// </summary>
    internal VoiceLinkFigures ReadFigures()
    {
        if (Client is not { } client) return default;
        var (roundTrip, count) = client.RoundTripSample;
        return new VoiceLinkFigures(roundTrip, count, client.UplinkLoss, client.DownlinkLoss);
    }
}
