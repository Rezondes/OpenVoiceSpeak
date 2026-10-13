namespace OVS.Shared.Protocol.Messages;

/// <summary>Client request. A failed request is answered with an Error carrying the same RequestId.</summary>
public abstract record Request : Message
{
    /// <summary>Chosen by the client; the answer or the Error carries it back.</summary>
    public string? RequestId { get; init; }
}
