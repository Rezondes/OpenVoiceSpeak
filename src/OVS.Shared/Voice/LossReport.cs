namespace OVS.Shared.Voice;

/// <summary>Package 133 (A142): frames expected and missed in a window of the frame numbers.</summary>
public readonly record struct LossReport(uint Expected, uint Missed)
{
    /// <summary>0 when nothing was expected: nobody talked, which is no loss.</summary>
    public double Percent => Expected == 0 ? 0 : 100.0 * Missed / Expected;
}
