namespace OVS.Manager.Telemetry;

/// <summary>Package M24 (AC1): the errors of one message, newest first.</summary>
/// <param name="Message">The message (template) they share.</param>
/// <param name="Count">How many there are.</param>
/// <param name="Entries">The errors, newest first.</param>
internal sealed record ErrorGroup(string Message, int Count, IReadOnlyList<ErrorEntry> Entries);
