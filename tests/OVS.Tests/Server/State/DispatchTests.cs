using OVS.Server;
using OVS.Server.State;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.RequestSamples;

namespace OVS.Tests.Server.State;

/// <summary>Every request type of the protocol reaches a handler: none is answered with UnknownRequest.</summary>
public sealed class DispatchTests
{
    static IEnumerable<Type> RequestTypes() =>
        typeof(Request).Assembly.GetTypes().Where(candidate => candidate.IsSubclassOf(typeof(Request)) && !candidate.IsAbstract).OrderBy(candidate => candidate.Name, StringComparer.Ordinal);

    public static TheoryData<string> RequestTypeNames => new(RequestTypes().Select(candidate => candidate.Name));

    [Fact]
    public void RoutesAndFactories_EveryRequestType_ExactlyOneEntryEach()
    {
        Assert.Equal(RequestTypes().Select(candidate => candidate.Name), ServerState.Routes.Keys.Select(candidate => candidate.Name).Order(StringComparer.Ordinal));
        Assert.Equal(RequestTypes().Select(candidate => candidate.Name), Factories.Keys.Select(candidate => candidate.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RequestTypeNames))]
    public void Handle_EveryRequestType_ReachesItsHandler(string typeName)
    {
        var type = RequestTypes().Single(candidate => candidate.Name == typeName);
        Assert.True(Factories.TryGetValue(type, out var create), $"No factory entry for {typeName}");
        var request = create() with { RequestId = "r" };
        Assert.Null(RequestShape.Problem(request)); // a malformed request would be answered before the dispatch
        using var admin = ClientIdentity.Create();
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var session = harness.Admit("admin", admin.Fingerprint); // a fresh session: no request budget used yet
        StateHarness.Drain(session);

        harness.Handle(session, request);

        var answers = StateHarness.Drain(session);
        Assert.DoesNotContain(answers, message => message is Error { Code: ReasonCodes.UnknownRequest });
        Assert.DoesNotContain(answers, message => message is Error { Code: ReasonCodes.RateLimited }); // not stopped by the request budget before its handler
    }

    /// <summary>Transfers (one chunk per answer) are exempt from the request budget; every other request counts.</summary>
    [Fact]
    public void Handle_PastTheRequestBudget_OnlyTransfersPass()
    {
        using var harness = new StateHarness();
        using var annaId = ClientIdentity.Create(); // one identity, so the nickname stays hers
        var anna = annaId.Fingerprint;
        var passed = new List<string>();
        foreach (var type in RequestTypes())
        {
            var session = harness.Admit("anna", anna);
            for (int index = 0; index < ServerTuning.RequestBurst; index++) harness.Handle(session, new JoinChannel(Unknown));
            StateHarness.Drain(session);

            harness.Handle(session, Factories[type]() with { RequestId = "r" });

            if (!StateHarness.Drain(session).Any(message => message is Error { RequestId: "r", Code: ReasonCodes.RateLimited })) passed.Add(type.Name);
            harness.State.Remove(session);
        }
        Assert.Equal(["DownloadBackup", "DownloadLogChunk", "UploadBackupChunk"], passed);
    }
}
