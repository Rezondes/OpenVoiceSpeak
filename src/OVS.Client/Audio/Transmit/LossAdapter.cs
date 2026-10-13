using OVS.Shared.Protocol;
using OVS.Shared.Voice;

namespace OVS.Client.Audio.Transmit;

/// <summary>
/// Package 135 (A144): the sender's loss protection follows the uplink loss the server reports (every 2 s with the ping
/// answer). Opus is told to expect 1.5 times the loss (5 to 30 %) and adds that much FEC; above 10 % loss for 5 s the
/// bitrate halves (down to 16 kbps), after 10 s below 2 % it doubles again, at most to the channel's. Fed on one thread,
/// read on the capture thread.
/// </summary>
internal sealed class LossAdapter(TimeProvider time, int channelKbps)
{
    /// <summary>The range of the loss Opus is told to expect, in percent, and its start value; 10 was the fixed value before Package 135.</summary>
    public const int MinHint = 5, MaxHint = 30, InitialHint = 10;
    const double LossyAbove = 10, CleanBelow = 2;
    static readonly TimeSpan StepDownAfter = TimeSpan.FromSeconds(5), StepUpAfter = TimeSpan.FromSeconds(10);
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5); // 2.5 ping intervals without a figure

    volatile int hint = InitialHint, bitrate = channelKbps;
    int ceiling = channelKbps;
    DateTimeOffset? lossySince, cleanSince, lastFigure;

    /// <summary>The loss in percent Opus is told to expect.</summary>
    public int LossHint => hint;

    /// <summary>The kbps to send with, at most the channel's.</summary>
    public int BitrateKbps => bitrate;

    /// <summary>The expected loss for a measured loss: 1.5 times it, rounded, within MinHint and MaxHint.</summary>
    public static int HintFor(double lossPercent) => Math.Clamp((int)Math.Round(1.5 * lossPercent, MidpointRounding.AwayFromZero), MinHint, MaxHint);

    /// <summary>A join or a move starts again at the channel's bitrate.</summary>
    public void Reset(int kbps)
    {
        ceiling = bitrate = kbps;
        EndStreaks();
    }

    /// <summary>
    /// An edit of the own channel: a bitrate not lowered follows the new value, a lowered one stays lowered, at most the
    /// new value.
    /// </summary>
    public void SetCeiling(int kbps)
    {
        bitrate = bitrate == ceiling ? kbps : Math.Min(bitrate, kbps);
        ceiling = kbps;
    }

    /// <summary>A new connection: figures from before it are no evidence for the next.</summary>
    public void EndStreaks() => lossySince = cleanSince = lastFigure = null;

    /// <summary>Takes the uplink loss of a server ping.</summary>
    public void Report(LossReport uplink)
    {
        var now = time.GetUtcNow();
        // no figures for a while (an outage, a reconnect): a streak from before it says nothing about the line now
        if (now - lastFigure > StaleAfter) lossySince = cleanSince = null;
        lastFigure = now;
        if (uplink.Expected == 0)
        {
            // nothing sent in the server's window (a talk pause): neither loss nor proof of a clean line
            lossySince = cleanSince = null;
            return;
        }
        double loss = uplink.Percent;
        hint = HintFor(loss);
        lossySince = loss > LossyAbove ? lossySince ?? now : null;
        cleanSince = loss < CleanBelow ? cleanSince ?? now : null;
        if (now - lossySince >= StepDownAfter)
        {
            bitrate = Math.Max(bitrate / 2, ProtocolLimits.MinBitrate);
            lossySince = now; // the next step needs another 5 s
        }
        if (now - cleanSince >= StepUpAfter)
        {
            bitrate = Math.Min(bitrate * 2, ceiling);
            cleanSince = now;
        }
    }
}
