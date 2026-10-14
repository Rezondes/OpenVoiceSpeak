using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OVS.Client.Layout;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Views;

/// <summary>Package 158: the search above the settings list finds a setting by its text and opens its page.</summary>
[Collection(MotionCollection.Name)]
public sealed class SettingsSearchTests
{
    /// <summary>The settings page alone in a window; its window is the view's parent.</summary>
    static SettingsView Show()
    {
        var view = new SettingsView { DataContext = new SettingsViewModel(new ClientSettings(), [], [], TimeProvider.System) };
        new Window { Width = 1100, Height = 800, Content = view }.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    static void Type(SettingsView view, string text)
    {
        var box = view.FindControl<TextBox>("SettingsSearchBox")!;
        box.Focus();
        box.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Find_PartOfLabel_HitWithPage()
    {
        var view = Show();
        var window = (Window)view.Parent!;
        try
        {
            var search = new NavSearch(SettingsPages.Sections(view));
            Assert.Contains(search.Find("Rausch"), hit => hit.Text == Strings.Ui_NoiseSuppression && hit.PageName == Strings.Nav_Audio);
            Assert.Contains(search.Find("push"), hit => hit.PageName == Strings.Nav_Transmit);
            Assert.True(search.Find("Sprache").Any(hit => hit.Text == Strings.Settings_Language && hit.PageName == Strings.Nav_Appearance), string.Join(" | ", search.Find("Sprache").Select(hit => hit.Text + "@" + hit.PageName)));
            Assert.Contains(search.Find("gerate"), hit => hit.Text == Strings.Ui_DevicesSection); // without the umlaut
            Assert.Empty(search.Find("   "));
            Assert.True(search.Find("e").Count <= NavSearch.MaxHits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Choose_Hit_OpensPageAndScrolls()
    {
        var view = Show();
        var window = (Window)view.Parent!;
        try
        {
            SettingsPages.Open(view, Strings.Nav_About);
            Type(view, "Rausch");
            Assert.True(view.FindControl<ListBox>("SettingsSearchHits")!.IsVisible);
            Assert.True(view.FindControl<TextBox>("SettingsSearchBox")!.IsFocused, "focus");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            MotionWait.Settle(50); // the page is laid out before the setting is scrolled to and lit
            Assert.Equal(Strings.Nav_Audio, ((TabItem)SettingsPages.Sections(view).SelectedItem!).Header);
            var first = new NavSearch(SettingsPages.Sections(view)).Find("Rausch")[0];
            Assert.Equal(Strings.Ui_NoiseSuppression, first.Text); // the label that starts with it comes first
            Assert.True(first.Target is CheckBox, first.Target.GetType().Name);
            Assert.Contains(SettingsView.SearchHitClass, first.Target.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NoHit_Text_EscClears()
    {
        var view = Show();
        var window = (Window)view.Parent!;
        try
        {
            Type(view, "zzzqqq");
            Assert.True(view.FindControl<TextBlock>("SettingsSearchNone")!.IsVisible);
            Assert.False(view.FindControl<ListBox>("SettingsSearchHits")!.IsVisible);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", view.FindControl<TextBox>("SettingsSearchBox")!.Text);
            Assert.False(view.FindControl<TextBlock>("SettingsSearchNone")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Index_EnglishUi_TextsInEnglish() => TestCulture.With("en-US", () =>
    {
        var view = Show();
        var window = (Window)view.Parent!;
        try
        {
            Assert.Contains(new NavSearch(SettingsPages.Sections(view)).Find("noise"), hit => hit.Text == Strings.Ui_NoiseSuppression && hit.PageName == "Audio");
        }
        finally
        {
            window.Close();
        }
        return 0;
    });
}
