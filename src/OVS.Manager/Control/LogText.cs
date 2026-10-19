using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Manager.Control;

/// <summary>Package M16 (AC1): one page of a log file, or the hits of a search, or why there is none.</summary>
/// <param name="Page">The page, null for a search or a failure.</param>
/// <param name="Hits">The search's hits in server logs, empty otherwise.</param>
/// <param name="Error">Why there is nothing, null when there is.</param>
internal sealed record LogText(LogPage? Page, IReadOnlyList<LogHit> Hits, string? Error);
