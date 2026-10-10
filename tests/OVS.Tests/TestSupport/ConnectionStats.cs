using OVS.Client.ViewModels;

namespace OVS.Tests.TestSupport;

/// <summary>The one place the tests build a connection statistics card that measures nothing: <c>Refresh</c> does nothing, the figures come through <c>Update</c>.</summary>
static class ConnectionStats
{
    public static ConnectionStatsViewModel CreateBlank() =>
        new(new ManualTimeProvider(), () => null, () => default, () => default, () => 0, _ => { });
}
