using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.ViewModels;

/// <summary>
/// The settings card of the server tab: name, welcome text, password and the limits, edited in place and sent with one
/// save, and kept in step with what the server reports (the server logo stays with the page).
/// </summary>
internal sealed partial class ServerSettingsViewModel : ObservableObject
{
    readonly ServerViewModel server;

    [ObservableProperty] string name = "";
    [ObservableProperty] string welcomeText = "";
    [ObservableProperty] string newPassword = "";
    [ObservableProperty] bool removePassword;
    [ObservableProperty] bool hasPassword;
    // Package 69: limits, logs and restart; null limits (not sent yet) leave them unchanged on save
    ServerLimits? loadedLimits;
    [ObservableProperty] decimal? maxUsers;
    [ObservableProperty] decimal? logDays;
    [ObservableProperty] bool logRotateDaily;
    [ObservableProperty] bool autoRestart;
    [ObservableProperty] TimeSpan? autoRestartTime;
    /// <summary>Package 83: why the settings would be refused, null while they are fine.</summary>
    [ObservableProperty] string? error;

    /// <summary>What the card's fields were loaded with (the server's settings then).</summary>
    string loadedName = "", loadedWelcome = "";
    ServerSettingsInfo? seenSettings;

    internal ServerSettingsViewModel(ServerViewModel server)
    {
        this.server = server;
        name = loadedName = server.Mirror.Settings.Name;
        welcomeText = loadedWelcome = server.Mirror.Settings.WelcomeText;
        seenSettings = server.Mirror.Settings;
        hasPassword = server.Mirror.Settings.HasPassword;
        LoadLimits();
        Place = AdminPlace.Create(() => HasChanges, null, Send, Reload);
    }

    /// <summary>Package 114: the place of the server tab: the settings are sent with one request (nothing else saves them), or loaded again.</summary>
    internal AdminPlace Place { get; }

    /// <summary>Package 76: the settings are shown and changed with ServerConfig. The page raises the change notification for the bound card (<c>AdminViewModel.ShowServerSettings</c>).</summary>
    internal bool ShowServerSettings => server.SelfPermissions.Has(Permission.ServerConfig);

    /// <summary>The server's state changed: the password flag, limits that arrive late, and new settings from the server follow.</summary>
    internal void OnStateChanged()
    {
        HasPassword = server.Mirror.Settings.HasPassword;
        if (loadedLimits is null) LoadLimits(); // e.g. arriving with the ServerConfig right; later ones never overwrite edits
        if (!ReferenceEquals(server.Mirror.Settings, seenSettings)) // Package 114
        {
            seenSettings = server.Mirror.Settings;
            SettingsArrived();
        }
    }

    /// <summary>The server's normal form: a trimmed name, "\r\n" as "\n" (TextRules), so a saved edit is no change.</summary>
    static string NormName(string? text) => text?.Trim() ?? "";
    static string NormText(string? text) => (text ?? "").Replace("\r\n", "\n");

    ServerLimits EditedLimits(ServerLimits loaded) =>
        new((int)(MaxUsers ?? loaded.MaxUsers), (int)(LogDays ?? loaded.LogDays), LogRotateDaily, AutoRestart,
            AutoRestartTime is { } restartTime ? TimeOnly.FromTimeSpan(restartTime) : loaded.AutoRestartTime);

    /// <summary>The card differs from what its fields were loaded with.</summary>
    internal bool HasChanges =>
        ShowServerSettings && (NormName(Name) != NormName(loadedName) || NormText(WelcomeText) != NormText(loadedWelcome)
                               || NewPassword.Length > 0 || RemovePassword || (loadedLimits is { } loaded && EditedLimits(loaded) != loaded));

    /// <summary>
    /// New settings from the server, field by field: an untouched field shows them (a change by another admin), and so
    /// does an edited one that is now what the server has (the own save landed); an edit that still differs stays, and
    /// so do the password fields (only a save empties them).
    /// </summary>
    void SettingsArrived()
    {
        var settings = server.Mirror.Settings;
        if (NormName(Name) == NormName(loadedName) || NormName(Name) == NormName(settings.Name)) Name = loadedName = settings.Name;
        if (NormText(WelcomeText) == NormText(loadedWelcome) || NormText(WelcomeText) == NormText(settings.WelcomeText)) WelcomeText = loadedWelcome = settings.WelcomeText;
        if (loadedLimits is { } loaded && settings.Limits is { } now && now != loaded && (EditedLimits(loaded) == loaded || EditedLimits(loaded) == now))
        {
            loadedLimits = null;
            LoadLimits();
        }
    }

    /// <summary>Throws the edits away: the fields show the server's settings again.</summary>
    internal void Reload()
    {
        Name = loadedName = server.Mirror.Settings.Name;
        WelcomeText = loadedWelcome = server.Mirror.Settings.WelcomeText;
        NewPassword = "";
        RemovePassword = false;
        Error = null;
        loadedLimits = null;
        LoadLimits();
    }

    internal bool HasLimits => loadedLimits is not null;

    void LoadLimits()
    {
        if (server.Mirror.Settings.Limits is not { } limits) return;
        loadedLimits = limits;
        MaxUsers = limits.MaxUsers;
        LogDays = limits.LogDays;
        LogRotateDaily = limits.LogRotateDaily;
        AutoRestart = limits.AutoRestart;
        AutoRestartTime = limits.AutoRestartTime.ToTimeSpan();
        OnPropertyChanged(nameof(HasLimits));
    }

    [RelayCommand]
    Task Save()
    {
        Send();
        return Task.CompletedTask;
    }

    /// <summary>Package 114: waits for the server's new settings; null when the input breaks the server's rules.</summary>
    internal PendingViewModel? Send()
    {
        // Package 83: the server's rules, checked before anything is sent or cleared
        Error = TextRules.NormalizeName(Name, ProtocolLimits.MaxNameLength) is null ? Strings.Dlg_NameInvalid
            : TextRules.NormalizeText(WelcomeText, ProtocolLimits.MaxTextLength) is null ? Strings.Dlg_TextInvalid
            : !RemovePassword && NewPassword.Length > ProtocolLimits.MaxPasswordLength ? Strings.Ui_PasswordTooLong
            : null;
        if (Error is not null) return null;
        string? password = RemovePassword ? "" : NewPassword.Length > 0 ? NewPassword : null;
        NewPassword = "";
        RemovePassword = false;
        // the server checks the ranges again; an emptied number field keeps the last known value
        var limits = loadedLimits is { } loaded
            ? new ServerLimits((int)(MaxUsers ?? loaded.MaxUsers), (int)(LogDays ?? loaded.LogDays), LogRotateDaily, AutoRestart,
                AutoRestartTime is { } restartTime ? TimeOnly.FromTimeSpan(restartTime) : loaded.AutoRestartTime)
            : null;
        return server.SendConfirmed(new UpdateServerSettings(Name, WelcomeText, password, limits), answer => answer is ServerSettingsChanged);
    }
}
