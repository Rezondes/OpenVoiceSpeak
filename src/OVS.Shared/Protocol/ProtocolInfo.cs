namespace OVS.Shared.Protocol;

public static class ProtocolInfo
{
    public const int Version = 5; // 2: server logo (Package 30), 3: chat (Package 31), 4: muted channels (Package 34), 5: channel slots (Package 35)
    public const int MaxChannelUsers = 999;
    public const int DefaultPort = 7000;
    public const int MaxChatLength = 2000;
    public const int ChatBurst = 5; // messages per ChatWindow
    public static readonly TimeSpan ChatWindow = TimeSpan.FromSeconds(5);
}
