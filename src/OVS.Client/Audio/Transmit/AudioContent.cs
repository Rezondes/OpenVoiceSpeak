namespace OVS.Client.Audio.Transmit;

/// <summary>Package 142 (A151): "Übertragungsart": speech (mono, voice filters, Opus VOIP) or music (stereo, no filters).</summary>
public enum AudioContent
{
    /// <summary>Speech: mono, with the voice filters.</summary>
    Voice,
    /// <summary>Music: stereo where the microphone gives it, without the voice filters (Package 142).</summary>
    Music,
}
