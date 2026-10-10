using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>
/// Package 126 and 133: the voice client of a connection is wired to the audio engine and its UDP reachability is logged. A
/// fake server on loopback UDP answers the client, the clock is a manual one, and the engine runs without devices.
/// </summary>
public sealed class VoiceLinkTests : IDisposable
{
    const string NowReachableLine = "UDP now reachable";
    const string ReachableAgainLine = "UDP reachable again";
    const string NotReachableLine = "UDP not reachable: voice does not arrive (firewall or NAT?)";

    readonly ManualTimeProvider time = new();
    readonly FakeVoiceServer server = new();
    readonly KeyPoller keys;
    readonly AudioEngine engine;
    readonly List<string> logLines = [];
    readonly VoiceLink link;

    public VoiceLinkTests()
    {
        keys = new KeyPoller(time);
        engine = new AudioEngine(keys, time, hardware: null);
        link = new VoiceLink(hear: engine.OnVoice, setSend: send => engine.Send = send, time: time, log: logLines.Add);
    }

    VoiceClient CreateClient() => new(server.EndPoint, FakeVoiceServer.SessionId, server.Key, time);

    /// <summary>Starts a client and has the server answer it, so UDP counts as reachable.</summary>
    async Task StartAnsweredClientAsync(int hellosSoFar)
    {
        link.Start(CreateClient());
        await AnswerHelloAsync(hellosSoFar);
    }

    /// <summary>Waits for the <paramref name="hellosSoFar"/>th Hello, has the server answer it and waits until UDP counts as reachable.</summary>
    async Task AnswerHelloAsync(int hellosSoFar)
    {
        await server.WaitForAsync(PacketType.Hello, hellosSoFar);
        await server.AnswerAsync();
        await AsyncWait.Until(() => link.Reachable == true);
    }

    // ---- Start ----

    [Fact]
    public async Task Start_AClient_SendsAHelloToTheServer()
    {
        link.Start(CreateClient());

        await server.WaitForAsync(PacketType.Hello);
        Assert.Contains(server.Received, header => header.Type == PacketType.Hello && header.SessionId == FakeVoiceServer.SessionId);
    }

    [Fact]
    public async Task Start_AClient_EngineSendsItsFramesThroughIt()
    {
        link.Start(CreateClient());

        engine.Send!(EngineProbe.SilentFrame(), VoiceHeader.TargetLinked);

        await server.WaitForAsync(PacketType.Voice);
        Assert.Equal(VoiceHeader.TargetLinked, Assert.Single(server.Received, header => header.Type == PacketType.Voice).Target);
    }

    [Fact]
    public async Task Start_AClient_EngineHearsWhatItReceives()
    {
        link.Start(CreateClient());
        await server.WaitForAsync(PacketType.Hello);

        await server.SendVoiceAsync(speaker: 9, frameNumber: 0, EngineProbe.SilentFrame());

        await AsyncWait.Until(() => engine.FramesReceived.ContainsKey(9));
        Assert.Equal(1, engine.FramesReceived[9]);
    }

    [Fact]
    public void Start_AClient_LinkHoldsIt()
    {
        var client = CreateClient();

        link.Start(client);

        Assert.Same(client, link.Client);
    }

    // ---- Stop ----

    [Fact]
    public void Stop_AStartedLink_ClearsTheEngineSend()
    {
        link.Start(CreateClient());

        link.Stop();

        Assert.Null(engine.Send);
    }

    [Fact]
    public void Stop_AStartedLink_ForgetsTheClient()
    {
        link.Start(CreateClient());

        link.Stop();

        Assert.Null(link.Client);
    }

    [Fact]
    public async Task Stop_AStartedLink_ClientSendsNothingMore()
    {
        link.Start(CreateClient());
        await server.WaitForAsync(PacketType.Hello);
        link.Stop();
        await server.DrainAsync();
        int before = server.Received.Count;

        time.Advance(TimeSpan.FromSeconds(10));
        await server.DrainAsync();

        Assert.Equal(before, server.Received.Count);
    }

    [Fact]
    public void Stop_NothingStarted_StillClearsTheEngineSend()
    {
        engine.Send = (_, _) => { };

        link.Stop();

        Assert.Null(engine.Send);
    }

    // ---- LogReachability ----

    [Fact]
    public void LogReachability_NoClient_LogsNothing()
    {
        link.LogReachability();

        Assert.Empty(logLines);
    }

