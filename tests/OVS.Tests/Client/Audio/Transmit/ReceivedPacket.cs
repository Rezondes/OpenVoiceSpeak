using System.Net;
using OVS.Shared.Voice;

namespace OVS.Tests.Client.Audio.Transmit;

/// <summary>A voice packet the fake server received, opened.</summary>
/// <param name="Header">The voice header.</param>
/// <param name="Plain">The decrypted payload.</param>
/// <param name="From">Where it came from.</param>
public sealed record ReceivedPacket(VoiceHeader Header, byte[] Plain, IPEndPoint From);
