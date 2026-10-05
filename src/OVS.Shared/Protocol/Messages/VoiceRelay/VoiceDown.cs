namespace OVS.Shared.Protocol.Messages.VoiceRelay;

/// <summary>Package 141: a speaker's frame to a client on voice over TCP; never with a copy, the path loses nothing.</summary>
public sealed record VoiceDown(uint SessionId, uint Seq, byte Target, string Opus) : Message;
