using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Audio.Playback;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Shared.Voice;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 133 (A142): the signal icon next to the ping and the "Verbindungsdetails" card. The main view model calls
/// <see cref="Refresh"/> on every UI tick; the figures follow every 2 s, the crossing out at once. The rating compares the
/// voice round trip with the median of the last 30, like Fluxer, so a far server is not red for good. What it measures it
/// reads through delegates, so it knows neither the voice connection nor the engine. The loss adapter's feed (Package 135) is
/// not its job: <see cref="VoiceLink"/> hands it the uplink figure of each new answer.
/// </summary>
internal sealed partial class ConnectionStatsViewModel : ObservableObject
{
    /// <summary>The round trips the baseline is taken from.</summary>
    public const int BaselineSamples = 30;
    const string None = "-";
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    readonly Queue<double> samples = new();
    readonly TimeProvider time;
    readonly Func<bool?> readReachable;
    readonly Func<VoiceLinkFigures> readFigures;
    readonly Func<ReceiveStats> readReceiveStats;
    readonly Func<int> readEncoderBitrate;
    ReceiveStats atStart;
    DateTimeOffset refreshedAt;
    int roundTripsSeen; // a round trip enters the baseline once, not again on every refresh while no answer comes

    /// <param name="time">The clock of the 2 s between two refreshes.</param>
    /// <param name="readReachable">Whether UDP gets an answer; null without a voice connection, when nothing is refreshed.</param>
    /// <param name="readFigures">The voice connection's figures; asked only when the figures are due.</param>
    /// <param name="readReceiveStats">The mixer's counts, since the engine started (the card counts from <see cref="Reset"/>).</param>
    /// <param name="readEncoderBitrate">The bitrate the encoder uses now, in kbps.</param>
    internal ConnectionStatsViewModel(TimeProvider time, Func<bool?> readReachable, Func<VoiceLinkFigures> readFigures, Func<ReceiveStats> readReceiveStats,
        Func<int> readEncoderBitrate)
    {
        this.time = time;
        this.readReachable = readReachable;
        this.readFigures = readFigures;
        this.readReceiveStats = readReceiveStats;
        this.readEncoderBitrate = readEncoderBitrate;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Bars), nameof(Tip))] SignalRating rating;

    /// <summary>UDP gets no answer: the icon is crossed out. Set on every UI tick, so it shows at once.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Bars), nameof(Tip))] bool unreachable;

    [ObservableProperty] string roundTripText = None;
    [ObservableProperty] string uplinkText = None;
    [ObservableProperty] string downlinkText = None;
    [ObservableProperty] string jitterText = None;
    [ObservableProperty] string delayText = None;
    [ObservableProperty] string concealedText = None;
    [ObservableProperty] string bitrateText = None;

    /// <summary>The connection's usual round trip, null until enough were measured.</summary>
    public double? BaselineMs { get; private set; }

    /// <summary>How many round trips the baseline holds (at most <see cref="BaselineSamples"/>).</summary>
    public int Samples => samples.Count;

    /// <summary>The signal icon's bars, 0 to 4.</summary>
    public int Bars => Unreachable ? 0 : Rating switch
    {
        SignalRating.Good => 4,
        SignalRating.Fair => 3,
        SignalRating.Poor => 2,
        SignalRating.Bad => 1,
        _ => 0,
    };

    /// <summary>The signal icon's tooltip.</summary>
    public string Tip => string.Format(CultureInfo.CurrentCulture, Strings.Signal_Tip, Unreachable ? Strings.Signal_Unreachable : Rating switch
    {
        SignalRating.Good => Strings.Signal_Good,
        SignalRating.Fair => Strings.Signal_Fair,
        SignalRating.Poor => Strings.Signal_Poor,
        SignalRating.Bad => Strings.Signal_Bad,
        _ => Strings.Signal_Unknown,
    });

    /// <summary>Below 70 ms above the baseline green, below 160 yellow, below 300 orange, else red (A142).</summary>
    public static SignalRating Rate(double roundTripMs, double baselineMs) => (roundTripMs - baselineMs) switch
    {
        < 70 => SignalRating.Good,
        < 160 => SignalRating.Fair,
        < 300 => SignalRating.Poor,
        _ => SignalRating.Bad,
    };

    /// <summary>The crossing out on every call, the figures every 2 s, like Fluxer's stats; nothing without a voice connection.</summary>
    public void Refresh()
    {
        if (readReachable() is not { } isReachable) return;
        Unreachable = !isReachable;
        var now = time.GetUtcNow();
        if (now - refreshedAt < RefreshInterval) return;
        refreshedAt = now;
        var figures = readFigures();
        bool answered = figures.RoundTripCount != roundTripsSeen;
        Update(answered ? figures.RoundTrip : null, figures.Uplink, figures.Downlink, readReceiveStats(), readEncoderBitrate());
        roundTripsSeen = figures.RoundTripCount;
    }

    /// <param name="roundTrip">A new round trip, or null when no answer came since the last update (the figures stay).</param>
    /// <param name="receive">The mixer's counts; those up to the last <see cref="Reset"/> are left out.</param>
    /// <param name="uplink">The loss the server sees from the client.</param>
    /// <param name="downlink">The loss the client sees from the server.</param>
    /// <param name="bitrateKbps">The encoder's bitrate now.</param>
    internal void Update(TimeSpan? roundTrip, LossReport uplink, LossReport downlink, ReceiveStats receive, int bitrateKbps)
    {
        if (roundTrip is { } rtt)
        {
            double milliseconds = rtt.TotalMilliseconds;
            samples.Enqueue(milliseconds);
            while (samples.Count > BaselineSamples) samples.Dequeue();
            var sorted = samples.Order().ToList();
            double baseline = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            BaselineMs = baseline;
            Rating = Rate(milliseconds, baseline);
            RoundTripText = string.Format(CultureInfo.CurrentCulture, Strings.ConnDetails_RoundTripValue, milliseconds.ToString("0", CultureInfo.CurrentCulture), baseline.ToString("0", CultureInfo.CurrentCulture));
        }
        UplinkText = Loss(uplink);
        DownlinkText = Loss(downlink);
        JitterText = $"{receive.JitterMs:0.0} ms";
        DelayText = $"{receive.DelayMs} ms";
        ConcealedText = string.Format(CultureInfo.CurrentCulture, Strings.ConnDetails_ConcealedValue, receive.Concealed - atStart.Concealed, receive.Rebuilt - atStart.Rebuilt, receive.Recovered - atStart.Recovered);
        BitrateText = $"{bitrateKbps} kbps";
    }

    static string Loss(LossReport loss) => $"{loss.Percent:0.#} % ({loss.Missed}/{loss.Expected})";

    /// <summary>A new connection, or none: nothing measured yet, and the details count from the mixer's counts now.</summary>
    public void Reset()
    {
        atStart = readReceiveStats();
        refreshedAt = DateTimeOffset.MinValue; // the first refresh shows what there is
        roundTripsSeen = 0;
        samples.Clear();
        BaselineMs = null;
        Rating = SignalRating.Unknown;
        Unreachable = false;
        RoundTripText = UplinkText = DownlinkText = JitterText = DelayText = ConcealedText = BitrateText = None;
    }
}
