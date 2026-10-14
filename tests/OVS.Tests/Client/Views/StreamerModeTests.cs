using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Views;

/// <summary>Package 160: streamer mode shows no IP address, no full fingerprint and no server address.</summary>
[Collection(MotionCollection.Name)]
public sealed partial class StreamerModeTests() : TempDirectoryTest("ovs-streamer-", quiet: true)
{
    /// <summary>The crowded administration of <c>ResponsiveTests</c> with streamer mode on.</summary>
    MainWindow OpenAdmin()
    {
        var main = CrowdedWindows.OpenAdmin(dir, 1100);
        SetStreamerMode(main, true);
        return main;
    }

    static void SetStreamerMode(MainWindow main, bool on)
    {
        var viewModel = (MainViewModel)main.DataContext!;
        var settings = viewModel.Settings;
        settings.StreamerMode = on;
        viewModel.ApplySettings(settings);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Every text the window shows or offers as a tooltip, on every page of the administration.</summary>
    static List<string> TextsOnEveryPage(MainWindow main)
    {
        var texts = new List<string>();
        foreach (var page in AdminPages.All(main))
        {
            AdminPages.Open(main, page);
            foreach (var visual in main.GetVisualDescendants())
            {
                if (visual is TextBlock textBlock)
                {
                    texts.Add(textBlock.Text ?? "");
                    if (textBlock.Inlines is { } inlines) texts.Add(string.Concat(inlines.OfType<Run>().Select(run => run.Text)));
                }
                if (visual is TextBox { PasswordChar: '\0' } textBox) texts.Add(textBox.Text ?? "");
                if (visual is Control control && ToolTip.GetTip(control) is string tip) texts.Add(tip);
            }
        }
        return texts;
    }

    [AvaloniaFact]
    public void On_AdminAndHeader_NothingRevealed()
    {
        var main = OpenAdmin();
        try
        {
            Assert.True(main.FindControl<Border>("StreamerChip")!.IsVisible);
            var texts = TextsOnEveryPage(main);
            Assert.Contains(texts, text => text.Contains("Mitspieler", StringComparison.Ordinal)); // nicknames stay
            Assert.DoesNotContain(texts, text => text.Contains("2001:db8", StringComparison.Ordinal));
            Assert.DoesNotContain(texts, text => FullFingerprint().IsMatch(text));

            SetStreamerMode(main, false); // off again: the values are back
            Assert.False(main.FindControl<Border>("StreamerChip")!.IsVisible);
            texts = TextsOnEveryPage(main);
            Assert.Contains(texts, text => text.Contains("2001:db8", StringComparison.Ordinal));
            Assert.Contains(texts, text => FullFingerprint().IsMatch(text));
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void On_SelectableFingerprint_NotCopyable()
    {
        var main = OpenAdmin();
        try
        {
            var selected = new List<string>();
            foreach (var page in AdminPages.All(main))
            {
                AdminPages.Open(main, page);
                foreach (var selectable in main.GetVisualDescendants().OfType<SelectableTextBlock>())
                {
                    selectable.SelectAll();
                    selected.Add(selectable.SelectedText);
                }
            }
            Assert.NotEmpty(selected);
            Assert.DoesNotContain(selected, text => FullFingerprint().IsMatch(text) || text.Contains("2001:db8", StringComparison.Ordinal));
        }
        finally
        {
            main.Close();
        }
    }

    [Fact]
    public async Task Key_Pressed_TogglesAndSaves()
    {
        using var ui = new TestDispatcher();
        var keys = new FakeKeys();
        var viewModel = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(dir, ui.Post, keyState: keys.IsDown)));
        try
        {
            Assert.False(await ui.ReadAsync(() => viewModel.StreamerMode));
            await PressUntilAsync(ui, viewModel, () => viewModel.StreamerMode); // without a connection and without the settings page
            Assert.True(ClientSettings.Load(dir, out _).StreamerMode);
            await PressUntilAsync(ui, viewModel, () => !viewModel.StreamerMode);
            Assert.False(ClientSettings.Load(dir, out _).StreamerMode);
        }
        finally
        {
            await ui.InvokeAsync<object?>(async () =>
            {
                await viewModel.DisposeAsync();
                return null;
            });
        }
    }

    /// <summary>Holds the key until the client has seen it, then lets go until the key thread has seen that too.</summary>
    static async Task PressUntilAsync(TestDispatcher ui, MainViewModel viewModel, Func<bool> done)
    {
        viewModel.Keys.Simulate(KeyAction.ToggleStreamerMode, true);
        try
        {
            await AsyncWait.UntilAsync(() => ui.ReadAsync(done), TimeSpan.FromSeconds(10));
        }
        finally
        {
            viewModel.Keys.Simulate(KeyAction.ToggleStreamerMode, false);
        }
        await AsyncWait.UntilAsync(() => !viewModel.Keys.IsDown(KeyAction.ToggleStreamerMode), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Views_EveryPrivateBinding_ThroughConverter()
    {
        var found = new List<string>();
        foreach (var file in RepoPaths.SourceFiles(RepoPaths.ClientSource, "*.axaml"))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (Match match in BindingPath().Matches(lines[index]))
                {
                    if (!PrivatePath().IsMatch(match.Groups["path"].Value)) continue;
                    // only the value of a MultiBinding whose converter is one of PrivacyConverters, on the line above
                    var throughConverter = match.Value.StartsWith("<Binding", StringComparison.Ordinal) && index > 0
                        && lines[index - 1].Contains("PrivacyConverters", StringComparison.Ordinal);
                    if (!throughConverter) found.Add($"{Path.GetFileName(file)}:{index + 1}: {match.Value}");
                }
            }
        }
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])")]
    private static partial Regex FullFingerprint();

    // {Binding Path ...} and <Binding Path="Path" />
    [GeneratedRegex(@"\{Binding (?<path>[\w.]+)|<Binding Path=""(?<path>[\w.]+)""")]
    private static partial Regex BindingPath();

    // the last part of a path names an IP, a fingerprint, a host or an address (HasIp and ShortFingerprint show none)
    [GeneratedRegex(@"(?:^|\.)(?!Has|Short)\w*(?:Ip|Fingerprint|Host|Address)(?:Text)?$")]
    private static partial Regex PrivatePath();
}
