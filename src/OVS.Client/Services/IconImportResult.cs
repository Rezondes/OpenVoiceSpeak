namespace OVS.Client.Services;

/// <summary>What preparing a server logo gave: the PNG to upload, or why the picture was refused.</summary>
/// <param name="Png">The logo as PNG in the size the server keeps; null when refused.</param>
/// <param name="Error">Why the picture was refused, for the user; null when it was taken.</param>
internal sealed record IconImportResult(byte[]? Png, string? Error);
