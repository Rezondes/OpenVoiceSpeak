using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Audio;
using OVS.Client.Localization;
using OVS.Shared.Voice;

namespace OVS.Client.ViewModels;

/// <summary>Package 133 (A142): the voice round trip against the user's own baseline, as 4 to 1 bars.</summary>
public enum SignalRating { Unknown, Good, Fair, Poor, Bad }

/// <summary>
/// Package 133 (A142): the signal icon next to the ping and the "Verbindungsdetails" card. The main view model feeds it
/// every 2 s on the UI thread; the rating compares the voice round trip with the median of the last 30, like Fluxer, so a
/// far server is not red for good.
/// </summary>
public sealed partial class ConnectionStats : ObservableObject
{
    public const int BaselineSamples = 30;
    const string None = "-";

    readonly Queue<double> samples = new();
    ReceiveStats atStart;

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

    public double? BaselineMs { get; private set; }

    /// <summary>How many round trips the baseline holds (at most <see cref="BaselineSamples"/>).</summary>
    public int Samples => samples.Count;

    public int Bars => Unreachable ? 0 : Rating switch
    {
        SignalRating.Good => 4,
        SignalRating.Fair => 3,
        SignalRating.Poor => 2,
        SignalRating.Bad => 1,
        _ => 0,
    };

    public string Tip => string.Format(Strings.Signal_Tip, Unreachable ? Strings.Signal_Unreachable : Rating switch
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

    /// <param name="roundTrip">A new round trip, or null when no answer came since the last update (the figures stay).</param>
    /// <param name="receive">The mixer's counts; those up to the last <see cref="Reset"/> are left out.</param>
    public void Update(TimeSpan? roundTrip, LossReport uplink, LossReport downlink, ReceiveStats receive, int bitrateKbps)
    {
        if (roundTrip is { } rtt)
        {
            double ms = rtt.TotalMilliseconds;
            samples.Enqueue(ms);
            while (samples.Count > BaselineSamples) samples.Dequeue();
            var sorted = samples.Order().ToList();
            double baseline = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            BaselineMs = baseline;
            Rating = Rate(ms, baseline);
            RoundTripText = string.Format(Strings.ConnDetails_RoundTripValue, ms.ToString("0"), baseline.ToString("0"));
        }
        UplinkText = Loss(uplink);
        DownlinkText = Loss(downlink);
        JitterText = $"{receive.JitterMs:0.0} ms";
        DelayText = $"{receive.DelayMs} ms";
        ConcealedText = string.Format(Strings.ConnDetails_ConcealedValue, receive.Concealed - atStart.Concealed, receive.Rebuilt - atStart.Rebuilt, receive.Recovered - atStart.Recovered);
        BitrateText = $"{bitrateKbps} kbps";
    }

    static string Loss(LossReport loss) => $"{loss.Percent:0.#} % ({loss.Missed}/{loss.Expected})";

    /// <summary>A new connection, or none: nothing measured yet.</summary>
    /// <param name="receive">The mixer's counts now, so the details count from here.</param>
    public void Reset(ReceiveStats receive)
    {
        atStart = receive;
        samples.Clear();
        BaselineMs = null;
        Rating = SignalRating.Unknown;
        Unreachable = false;
        RoundTripText = UplinkText = DownlinkText = JitterText = DelayText = ConcealedText = BitrateText = None;
    }
}