    [Fact]
    public void LogReachability_NothingAnswersWithinFiveSeconds_LogsNothing()
    {
        link.Start(CreateClient());
        time.Advance(TimeSpan.FromSeconds(5));

        link.LogReachability();

        Assert.Empty(logLines);
    }

    [Fact]
    public void LogReachability_NothingAnswersAfterFiveSecondsAskedTwice_LogsNotReachableOnce()
    {
        link.Start(CreateClient());
        time.Advance(TimeSpan.FromSeconds(5) + TimeSpan.FromMilliseconds(1));

        link.LogReachability();
        link.LogReachability();

        Assert.Equal([NotReachableLine], logLines);
    }

    [Fact]
    public async Task LogReachability_ServerAnswersAskedTwice_LogsNowReachableOnce()
    {
        await StartAnsweredClientAsync(hellosSoFar: 1);

        link.LogReachability();
        link.LogReachability();

        Assert.Equal([NowReachableLine], logLines);
    }

    [Fact]
    public async Task LogReachability_AnswersStopForFifteenSeconds_LogsNotReachableAfterNowReachable()
    {
        await StartAnsweredClientAsync(hellosSoFar: 1);
        link.LogReachability();
        time.Advance(TimeSpan.FromSeconds(15));

        link.LogReachability();

        Assert.Equal([NowReachableLine, NotReachableLine], logLines);
    }

    [Fact]
    public async Task LogReachability_ServerAnswersAgainAfterAnOutage_LogsReachableAgain()
    {
        await StartAnsweredClientAsync(hellosSoFar: 1);
        link.LogReachability();
        time.Advance(TimeSpan.FromSeconds(15));
        link.LogReachability();
        await AnswerHelloAsync(hellosSoFar: 2);

        link.LogReachability();

        Assert.Equal(3, logLines.Count);
        Assert.Equal(ReachableAgainLine, logLines[^1]);
    }

    [Fact]
    public async Task LogReachability_NewClientAfterOneThatWasReachable_StartsOverWithNowReachable()
    {
        await StartAnsweredClientAsync(hellosSoFar: 1);
        link.LogReachability();
        link.Stop();
        await StartAnsweredClientAsync(hellosSoFar: 2);

        link.LogReachability();

        Assert.Equal([NowReachableLine, NowReachableLine], logLines);
    }

    [Fact]
    public void LogReachability_NewClientAfterOneThatWasNotReachable_StartsOverWithNotReachable()
    {
        link.Start(CreateClient());
        time.Advance(TimeSpan.FromSeconds(6));
        link.LogReachability();
        link.Stop();
        link.Start(CreateClient());
        time.Advance(TimeSpan.FromSeconds(6));

        link.LogReachability();

        Assert.Equal([NotReachableLine, NotReachableLine], logLines);
    }

    [Fact]
    public void LogReachability_NewClientTenSecondsAfterTheFirst_LogsNothingYet()
    {
        link.Start(CreateClient());
        time.Advance(TimeSpan.FromSeconds(10));
        link.Stop();
        link.Start(CreateClient());

        link.LogReachability();

        Assert.Empty(logLines);
    }

    // ---- ReadFigures ----

    [Fact]
    public void ReadFigures_NoClient_ReturnsNoFigures()
    {
        Assert.Equal(default, link.ReadFigures());
    }

    [Fact]
    public async Task ReadFigures_ServerAnswered_ReturnsTheRoundTripAndTheLossSeen()
    {
        link.Start(CreateClient());
        await server.WaitForAsync(PacketType.Hello);
        time.Advance(TimeSpan.FromMilliseconds(40));
        await server.SendVoiceAsync(speaker: 9, frameNumber: 0, EngineProbe.SilentFrame());
        await server.SendVoiceAsync(speaker: 9, frameNumber: 2, EngineProbe.SilentFrame());
        await AsyncWait.Until(() => engine.FramesReceived.GetValueOrDefault(9u) == 2);
        await AnswerHelloAsync(hellosSoFar: 1);

        var figures = link.ReadFigures();

        Assert.Equal(new VoiceLinkFigures(TimeSpan.FromMilliseconds(40), RoundTripCount: 1, Uplink: FakeVoiceServer.UplinkLoss, Downlink: new LossReport(Expected: 3, Missed: 1)), figures);
    }

    public void Dispose()
    {
        link.Stop();
        engine.Dispose();
        keys.Dispose();
        server.Dispose();
    }
}
