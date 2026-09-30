using OVS.Client.ViewModels;

namespace OVS.Tests.Client;

public class ListPagesTests
{
    static IReadOnlyList<int> Page(int from, int count) => Enumerable.Range(from, count).ToList();

    [Fact]
    public void OverlappingRounds_OnlyTheNewestRoundIsShown()
    {
        var pages = new ListPages<int>();
        var asked = new List<(string? Id, int Offset)>();
        void Ask(string? id, int offset) => asked.Add((id, offset));

        Assert.Null(pages.Add("A", 0, 300, Page(0, 200), Ask));
        Assert.Null(pages.Add("B", 0, 300, Page(1000, 200), Ask)); // a new round starts while A still fetches
        Assert.Equal([("A", 200), ("B", 200)], asked); // the continuation carries its round
        Assert.Null(pages.Add("A", 200, 300, Page(200, 100), Ask)); // the old round's page is dropped
        var list = pages.Add("B", 200, 300, Page(1200, 100), Ask);

        Assert.Equal(Page(1000, 300), list);
    }

    [Fact]
    public void UnrequestedFirstPage_StartsACleanRound()
    {
        var pages = new ListPages<int>();
        var asked = new List<(string? Id, int Offset)>();
        void Ask(string? id, int offset) => asked.Add((id, offset));

        Assert.Null(pages.Add("A", 0, 300, Page(0, 200), Ask));
        Assert.Equal([5, 6], pages.Add("unban", 0, 2, Page(5, 2), Ask)); // e.g. the answer to an Unban, complete in itself
        Assert.Null(pages.Add("A", 200, 300, Page(200, 100), Ask)); // the round it replaced stays closed
        Assert.Single(asked);
    }

    [Fact]
    public void PageOfTheRightRound_AtTheWrongOffset_IsDropped()
    {
        var pages = new ListPages<int>();
        Assert.Null(pages.Add("A", 0, 500, Page(0, 200), (_, _) => { }));
        Assert.Null(pages.Add("A", 400, 500, Page(400, 100), (_, _) => { }));
        Assert.Null(pages.Add("A", 200, 500, Page(200, 200), (_, _) => { }));
        Assert.Equal(Page(0, 500), pages.Add("A", 400, 500, Page(400, 100), (_, _) => { }));
    }
}
