using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Shared.Logging;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>Package 165: the page "Audit-Log" from the server log's audit lines.</summary>
public sealed class AuditLogViewModelTests
{
    const string File = "server/2026-10-15_00-00-00.log";

    static LogHit Hit(string time, string actor, string action) => new(File, 1, $"2026-10-15 {time}.000 {AuditLine.Prefix(actor)}{action}");

    static readonly LogHit[] Hits =
    [
        Hit("09:00:00", "chef", "Group 'Team' created by chef (Speak)"),
        Hit("11:30:00", "mod", "anna was kicked by mod: Spam"),
        new(File, 2, "2026-10-15 10:00:00.000 Logs searched by chef"), // a hit without the marker in its place is no audit line
        Hit("10:15:00", "Chef der Gilde", "Channel 'Raid' created by Chef der Gilde"),
    ];

    readonly List<Request> sent = [];

    AuditLogViewModel Load(Func<Request, Message?> reply)
    {
        var page = new AdminViewModel(FakeServers.Admin(sent, reply: reply)).AuditLog;
        page.RefreshCommand.Execute(null);
        return page;
    }

    static Message? Found(Request request) => request is SearchLogs ? new LogSearchResult(request.RequestId, Hits, false, false) : null;

    [Fact]
    public void Load_SearchesAuditMarker_NewestFirst()
    {
        var page = Load(Found);
        var search = Assert.Single(sent.OfType<SearchLogs>());
        Assert.Equal(("[audit]", (LogKind?)LogKind.Server), (search.Query, search.Kind));
        Assert.Equal(["mod", "Chef der Gilde", "chef"], page.Rows.Select(row => row.Actor)); // newest first, the plain line left out
        Assert.Equal("anna was kicked by mod: Spam", page.Rows[0].Action);
        Assert.False(page.HasNoRows);
        Assert.Null(page.Note);
    }

    [Fact]
    public void PersonFilter_Chosen_OnlyThatActor()
    {
        var page = Load(Found);
        Assert.Equal([Strings.Audit_Everyone, "chef", "Chef der Gilde", "mod"], page.People);
        page.SelectedPerson = "chef";
        Assert.Equal(["Group 'Team' created by chef (Speak)"], page.Rows.Select(row => row.Action)); // not "Chef der Gilde"
        page.SelectedPerson = Strings.Audit_Everyone;
        page.SearchText = "raid";
        Assert.Equal(["Chef der Gilde"], page.Rows.Select(row => row.Actor));
        page.SearchText = "zzz";
        Assert.True(page.HasNoRows);
    }

    [Fact]
    public void Load_Error_ShownWithId()
    {
        const string Id = "0123456789abcdef0123456789abcdef";
        var page = Load(request => request is SearchLogs ? new Error(request.RequestId, ReasonCodes.InvalidValue, ErrorId: Id) : null);
        Assert.True(page.Loading.HasError);
        Assert.Contains(Id, page.Loading.Error, StringComparison.Ordinal);
        Assert.Empty(page.Rows);
        Assert.False(page.HasNoRows); // no "Keine Einträge" while the search failed
    }
}
