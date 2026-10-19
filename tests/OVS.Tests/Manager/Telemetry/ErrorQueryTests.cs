using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Telemetry;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Telemetry;

/// <summary>Package M24: a server's errors, an error found by its ID with its trace, and the errors of the last day.</summary>
public sealed class ErrorQueryTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    const string Template = "Error in request {Request} from {Nickname}, error ID {ErrorId}:";
    const string Trace = "4bf92f3577b34da6a3ce929d0e0e4736";

    static LogRecordRow Error(string serverId, DateTimeOffset at, string errorId, string template = Template, int severity = 17, string? trace = null) => new()
    {
        ServerId = serverId, AtUnixMs = Ms(at), Severity = severity, Body = $"Error in request CreateChannel from anna, error ID {errorId}:", TraceId = trace,
        Attributes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["{OriginalFormat}"] = template, ["ErrorId"] = errorId, ["exception.message"] = "Testfehler",
            ["exception.stacktrace"] = "System.InvalidOperationException: Testfehler\n   at OVS.Server.State.ServerState.OnCreateChannel()",
        }),
    };

    static async Task<T> QueryAsync<T>(ManagerFactory manager, Func<ErrorQuery, Task<T>> query)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ErrorQuery>());
    }

    /// <summary>AC1: errors and worse, grouped by their template with a count; a warning is left out; one opens with its stack trace.</summary>
    [Fact]
    public async Task Errors_SameTemplate_GroupedWithStackTrace()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await AddAsync(manager, Error(id, Now.AddMinutes(-5), "aaa1"), Error(id, Now.AddMinutes(-1), "bbb2"), Error(id, Now.AddMinutes(-3), "ccc3", "Saving failed: {Reason}"),
            Error(id, Now, "ddd4", severity: 13));

        var groups = await QueryAsync(manager, query => query.GroupsAsync(id));

        Assert.Equal([(Template, 2, "bbb2"), ("Saving failed: {Reason}", 1, "ccc3")], groups.Select(group => (group.Message, group.Count, group.Entries[0].ErrorId)));
        await using var context = ServerPages.Context(manager);
        var page = context.Render<Errors>(parameters => parameters.Add(errors => errors.Id, id));
        Assert.Equal(2, page.FindAll("[data-error-group]").Count);
        await page.Find("[data-open]").ClickAsync(new());
        Assert.Contains("at OVS.Server.State.ServerState.OnCreateChannel()", page.Find("[data-stack]").TextContent, StringComparison.Ordinal);
    }

    /// <summary>AC2: an error ID (in any case) finds its error and the spans of its trace; an ID without a trace is found by its attribute.</summary>
    [Fact]
    public async Task Lookup_ErrorId_ErrorAndTrace()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await AddAsync(manager, Error(id, Now, Trace, trace: Trace), Error(id, Now, "0123456789abcdef0123456789abcdef"),
            new SpanRow { ServerId = id, TraceId = Trace, SpanId = "00f067aa0ba902b7", Name = "request CreateChannel", StartUnixMs = Ms(Now.AddMilliseconds(-20)), DurationMs = 12.5, Failed = true },
            new SpanRow { ServerId = id, TraceId = Trace, SpanId = "00f067aa0ba902b8", Name = "save", StartUnixMs = Ms(Now.AddMilliseconds(-10)), DurationMs = 3 },
            new SpanRow { ServerId = id, TraceId = "ffffffffffffffffffffffffffffffff", SpanId = "1111111111111111", Name = "other", StartUnixMs = Ms(Now) });

        var found = await QueryAsync(manager, query => query.LookupAsync($" {Trace.ToUpperInvariant()} ", visible: null));

        Assert.NotNull(found);
        Assert.Equal((id, Trace), (found.ServerId, found.Error.ErrorId));
        Assert.Equal(["request CreateChannel", "save"], found.Spans.Select(span => span.Name));
        var untraced = await QueryAsync(manager, query => query.LookupAsync("0123456789abcdef0123456789abcdef", visible: null));
        Assert.Empty(untraced!.Spans);
        Assert.Null(await QueryAsync(manager, query => query.LookupAsync("deadbeef", visible: null)));
    }

    /// <summary>AC2: an error on a server the manager is not assigned to is not found.</summary>
    [Fact]
    public async Task Lookup_OtherManagersServer_NotFound()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await AddAsync(manager, Error(id, Now, Trace, trace: Trace));

        Assert.Null(await QueryAsync(manager, query => query.LookupAsync(Trace, new HashSet<string> { "other" })));
        Assert.NotNull(await QueryAsync(manager, query => query.LookupAsync(Trace, new HashSet<string> { id })));
    }

    /// <summary>AC3: the overview counts each server's errors of the last 24 hours.</summary>
    [Fact]
    public async Task Count_LastDay_InOverview()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await ServerLinks.RunAtAsync(manager, id, 7000);
        await AddAsync(manager, Error(id, Now.AddHours(-2), "aaa1"), Error(id, Now.AddHours(-23), "bbb2"), Error(id, Now.AddHours(-25), "ccc3"), Error(id, Now, "ddd4", severity: 13));

        Assert.Equal(2, (await QueryAsync(manager, query => query.CountsAsync([id])))[id]);
        await using var context = ServerPages.Context(manager);
        Assert.Equal("2", context.Render<OVS.Manager.Components.Pages.Servers.ServerList>().Find($"tr[data-server='{id}'] [data-errors]").TextContent);
    }
}
