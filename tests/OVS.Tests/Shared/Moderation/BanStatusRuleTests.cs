using OVS.Shared.Moderation;

namespace OVS.Tests.Shared.Moderation;

public class BanStatusRuleTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<int?, int?, BanStatus> Cases => new()
    {
        // lifted (hours from now, null = not lifted), end (hours from now, null = permanent), expected
        { null, null, BanStatus.Active },
        { null, 1, BanStatus.Active },
        { null, 0, BanStatus.Expired }, // an end equal to now has passed
        { null, -1, BanStatus.Expired },
        { -2, null, BanStatus.Lifted },
        { -2, 1, BanStatus.Lifted },
        { -2, -1, BanStatus.Lifted }, // lifted wins over expired
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Evaluate_LiftedAndEnd_GivesStatus(int? liftedHours, int? endHours, BanStatus expected) =>
        Assert.Equal(expected, BanStatusRule.Evaluate(At(liftedHours), At(endHours), Now));

    static DateTimeOffset? At(int? hours) => hours is { } hourCount ? Now.AddHours(hourCount) : null;
}
