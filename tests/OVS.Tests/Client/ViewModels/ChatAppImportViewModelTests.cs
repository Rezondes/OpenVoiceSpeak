using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Tests.Client.ViewModels;

/// <summary>Package 187 (A200): one button per chat app; each reads only its own app.</summary>
public sealed class ChatAppImportViewModelTests
{
    static readonly ImportedAudio Fluxer = new("Fluxer") { AutoGain = false };

    /// <summary>Package 187 (AC2): Fluxer's button reads Fluxer while Discord is installed too; Discord's reads only Discord.</summary>
    [Fact]
    public async Task Import_Fluxer_ReadsOnlyFluxerWhileDiscordInstalled()
    {
        var read = new List<string>();
        var page = new SettingsViewModel(new ClientSettings(), [], [], TimeProvider.System)
        {
            ImportApps =
            [
                new AudioImportApp("Discord", () => true, () => { read.Add("Discord"); return new AudioImportResult(new ImportedAudio("Discord"), null); }),
                new AudioImportApp("Fluxer", () => true, () => { read.Add("Fluxer"); return new AudioImportResult(Fluxer, null); }),
            ],
            AskImport = plan => Task.FromResult<IReadOnlyList<ImportChange>?>(plan.Changes),
        };

        await page.Imports[1].ImportCommand.ExecuteAsync(null);

        Assert.Equal(["Fluxer"], read);
        Assert.False(page.AutoGain);
        await page.Imports[0].ImportCommand.ExecuteAsync(null);
        Assert.Equal(["Fluxer", "Discord"], read);
    }

    /// <summary>Package 187 (AC3): a button whose app is not installed is off and its tooltip names that app.</summary>
    [Fact]
    public void Button_AppNotInstalled_OffWithAppNameInTip()
    {
        var page = new SettingsViewModel(new ClientSettings(), [], [], TimeProvider.System)
        {
            ImportApps = [new AudioImportApp("Fluxer", () => false, () => throw new InvalidOperationException("never read"))],
        };

        var fluxer = Assert.Single(page.Imports);

        Assert.False(fluxer.ImportCommand.CanExecute(null));
        Assert.Equal(TextFormat.Of(Strings.Import_NoneFoundFmt, "Fluxer"), fluxer.Tip);
        Assert.Contains("Fluxer", fluxer.Tip, StringComparison.Ordinal);
    }

    /// <summary>Package 187 (AC4): a storage that cannot be read shows the error under that app's button only, the reason in the log.</summary>
    [Fact]
    public async Task Import_Unreadable_ErrorUnderThisButtonOnly()
    {
        var problems = new List<string>();
        var page = new SettingsViewModel(new ClientSettings(), [], [], TimeProvider.System)
        {
            ImportApps =
            [
                new AudioImportApp("Discord", () => true, () => new AudioImportResult(null, "discord: locked")),
                new AudioImportApp("Fluxer", () => true, () => new AudioImportResult(Fluxer, null)),
            ],
            LogImportProblem = problems.Add,
        };

        await page.Imports[0].ImportCommand.ExecuteAsync(null);

        Assert.Equal((TextFormat.Of(Strings.Import_FailedFmt, "Discord"), null), (page.Imports[0].Error, page.Imports[1].Error));
        Assert.Equal(["discord: locked"], problems);
        Assert.False(page.HasChanges);
    }
}
