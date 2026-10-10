namespace OVS.Client.Services;

/// <summary>What <see cref="VoiceHints"/> needs to know about the connected server and the own state on it.</summary>
/// <param name="ChannelMuted">The own channel is muted, so nobody hears anybody there.</param>
/// <param name="HasSpeakLinked">The right to speak into the linked channels.</param>
/// <param name="ServerMuted">The server muted the own microphone.</param>
/// <param name="SelfDeafened">The own speakers are off (which mutes the microphone too).</param>
/// <param name="SelfMuted">The own microphone is muted, by the button or by push-to-mute.</param>
internal readonly record struct VoiceHintState(bool ChannelMuted, bool HasSpeakLinked, bool ServerMuted, bool SelfDeafened, bool SelfMuted);
