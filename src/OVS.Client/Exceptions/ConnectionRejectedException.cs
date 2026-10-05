namespace OVS.Client.Exceptions;

public sealed class ConnectionRejectedException(string code, string? detail) : Exception($"{code}: {detail}")
{
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
}
