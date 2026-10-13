using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

internal sealed record OpenedPacket(VoiceHeader Header, byte[] Plain);
