using System.Net;
using Microsoft.Extensions.Hosting;
using OVS.Server;
using OVS.Server.Logging;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 147 (A157): the server's meter, one per run; chat counts carry the channel id only.</summary>
public sealed class ServerMetricsTests() : TempDirectoryTest("ovs-metrics-", quiet: true)
{
    [Fact]
    public async Task Sessions_JoinAndLeave_Reported()
    {
        await using var server = await TestServer.StartAsync();
        using var metrics = new MetricRecorder(server.State.Metrics.Meter);
        double Online()
        {
            metrics.Collect();
            return metrics.Of("ovs.sessions")[^1].Value;
        }

        await using (var anna = await TestClient.ConnectAsync(server, "anna"))
        {
            await using (var bert = await TestClient.ConnectAsync(server, "bert")) Assert.Equal(2, Online());
            await AsyncWait.UntilAsync(() => Online() == 1);
        }
        await AsyncWait.UntilAsync(() => Online() == 0);
    }

    [Fact]
    public void Requests_Handled_CountedByTypeAndCode()
    {
        using var harness = new StateHarness();
        using var metrics = new MetricRecorder(harness.State.Metrics.Meter);
        var anna = harness.Admit("anna");

        harness.Handle(anna, new SetSelfState(true, false) { RequestId = "a" });
        harness.Handle(anna, new CreateChannel("Raid", "") { RequestId = "b" });

        Assert.Equal(1, metrics.Sum("ovs.requests", (ServerTelemetry.RequestTypeTag, "SetSelfState"), (ServerTelemetry.ResultTag, ServerTelemetry.ResultOk)));
        Assert.Equal(1, metrics.Sum("ovs.requests", (ServerTelemetry.RequestTypeTag, "CreateChannel"), (ServerTelemetry.ResultTag, ReasonCodes.PermissionDenied)));
    }

    [Fact]
    public async Task OutboxAbortAndRejection_Happen_Counted()
    {
        using var serverMetrics = new ServerMetrics();
        using var metrics = new MetricRecorder(serverMetrics.Meter);
        var outbox = new SessionOutbox(serverMetrics);
        outbox.SendFrame(null, "Huge"); // too large for a frame
        outbox.Abort("again"); // the first reason counts, once
        Assert.Equal(1, metrics.Sum("ovs.outbox.aborts", (ServerMetrics.AbortReasonTag, "message too large")));
        Assert.Equal(1, metrics.Sum("ovs.outbox.aborts"));

        await using var server = await TestServer.StartAsync(password: "geheim");
        using var rejections = new MetricRecorder(server.State.Metrics.Meter);
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.IsType<Rejected>(await client.HandshakeAsync("anna", "falsch"));
        Assert.Equal(1, rejections.Sum("ovs.admission.rejections", (ServerTelemetry.ResultTag, ReasonCodes.WrongPassword)));
    }

    [Fact]
    public async Task ControlTraffic_SentAndReceived_CountedPerType()
    {
        await using var server = await TestServer.StartAsync();
        using var metrics = new MetricRecorder(server.State.Metrics.Meter);
        await using var anna = await TestClient.ConnectAsync(server, "anna");

        await anna.SendAsync(new Ping());
        await anna.WaitForAsync<Pong>();

        await AsyncWait.UntilAsync(() => metrics.Sum("ovs.control.frames", (ServerMetrics.MessageTypeTag, "Ping"), (ServerMetrics.DirectionTag, "in")) == 1);
        Assert.True(metrics.Sum("ovs.control.bytes", (ServerMetrics.MessageTypeTag, "Ping"), (ServerMetrics.DirectionTag, "in")) > 4); // header and JSON
        Assert.Equal(1, metrics.Sum("ovs.control.frames", (ServerMetrics.MessageTypeTag, "Pong"), (ServerMetrics.DirectionTag, "out")));
        Assert.Equal(1, metrics.Sum("ovs.control.frames", (ServerMetrics.MessageTypeTag, "Welcome"), (ServerMetrics.DirectionTag, "out")));
    }

    [Fact]
    public void Chat_CountAndSize_OnlyChannelLabel()
    {
        using var harness = new StateHarness();
        using var metrics = new MetricRecorder(harness.State.Metrics.Meter);
        using var annaId = ClientIdentity.Create();
        var anna = harness.Admit("annakarenina", annaId.Fingerprint);
        var bert = harness.Admit("bert");

        harness.Handle(anna, new SendChat(ChatTarget.Channel, null, "hallo"));
        harness.Handle(anna, new SendChat(ChatTarget.Private, bert.Id, "äöü"));

        var channel = Assert.Single(metrics.Of("ovs.chat.messages", (ServerMetrics.ChannelTag, anna.ChannelId.ToString())));
        Assert.Equal(1, channel.Value);
        Assert.Equal(5, Assert.Single(metrics.Of("ovs.chat.message.size", (ServerMetrics.ChannelTag, anna.ChannelId.ToString()))).Value);
        var unlabelled = metrics.Of("ovs.chat.message.size").Where(measured => measured.Tags.Count == 0).ToList();
        Assert.Equal(6, Assert.Single(unlabelled).Value);
        Assert.Equal(2, metrics.Sum("ovs.chat.messages"));
        var values = metrics.Measurements.SelectMany(measured => measured.Tags.Values).ToList();
        Assert.DoesNotContain(values, value => value is "hallo" or "äöü" or "annakarenina" or "bert" || value == annaId.Fingerprint);
    }

    [Fact]
    public async Task RuntimeMetrics_TelemetryOn_Exported()
    {
        await using var receiver = new OtlpReceiver();
        var env = new Dictionary<string, string>
        {
            [ServerTelemetry.EndpointVariable] = receiver.Endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_METRIC_EXPORT_INTERVAL"] = "100",
        };
        using (var logs = new ServerLogWriter(dir, 0, TimeProvider.System, _ => { }))
        using (var host = ServerHost.BuildRun(new ServerConfig(0, dir, 50, "Testserver", ""), logs, TimeProvider.System, new IPEndPoint(IPAddress.Loopback, 0), env.GetValueOrDefault))
        {
            await host.StartAsync();
            await AsyncWait.UntilAsync(() => receiver.Count("/v1/metrics", "ovs.sessions") > 0 && receiver.Count("/v1/metrics", "dotnet.") > 0);
            await host.StopAsync();
        }
    }

    [Fact]
    public void Voice_NoListener_NoAllocation()
    {
        using var serverMetrics = new ServerMetrics();
        for (int warm = 0; warm < 10; warm++) Count(serverMetrics);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int packet = 0; packet < 1000; packet++) Count(serverMetrics);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        static void Count(ServerMetrics serverMetrics)
        {
            serverMetrics.Voice(incoming: true, tcp: false, 120);
            serverMetrics.Voice(incoming: false, tcp: true, 160);
            serverMetrics.VoiceDropped(ServerMetrics.Budget);
        }
    }
}
