using System.Collections.Concurrent;
using System.Diagnostics;
using OVS.Server;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Protocol.Messages.VoiceRelay;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 146 (A156): one span per request and per handshake, with the fingerprint and never the nickname.</summary>
public sealed class ServerTracingTests
{
    [Fact]
    public void Request_OneSpan_TypeCodeFingerprintNoNickname()
    {
        using var spans = new SpanRecorder();
        using var harness = new StateHarness();
        using var identity = ClientIdentity.Create();
        var fingerprint = identity.Fingerprint;
        var anna = harness.Admit("annakarenina", fingerprint);

        harness.Handle(anna, new SetSelfState(true, false) { RequestId = "a" });
        harness.Handle(anna, new CreateChannel("Raid", "geheimer Text") { RequestId = "b" });

        var mine = spans.Of(fingerprint);
        Assert.Equal(["request SetSelfState", "request CreateChannel"], mine.Select(span => span.DisplayName));
        Assert.Equal(["SetSelfState", "CreateChannel"], mine.Select(span => span.GetTagItem(ServerTelemetry.RequestTypeTag)));
        Assert.Equal(["ok", ReasonCodes.PermissionDenied], mine.Select(span => span.GetTagItem(ServerTelemetry.ResultTag)));
        var values = mine.SelectMany(span => span.TagObjects.Select(tag => tag.Value?.ToString() ?? "")
            .Concat(span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString() ?? "")))).ToList();
        Assert.DoesNotContain(values, value => value.Contains("annakarenina", StringComparison.Ordinal) || value.Contains("geheimer", StringComparison.Ordinal));
    }

    [Fact]
    public void Request_Throws_SpanFailsAndErrorIdIsTraceId()
    {
        using var spans = new SpanRecorder();
        using var harness = new StateHarness();
        using var identity = ClientIdentity.Create();
        var fingerprint = identity.Fingerprint;
        var anna = harness.Admit("anna", fingerprint);
        StateHarness.Drain(anna);
        harness.State.BeforeRequest = _ => throw new InvalidOperationException("Testfehler");

        harness.Handle(anna, new SetSelfState(true, false) { RequestId = "a" });

        var span = Assert.Single(spans.Of(fingerprint));
        var error = Assert.IsType<Error>(Assert.Single(StateHarness.Drain(anna)));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Contains(span.Events, spanEvent => spanEvent.Name == "exception");
        Assert.Equal(span.TraceId.ToHexString(), error.ErrorId);
        Assert.Contains(harness.Log, line => line.Contains(span.TraceId.ToHexString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handshake_OneSpan_WithFingerprintOrRejection()
    {
        using var spans = new SpanRecorder();
        await using var server = await TestServer.StartAsync(password: "geheim");
        using var anna = ClientIdentity.Create();
        using var bert = ClientIdentity.Create();

        await using (var welcome = await TestClient.OpenAsync(server.Port, anna)) Assert.IsType<Welcome>(await welcome.HandshakeAsync("anna", "geheim"));
        await using (var refused = await TestClient.OpenAsync(server.Port, bert)) Assert.IsType<Rejected>(await refused.HandshakeAsync("bert", "falsch"));

        await AsyncWait.UntilAsync(() => spans.Of(bert.Fingerprint).Count == 1);
        var accepted = Assert.Single(spans.Of(anna.Fingerprint), span => span.DisplayName == "handshake");
        var rejected = Assert.Single(spans.Of(bert.Fingerprint));
        Assert.Equal(("handshake", "ok"), (accepted.DisplayName, accepted.GetTagItem(ServerTelemetry.ResultTag)));
        Assert.Equal(("handshake", ReasonCodes.WrongPassword), (rejected.DisplayName, rejected.GetTagItem(ServerTelemetry.ResultTag)));
    }

    [Fact]
    public async Task Voice_Relayed_NoSpans()
    {
        using var spans = new SpanRecorder();
        await using var server = await TestServer.StartAsync();
        using var anna = ClientIdentity.Create();
        await using var client = await TestClient.ConnectAsync(server, "anna", anna);
        var before = spans.Of(anna.Fingerprint).Count;

        await client.SendAsync(new VoiceUp(1, 0, Convert.ToBase64String(new byte[40])));
        await client.SendAsync(new Ping());
        await client.WaitForAsync<Pong>();

        Assert.Equal(before, spans.Of(anna.Fingerprint).Count);
    }

    [Fact]
    public void NoListener_NoSpan_RandomErrorId()
    {
        // without a recorded span the ID is a fresh random one, as in Package 143
        using var harness = new StateHarness();
        var anna = harness.Admit("anna");
        StateHarness.Drain(anna);
        harness.State.BeforeRequest = _ => throw new InvalidOperationException("Testfehler");

        harness.Handle(anna, new SetSelfState(true, false) { RequestId = "a" });
        harness.Handle(anna, new SetSelfState(true, false) { RequestId = "b" });

        var ids = StateHarness.Drain(anna).OfType<Error>().Select(error => error.ErrorId).ToList();
        Assert.Equal(2, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{32}$", id));
        Assert.Null(Activity.Current);
    }

    /// <summary>Catches the spans of the server's source that end while it lives; other tests' spans are told apart by fingerprint.</summary>
    sealed class SpanRecorder : IDisposable
    {
        readonly ActivityListener listener;

        public SpanRecorder()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ServerTelemetry.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
        }

        public ConcurrentQueue<Activity> Stopped { get; } = new();

        public List<Activity> Of(string fingerprint) =>
            [.. Stopped.Where(span => span.GetTagItem(ServerTelemetry.FingerprintTag) as string == fingerprint)];

        public void Dispose() => listener.Dispose();
    }
}
