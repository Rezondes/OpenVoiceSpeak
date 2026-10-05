using OVS.Shared.Voice;

namespace OVS.Client.Audio.Transmit;

public static class TransmitController
{
    /// <returns>Voice target to send with, or null to stay silent. Voice activation never uses links.</returns>
    /// <remarks>
    /// The PTT key only counts in push-to-talk mode: with voice activation the voice alone opens the microphone.
    /// Link-PTT works in both modes, it is the only way to reach linked channels.
    /// </remarks>
    public static byte? Decide(TransmitMode mode, bool pttDown, bool linkPttDown, bool vadActive, bool selfMuted, bool hasSpeakLinked)
    {
        if (selfMuted) return null;
        if (linkPttDown) return hasSpeakLinked ? VoiceHeader.TargetLinked : VoiceHeader.TargetChannel;
        return mode switch
        {
            TransmitMode.PushToTalk when pttDown => VoiceHeader.TargetChannel,
            TransmitMode.VoiceActivation when vadActive => VoiceHeader.TargetChannel,
            _ => null,
        };
    }
}
