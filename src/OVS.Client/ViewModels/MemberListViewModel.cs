using System.Collections.ObjectModel;
using OVS.Client.Localization;
using OVS.Client.Models;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 167: the member list beside the chat. Everyone online under one heading per group (the server's group order,
/// Package 37), each user under their highest group only, then "Online" for users without a group; it follows joins,
/// leaves and group changes. The rows are the channel tree's own user items, so the menu is the same.
/// </summary>
internal sealed class MemberListViewModel
{
    readonly ServerViewModel server;

    /// <param name="server">The connected server.</param>
    public MemberListViewModel(ServerViewModel server)
    {
        this.server = server;
        server.StateChanged += Rebuild;
        Rebuild();
    }

    /// <summary>The headings with their members, in group order.</summary>
    public ObservableCollection<MemberSection> Sections { get; } = [];

    /// <summary>Stops following the server (another server or none is shown).</summary>
    public void Detach() => server.StateChanged -= Rebuild;

    void Rebuild()
    {
        var groups = server.Mirror.Groups;
        var byGroup = server.Tree.Users
            .GroupBy(user => groups.FirstOrDefault(group => user.GroupIds.Contains(group.Id))?.Id)
            .ToDictionary(grouping => grouping.Key ?? Guid.Empty, grouping => (IReadOnlyList<UserViewModel>)[.. grouping.OrderBy(user => user.Nickname, StringComparer.CurrentCultureIgnoreCase)]);
        var sections = groups.Where(group => byGroup.ContainsKey(group.Id)).Select(group => new MemberSection(group.Name, byGroup[group.Id])).ToList();
        if (byGroup.TryGetValue(Guid.Empty, out var others)) sections.Add(new MemberSection(Strings.Members_Online, others));
        if (sections.SequenceEqual(Sections)) return; // same headings and the same rows: nothing to redraw
        Sections.Clear();
        foreach (var section in sections) Sections.Add(section);
    }
}
