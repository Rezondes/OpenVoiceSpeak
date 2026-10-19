using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OVS.Manager.Accounts;
using OVS.Manager.Telemetry;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Telemetry;

/// <summary>Package M21: a manager on a manual clock and the telemetry it is given.</summary>
internal static class TelemetryTesting
{
    /// <summary>Unix milliseconds of <paramref name="at"/>.</summary>
    public static long Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    /// <summary>A manager on <paramref name="time"/> with <paramref name="more"/> settings.</summary>
    public static ManagerFactory TelemetryManager(TimeProvider time, params (string Key, string Value)[] more) =>
        new([(FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password), .. more]) { Time = time };

    /// <summary>The manager's hosted <typeparamref name="T"/>.</summary>
    public static T Hosted<T>(ManagerFactory manager) => manager.Services.GetServices<IHostedService>().OfType<T>().Single();

    /// <summary>Stores <paramref name="rows"/> in the telemetry database.</summary>
    public static async Task AddAsync(ManagerFactory manager, params object[] rows)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TelemetryStore>();
        store.AddRange(rows);
        await store.SaveChangesAsync();
    }

    /// <summary>Reads the telemetry database.</summary>
    public static async Task<T> ReadAsync<T>(ManagerFactory manager, Func<TelemetryStore, Task<T>> read)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<TelemetryStore>());
    }

    /// <summary>A cumulative sum point of <paramref name="serverId"/>.</summary>
    public static MetricPoint Sum(string serverId, string name, DateTimeOffset at, DateTimeOffset start, double value, string attributes = "") =>
        new() { ServerId = serverId, Name = name, Kind = MetricKind.Sum, AtUnixMs = Ms(at), StartUnixMs = Ms(start), Value = value, Attributes = attributes };

    /// <summary>A gauge point of <paramref name="serverId"/>.</summary>
    public static MetricPoint Gauge(string serverId, string name, DateTimeOffset at, double value) =>
        new() { ServerId = serverId, Name = name, Kind = MetricKind.Gauge, AtUnixMs = Ms(at), Value = value };
}
