namespace OVS.Shared.Protocol;

public static class ProtocolInfo
{
    public const int Version = 3; // 2: server logo (Package 30), 3: chat (Package 31)
    public const int DefaultPort = 7000;
    public const int MaxChatLength = 2000;
    public const int ChatBurst = 5; // messages per ChatWindow
    public static readonly TimeSpan ChatWindow = TimeSpan.FromSeconds(5);
}
