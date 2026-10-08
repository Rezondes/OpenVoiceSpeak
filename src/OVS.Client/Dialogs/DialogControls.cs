using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OVS.Client.Localization;

namespace OVS.Client.Dialogs;

/// <summary>The controls the dialogs are built of: icons, texts, labelled fields, stacks.</summary>
internal static class DialogControls
{
    internal static T DockAt<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    internal static PathIcon CreateIcon(string key, string style)
    {
        var data = Application.Current!.FindResource("Icon." + key) as Geometry ?? throw new KeyNotFoundException($"Icon '{key}' is missing in Styles/Icons.axaml");
        var icon = new PathIcon { Data = data, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Classes.Add(style);
        return icon;
    }

    internal static TextBlock CreateText(string text, string? style = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        block.Classes.Add(style ?? "body");
        return block;
    }

    /// <summary>A visible label above its field, never a placeholder alone.</summary>
    internal static StackPanel CreateField(string label, Control input, string? hint = null)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(CreateText(label, "label"));
        panel.Children.Add(input);
        if (hint is not null) panel.Children.Add(CreateText(hint, "caption"));
        return panel;
    }

    internal static StackPanel CreateStack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.AddRange(children);
        return panel;
    }

    /// <summary>"Passwort speichern", only enabled (and only checked) while <paramref name="password"/> holds a password.</summary>
    internal static CheckBox CreateSavePasswordBox(TextBox password, bool isChecked)
    {
        var box = new CheckBox { Content = Strings.Dlg_SavePassword, IsChecked = isChecked };
        void Update()
        {
            box.IsEnabled = !string.IsNullOrEmpty(password.Text);
            if (!box.IsEnabled) box.IsChecked = false;
        }
        password.TextChanged += (_, _) => Update();
        Update();
        return box;
    }
}
