using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Controls;

/// <summary>Package 153: the bar shows only while something is changed, saving or refused.</summary>
[Collection(MotionCollection.Name)]
public sealed class UnsavedBarTests
{
    static Button Find(UnsavedBar bar, string name) => bar.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    static void WithBar(Action<UnsavedBar> check)
    {
        var bar = new UnsavedBar();
        var window = new Window { Width = 600, Height = 200, Content = bar };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            check(bar);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Bar_NoChangesThenChange_HiddenThenShown() => WithBar(bar =>
    {
        Assert.False(bar.IsVisible);
        bar.HasChanges = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsVisible);
        Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == Strings.Unsaved_Text);
        Assert.True(Find(bar, "UnsavedSave").IsEnabled && Find(bar, "UnsavedReset").IsEnabled);
    });

    [AvaloniaFact]
    public void Bar_Error_TextShownSaveOff() => WithBar(bar =>
    {
        bar.Error = "Zwei Aktionen auf einer Taste";
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsVisible);
        Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "Zwei Aktionen auf einer Taste" && textBlock.Classes.Contains("danger"));
        Assert.False(Find(bar, "UnsavedSave").IsEnabled);
    });

    [AvaloniaFact]
    public void Bar_Saving_SpinnerShownButtonsOff() => WithBar(bar =>
    {
        bar.IsSaving = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsVisible);
        Assert.Contains(Find(bar, "UnsavedSave").GetVisualDescendants().OfType<BusySpinner>(), spinner => spinner.IsEffectivelyVisible);
        Assert.False(Find(bar, "UnsavedReset").IsEnabled);
    });

    [AvaloniaFact]
    public void Attention_ShownBar_LitUntilHidden() => WithBar(bar =>
    {
        bar.Attention(); // hidden: nothing to point at
        Assert.DoesNotContain(UnsavedBar.AttentionClass, bar.Classes);
        bar.HasChanges = true;
        bar.Attention();
        Assert.Contains(UnsavedBar.AttentionClass, bar.Classes);
        bar.HasChanges = false;
        Assert.DoesNotContain(UnsavedBar.AttentionClass, bar.Classes);
    });
}
