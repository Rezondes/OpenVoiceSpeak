namespace OVS.Client.Services;

/// <summary>
/// What the audio engine needs to know about the current connection. <see cref="Disconnected"/> is the state without one
/// (muted); <c>default</c> is not that state. A new member needs its copy in <see cref="EngineSync.SyncConnection"/> and a test.
/// </summary>
/// <param name="Connected">A server is connected.</param>
/// <param name="SelfMuted">The own microphone is muted.</param>
/// <param name="Deafened">The own speakers are off.</param>
/// <param name="HasSpeakLinked">The right to speak into the linked channels.</param>
/// <param name="ChannelMuted">The own channel is muted, so nothing is sent.</param>
/// <param name="ChannelId">The own channel; null when in no channel.</param>
/// <param name="ChannelBitrate">The own channel's kbps as the server sent it; null when in no channel.</param>
internal readonly record struct EngineConnectionState(bool Connected, bool SelfMuted, bool Deafened, bool HasSpeakLinked, bool ChannelMuted, Guid? ChannelId, int? ChannelBitrate)
{
    /// <summary>No connection: muted, no right to speak, no channel.</summary>
    internal static readonly EngineConnectionState Disconnected = new(false, true, false, false, false, null, null);
}
