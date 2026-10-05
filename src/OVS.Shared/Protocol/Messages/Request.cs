namespace OVS.Shared.Protocol.Messages;

/// <summary>Client request. A failed request is answered with an Error carrying the same RequestId.</summary>
public abstract record Request : Message
{
    public string? RequestId { get; init; }
}
