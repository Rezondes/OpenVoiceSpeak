using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

public sealed record OpenedPacket(VoiceHeader Header, byte[] Plain);
