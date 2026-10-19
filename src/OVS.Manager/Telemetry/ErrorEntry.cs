namespace OVS.Manager.Telemetry;

/// <summary>Package M24: one error line of a server.</summary>
/// <param name="Id">The log line's row id.</param>
/// <param name="AtUnixMs">When it was written.</param>
/// <param name="Message">What groups it: the line's template, else its text.</param>
/// <param name="Text">The line as written.</param>
/// <param name="ErrorId">The error ID a user sees (the trace id, Package 143).</param>
/// <param name="StackTrace">The exception's stack trace, "" without one.</param>
/// <param name="ExceptionMessage">The exception's message, "" without one.</param>
internal sealed record ErrorEntry(long Id, long AtUnixMs, string Message, string Text, string ErrorId, string StackTrace, string ExceptionMessage);
