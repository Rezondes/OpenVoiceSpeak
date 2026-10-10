using OVS.Client.Net;
using OVS.Shared.Voice;

namespace OVS.Client.Services;

/// <summary>
/// The voice and UDP lifetime of the current connection: the voice client the audio engine sends through and hears from,
/// the log of whether UDP gets through, and the loss adapter's feed (the uplink figure of each new answer, the streaks ended
/// with each connection). It reaches the log and the adapter through delegates and knows no view model.
/// </summary>
internal sealed class VoiceLink
{
    readonly Action<uint, uint, byte, byte[], byte[]?> hear;
    readonly Action<Action<byte[], byte>?> setSend;
    readonly TimeProvider time;
    readonly Action<string> log;
    readonly Action<LossReport> reportUplink;
    readonly Action endLossStreaks;
    long connectedAt; // a timestamp of time
    bool? udpLogged; // null: nothing logged yet; true: "reachable" was logged last; false: "not reachable" was
    bool udpWasReachable; // Package 126
    int answersFed; // the answers of the client whose uplink figure the loss adapter has had

    /// <param name="hear">Takes what the voice client receives (speaker, frame number, target, Opus frame, the copy of the frame before).</param>
    /// <param name="setSend">Sets the engine's send to the voice client's, and to null when the client goes.</param>
    /// <param name="time">The clock of the 5 s without an answer.</param>
    /// <param name="log">Where the reachability lines are written.</param>
    /// <param name="reportUplink">Package 135: tells the loss adapter the uplink figure of each new answer.</param>
    /// <param name="endLossStreaks">Ends the loss adapter's running streaks; a delegate because the adapter can be replaced.</param>
    internal VoiceLink(Action<uint, uint, byte, byte[], byte[]?> hear, Action<Action<byte[], byte>?> setSend, TimeProvider time, Action<string> log,
        Action<LossReport> reportUplink, Action endLossStreaks)
    {
        this.reportUplink = reportUplink;
        this.endLossStreaks = endLossStreaks;
        this.hear = hear;
        this.setSend = setSend;
        this.time = time;
        this.log = log;
    }

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
        answersFed = 0; // the new client counts its answers from 0
        endLossStreaks(); // Package 135: a reconnect into the same channel keeps no streak from before
    }

    /// <summary>
    /// Clears the engine's send first, then disposes and forgets the voice client, if there is one, and ends the loss
    /// adapter's streaks (also without a client).
    /// </summary>
    internal void Stop()
    {
        setSend(null);
        Client?.Dispose();
        Client = null;
        endLossStreaks();
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
    /// Package 135: on every tick, hands the loss adapter the uplink figure of each new answer, once (by the answer count of the
    /// round trip), none while no answer comes and none without a voice client. A failing adapter throws on, but the answer
    /// counts as seen, so the next tick does not hand it the same figure again.
    /// </summary>
    internal void FeedLoss()
    {
        if (Client is not { } client) return;
        var (_, count) = client.RoundTripSample;
        if (count == answersFed) return;
        answersFed = count;
        reportUplink(client.UplinkLoss);
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
