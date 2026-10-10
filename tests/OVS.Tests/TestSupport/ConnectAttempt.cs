using OVS.Client.Dialogs;

namespace OVS.Tests.TestSupport;

/// <summary>One call of the connect seam as <see cref="ConnectSeam"/> recorded it: which call it was (from 1), what was asked for and the token it came with.</summary>
sealed record ConnectAttempt(int Number, ConnectChoice Choice, CancellationToken Token);
