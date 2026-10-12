using OVS.Client.Audio.Playback;

namespace OVS.Client.Audio;

/// <summary>
/// Package 122: which mixer frames report the speakers to the UI, at most every 100 ms (5 frames): with many speakers the
/// set changes nearly every frame, and each report goes over the users on the UI thread.
/// </summary>
internal sealed class SpeakerReportThrottle
{
    public const int Frames = 5;

    List<ActiveSpeaker> last = [];
    int sinceReport = Frames;

    /// <summary>Package 124: someone was reported speaking, so a report of the silence is still due.</summary>
    public bool Pending => last.Count > 0;

    /// <param name="active">Reused by the mixer: a report is a copy (made at most every 100 ms).</param>
    /// <returns>The speakers to report after this frame, or null.</returns>
    public List<ActiveSpeaker>? Next(List<ActiveSpeaker> active)
    {
        // On change, and every 100 ms while someone talks: the UI holds an indicator for 300 ms after the last report. A
        // change within the 100 ms waits for the next report (the set reported last stays the one compared with).
        if (sinceReport < Frames) sinceReport++; // capped: it would overflow after months of silence
        if (sinceReport < Frames || (active.Count == 0 && last.Count == 0)) return null;
        sinceReport = 0;
        last = [.. active];
        return last;
    }
}
