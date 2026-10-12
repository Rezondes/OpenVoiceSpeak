using System.Runtime.CompilerServices;
using OVS.Client.Services;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.Client.ViewModels;

namespace OVS.Tests.Client.Services;

/// <summary>The table of routed answers: each answer type with a request id reaches the route of its request.</summary>
public sealed class AnswerRoutesTests
{
    readonly AnswerRoutes routes = new();
    readonly List<Message> received = [];

    /// <summary>One answer of the type named <paramref name="typeName"/> for the request <paramref name="requestId"/>.</summary>
    static Message CreateAnswer(string typeName, string? requestId)
    {
        var type = typeof(Message).Assembly.GetTypes().Single(candidate => candidate.Name == typeName && candidate.IsSubclassOf(typeof(Message)));
        var answer = (Message)RuntimeHelpers.GetUninitializedObject(type);
        type.GetProperty("RequestId")!.SetValue(answer, requestId);
        return answer;
    }

    /// <summary>Every routed answer type but the server's error, which the server view model delivers itself after its notice.</summary>
    [Theory]
    [MemberData(nameof(AnswerRoutingTests.RoutedAnswerNames), MemberType = typeof(AnswerRoutingTests))]
    public void DeliverAnswer_EveryRoutedType_ReachesItsRoute(string typeName)
    {
        routes.Add("r1", received.Add);
        var answer = CreateAnswer(typeName, "r1");

        var delivered = routes.DeliverAnswer(answer);

        Assert.Equal(typeName != nameof(Error), delivered);
        Assert.Equal(typeName != nameof(Error) ? [answer] : [], received);
    }

    [Fact]
    public void DeliverAnswer_ServerIconOrChannelAdded_ReachesNoRouteAndIsFalse()
    {
        routes.Add("r1", received.Add);

        Assert.False(routes.DeliverAnswer(new ServerIcon("r1", null, null)));
        Assert.False(routes.DeliverAnswer(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 9), "r1")));
        Assert.Empty(received);
    }

    [Fact]
    public void DeliverAnswer_NullRequestId_IsTrueAndReachesNoRoute()
    {
        routes.Add("r1", received.Add);

        Assert.True(routes.DeliverAnswer(new UserList(null!, [])));
        Assert.Empty(received);
    }

    [Fact]
    public void DeliverAnswer_UnknownRequestId_IsTrueAndReachesNoRoute()
    {
        routes.Add("r1", received.Add);

        Assert.True(routes.DeliverAnswer(new UserList("r2", [])));
        Assert.Empty(received);
    }

    [Fact]
    public void DeliverAnswer_HandlerThrows_Propagates()
    {
        routes.Add("r1", _ => throw new InvalidOperationException("Empfänger kaputt"));

        Assert.Throws<InvalidOperationException>(() => routes.DeliverAnswer(new UserList("r1", [])));
    }
}
