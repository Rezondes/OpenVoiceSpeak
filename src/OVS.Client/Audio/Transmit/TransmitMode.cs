namespace OVS.Client.Audio.Transmit;

/// <summary>When the client sends.</summary>
public enum TransmitMode
{
    /// <summary>While the talk key is held.</summary>
    PushToTalk,
    /// <summary>While the voice activation hears speech.</summary>
    VoiceActivation,
}
