using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// Package 72, 85: a user card of the administration reaches the page only through the delegates it was made with;
/// each of its four buttons calls its own delegate with the card it sits on.
/// </summary>
public sealed class KnownUserViewModelTests
{
    static readonly string[] ActionNames = ["Ban", "Unban", "Delete", "LiftMute"];

    public static TheoryData<string> AllActions() => new(ActionNames);

    /// <summary>A card that allows every action unless <paramref name="allowed"/> is false.</summary>
    static KnownUserViewModel CreateCard(KnownUserActions actions, bool allowed = true) =>
        new(new KnownUserInfo("fpA", "anna", []), false, [], [], actions, new PendingViewModel(new ManualTimeProvider()),
            canBan: allowed, canUnban: allowed, canDelete: allowed, canLiftMute: allowed);

    /// <summary>A card whose delegates record their name and the card they got.</summary>
    static KnownUserViewModel CreateCardWithRecorder(List<(string Action, KnownUserViewModel Card)> calls, bool allowed = true)
    {
        Func<KnownUserViewModel, Task> Record(string name) => card =>
        {
            calls.Add((name, card));
            return Task.CompletedTask;
        };
        return CreateCard(new KnownUserActions(Record(ActionNames[0]), Record(ActionNames[1]), Record(ActionNames[2]), Record(ActionNames[3])), allowed);
    }

    static IAsyncRelayCommand SelectCommand(KnownUserViewModel card, string action) => action switch
    {
        "Ban" => card.BanCommand,
        "Unban" => card.UnbanCommand,
        "Delete" => card.DeleteCommand,
        "LiftMute" => card.LiftMuteCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    [Theory]
    [MemberData(nameof(AllActions))]
    public async Task Command_Executed_CallsOnlyItsDelegateWithTheCard(string action)
    {
        var calls = new List<(string Action, KnownUserViewModel Card)>();
        var card = CreateCardWithRecorder(calls);

        await HangGuard.Bound(SelectCommand(card, action).ExecuteAsync(null));

        var call = Assert.Single(calls);
        Assert.Equal(action, call.Action);
        Assert.Same(card, call.Card);
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public void Command_ActionNotAllowed_CannotExecute(string action)
    {
        var card = CreateCardWithRecorder([], allowed: false);

        Assert.False(SelectCommand(card, action).CanExecute(null));
    }

    [Fact]
    public void Actions_EveryDelegateOfTheRecord_HasACommandOnTheCard()
    {
        var names = typeof(KnownUserActions).GetProperties()
            .Select(property => property.Name).Order().ToList();

        Assert.Equal(ActionNames.Order(), names);
        Assert.All(names, name => Assert.NotNull(typeof(KnownUserViewModel).GetProperty(name + "Command")));
    }
}
