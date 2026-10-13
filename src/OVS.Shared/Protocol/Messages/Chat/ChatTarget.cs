namespace OVS.Shared.Protocol.Messages.Chat;

/// <summary>Chat (Package 31).</summary>
public enum ChatTarget
{
    /// <summary>Everyone on the server ("Allgemein").</summary>
    Server,
    /// <summary>Everyone in the sender's channel.</summary>
    Channel,
    /// <summary>One user.</summary>
    Private,
}
