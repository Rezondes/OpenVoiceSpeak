namespace OVS.Client.Models;

/// <summary>A piece of a log line; IsMatch marks the text the line filter found.</summary>
internal sealed record LogSegment(string Text, bool IsMatch);
