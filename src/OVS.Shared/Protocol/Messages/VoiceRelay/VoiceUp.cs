namespace OVS.Shared.Protocol.Messages.VoiceRelay;

/// <summary>
/// Package 141 (A150): voice over the TLS connection while UDP does not work; no request, it has its own budget. Seq is
/// the frame number, Opus the frame as base64 (at most VoiceHeader.MaxOpusSize bytes). An empty Opus only moves the
/// session's voice to TCP (a listener that does not talk); a UDP ping or voice packet moves it back.
/// </summary>
public sealed record VoiceUp(uint Seq, byte Target, string Opus) : Message;
