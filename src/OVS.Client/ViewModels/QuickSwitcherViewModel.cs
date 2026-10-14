using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Settings;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 163: the quick switcher (Ctrl+K). Its hits are the channels of the connected server (separators never) and
/// the saved servers, by how well their name matches (the same name, then the start, then a word's start, then anywhere),
/// channels before servers when equal. Enter joins a channel as a double click does, or connects to a server as a
/// click on its bookmark does.
/// </summary>
internal sealed partial class QuickSwitcherViewModel : ObservableObject
{
    readonly IReadOnlyList<QuickSwitchHit> all;
    readonly Func<Bookmark, Task> connect;

    [ObservableProperty] string query = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoHits))]
    IReadOnlyList<QuickSwitchHit> hits;
    [ObservableProperty] int selectedIndex;

    /// <param name="channels">The channels of the connected server, none without a connection.</param>
    /// <param name="servers">The saved servers.</param>
    /// <param name="connect">Connects to a saved server as a click on its bookmark does.</param>
    public QuickSwitcherViewModel(IEnumerable<ChannelViewModel> channels, IEnumerable<Bookmark> servers, Func<Bookmark, Task> connect)
    {
        this.connect = connect;
        all =
        [
            .. channels.Where(channel => !channel.IsSeparator).Select(channel => new QuickSwitchHit(channel.Name, TextFormat.Of(Strings.Quick_Users, channel.Users.Count), channel, null)),
            .. servers.Select(server => new QuickSwitchHit(server.Name, Strings.Quick_Server, null, server)),
        ];
        hits = all;
    }

    /// <summary>Nothing matches what was typed.</summary>
    public bool HasNoHits => Hits.Count == 0;

    /// <summary>The hit Enter opens, null when there is none.</summary>
    public QuickSwitchHit? Selected => SelectedIndex >= 0 && SelectedIndex < Hits.Count ? Hits[SelectedIndex] : null;

    partial void OnQueryChanged(string value)
    {
        // OrderBy keeps the order of equal ranks: channels before servers, each as listed
        Hits = string.IsNullOrWhiteSpace(value) ? all
            : all.Select(hit => (Hit: hit, Rank: TextMatch.Rank(hit.Title, value))).Where(ranked => ranked.Rank is not null).OrderBy(ranked => ranked.Rank).Select(ranked => ranked.Hit).ToList();
        SelectedIndex = Hits.Count > 0 ? 0 : -1;
    }

    /// <summary>Up and Down: the hit above or below, staying inside the list.</summary>
    public void Move(int by)
    {
        if (Hits.Count > 0) SelectedIndex = Math.Clamp(SelectedIndex + by, 0, Hits.Count - 1);
    }

    /// <summary>Joins the channel (asking for its password, or refused by its lock, as a double click) or connects to the server.</summary>
    public Task ChooseAsync(QuickSwitchHit hit) =>
        hit.Channel is { } channel ? channel.JoinCommand.ExecuteAsync(null)
        : hit.Server is { } server ? connect(server) : Task.CompletedTask;
}
