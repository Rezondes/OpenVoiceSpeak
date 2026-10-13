namespace OVS.Client.Audio.Transmit;

/// <summary>When the client sends.</summary>
#pragma warning disable CA1515 // public: tests take it as a theory parameter, and xunit test methods are public
public enum TransmitMode
#pragma warning restore CA1515
{
    /// <summary>While the talk key is held.</summary>
    PushToTalk,
    /// <summary>While the voice activation hears speech.</summary>
    VoiceActivation,
}
