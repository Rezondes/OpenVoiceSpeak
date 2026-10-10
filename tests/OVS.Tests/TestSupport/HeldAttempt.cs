namespace OVS.Tests.TestSupport;

/// <summary>A connect call <see cref="ConnectSeam.HoldNext"/> holds: <paramref name="Release"/> lets it go on, <paramref name="Reached"/> completes once the call was made.</summary>
sealed record HeldAttempt(Action Release, Task<ConnectAttempt> Reached);
