namespace OVS.Manager.Servers;

/// <summary>Package M18 (AC3): how many servers "Alle aktualisieren" updated and why it stopped, if it did.</summary>
/// <param name="Updated">The servers updated.</param>
/// <param name="Error">The first failure, after which no further server was touched; null when all went well.</param>
internal sealed record UpdateAllResult(int Updated, string? Error);
