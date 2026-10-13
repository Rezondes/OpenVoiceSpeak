namespace OVS.Client.Models;

/// <summary>Package 133 (A142): the voice round trip against the user's own baseline, as 4 to 1 bars.</summary>
internal enum SignalRating
{
    /// <summary>Nothing measured yet.</summary>
    Unknown,
    /// <summary>Round trip and loss are fine.</summary>
    Good,
    /// <summary>Noticeable delay or loss.</summary>
    Fair,
    /// <summary>Talking suffers.</summary>
    Poor,
    /// <summary>Barely usable.</summary>
    Bad,
}
