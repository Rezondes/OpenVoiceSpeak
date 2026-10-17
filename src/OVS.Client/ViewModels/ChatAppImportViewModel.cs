using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.Settings;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 187 (A200): the Audio page's button for one chat app ("Aus Fluxer übernehmen"): reads only that app, asks
/// through the dialog of Package 171 and sets the checked values on the page (previewed, saved only with "Speichern").
/// </summary>
internal sealed partial class ChatAppImportViewModel : ObservableObject
{
    readonly AudioImportApp app;
    readonly SettingsViewModel page;

    /// <param name="app">The app this button reads.</param>
    /// <param name="page">The page the taken values go to; it holds the dialog and the log.</param>
    public ChatAppImportViewModel(AudioImportApp app, SettingsViewModel page)
    {
        this.app = app;
        this.page = page;
        CanImport = app.IsInstalled();
    }

    /// <summary>The button's text.</summary>
    public string Text => TextFormat.Of(Strings.Import_ButtonFmt, app.Name);

    /// <summary>The app is installed; without it the button is off and its tooltip says so.</summary>
    public bool CanImport { get; }

    /// <summary>What the button does, or why it is off.</summary>
    public string Tip => TextFormat.Of(CanImport ? Strings.Import_ButtonHintFmt : Strings.Import_NoneFoundFmt, app.Name);

    /// <summary>Shown under this button when nothing could be read.</summary>
    [ObservableProperty] string? error;

    /// <summary>Reads this app (off the UI thread), asks, and sets the checked values on the page.</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    async Task ImportAsync()
    {
        Error = null;
        var result = await Task.Run(app.Read);
        if (result.Audio is not { } audio)
        {
            Error = TextFormat.Of(Strings.Import_FailedFmt, app.Name);
            page.LogImportProblem?.Invoke(result.Problem ?? "nothing found");
            return;
        }
        if (page.AskImport is not { } ask || await ask(ImportMapping.Map(audio, page)) is not { } chosen) return;
        foreach (var change in chosen) change.Apply(page);
    }
}
