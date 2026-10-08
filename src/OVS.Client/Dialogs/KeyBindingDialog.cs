using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OVS.Client.Controls;
using OVS.Client.Input;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;
using KeyBinding = OVS.Client.Input.KeyBinding;

namespace OVS.Client.Dialogs;

/// <summary>Package 41: pick the action, then press the key or combination.</summary>
internal static class KeyBindingDialog
{
    internal static Task<KeyBinding?> ShowAsync(OverlayHost overlay, KeyBinding? current, Func<KeyAction, Task<KeyChord?>> capture)
    {
        var action = new ComboBox
        {
            ItemsSource = KeyActionTraits.All.Select(KeyActionLabels.Localize).ToList(),
            SelectedIndex = current is null ? -1 : KeyActionTraits.All.ToList().IndexOf(current.Action),
            PlaceholderText = Strings.Dlg_ChooseAction,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        KeyChord? chord = current?.Chord is { Key: > 0 } known ? known : null; // Package 59: key 0 = only the action is chosen
        var keyText = new TextBlock { Text = chord?.Name ?? Strings.Dlg_NoKeyYet, VerticalAlignment = VerticalAlignment.Center };
        var keycap = new Border { Child = keyText, VerticalAlignment = VerticalAlignment.Center };
        keycap.Classes.Add("keycap");
        var assign = new Button { Content = Strings.Dlg_SetKeyButton, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(assign, Strings.Dlg_SetKey);
        var hint = CreateText("", "accent");
        hint.IsVisible = false;
        var error = CreateText("", "danger");
        error.IsVisible = false;
        assign.Click += async (_, _) =>
        {
            assign.IsEnabled = false;
            hint.Text = Strings.Dlg_PressKey;
            hint.IsVisible = true;
            try
            {
                if (await capture(action.SelectedIndex >= 0 ? KeyActionTraits.All[action.SelectedIndex] : KeyAction.PushToTalk) is { } captured)
                {
                    chord = captured;
                    keyText.Text = captured.Name;
                }
            }
            finally
            {
                assign.IsEnabled = true;
                hint.IsVisible = false;
            }
        };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { keycap, assign } };
        var body = CreateStack(CreateField(Strings.Dlg_Action, action), CreateField(Strings.Dlg_Key, keyRow, Strings.Dlg_KeyHint), hint, error);
        return DialogFrame.ShowAsync(overlay, current is null ? Strings.Dlg_AddKeyAction : Strings.Dlg_ChangeKeyAction, "Keyboard", body, () =>
        {
            string? problem = action.SelectedIndex < 0 ? Strings.Dlg_ActionMissing : chord is null ? Strings.Dlg_KeyMissing : null;
            error.Text = problem ?? "";
            error.IsVisible = problem is not null;
            return problem is null ? new KeyBinding(KeyActionTraits.All[action.SelectedIndex], chord!) : null;
        }, new(Strings.Dlg_Save));
    }
}
