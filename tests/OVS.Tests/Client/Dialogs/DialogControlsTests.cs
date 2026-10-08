using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Dialogs;

namespace OVS.Tests.Client.Dialogs;

/// <summary>The controls the dialogs are built of.</summary>
public sealed class DialogControlsTests
{
    [AvaloniaTheory]
    [InlineData("", true, false, false)]
    [InlineData("geheim", true, true, true)]
    [InlineData("geheim", false, true, false)]
    public void CreateSavePasswordBox_InitialPassword_EnabledAndCheckedOnlyWithOne(string text, bool isChecked, bool enabled, bool checkedAfter)
    {
        var password = new TextBox { Text = text };
        var box = DialogControls.CreateSavePasswordBox(password, isChecked);
        Assert.Equal((enabled, checkedAfter), (box.IsEnabled, box.IsChecked == true));
    }

    [AvaloniaFact]
    public void CreateSavePasswordBox_PasswordEmptied_DisabledAndUnchecked()
    {
        var password = new TextBox { Text = "geheim" };
        var box = DialogControls.CreateSavePasswordBox(password, true);
        password.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((false, false), (box.IsEnabled, box.IsChecked == true));
    }
}
