using System.Reflection;
using OVS.Client.Controls;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Dialogs;

/// <summary>
/// Every dialog has the same shape: an internal static class with one static <c>ShowAsync</c> on the overlay that returns a
/// task, built on <c>DialogFrame</c>. A dialog is a type of the dialogs namespace whose name ends in "Dialog"
/// (<see cref="DialogCaseTable.FindDialogTypes"/>). Not dialogs, and none of them ends so: <c>DialogFrame</c> (the card every
/// dialog is shown in), <c>DialogControls</c> (what the dialogs are built of), <c>DialogOptions</c> (the frame's options),
/// <c>DialogCallbacks</c> (the delegates the view models call) and <c>OverlayDialogs</c> (which makes them).
/// </summary>
public sealed class DialogShapeTests
{
    [Fact]
    public void Dialogs_EveryDialogType_StaticShowAsyncOnOverlayReturningTask()
    {
        var dialogs = DialogCaseTable.FindDialogTypes();
        // a new dialog also needs its case in DialogCaseTable (DialogButtonsTests); the channel dialog is Views/ChannelDialogView
        Assert.True(dialogs.Count == 16, $"{dialogs.Count} dialog types: update this count and the cases of DialogCaseTable (DialogButtonsTests)");
        var problems = new List<string>();
        foreach (var type in dialogs)
        {
            if (type.IsPublic || !(type.IsAbstract && type.IsSealed)) problems.Add($"{type.Name}: not an internal static class");
            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !m.Name.Contains('<')).ToList();
            if (methods.Count != 1 || methods[0].Name != "ShowAsync") problems.Add($"{type.Name}: methods {string.Join(", ", methods.Select(m => m.Name))}");
            else
            {
                var show = methods[0];
                if (!(show.IsAssembly || show.IsPublic)) problems.Add($"{type.Name}: ShowAsync is private");
                if (show.GetParameters().FirstOrDefault()?.ParameterType != typeof(OverlayHost)) problems.Add($"{type.Name}: first parameter not OverlayHost");
                if (!typeof(Task).IsAssignableFrom(show.ReturnType)) problems.Add($"{type.Name}: returns {show.ReturnType.Name}");
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void Dialogs_EveryDialogSource_ShowsThroughTheFrame()
    {
        var folder = Path.Combine(RepoPaths.ClientSource, "Dialogs");
        var withoutFrame = DialogCaseTable.FindDialogTypes()
            .Where(t => !File.ReadAllText(Path.Combine(folder, t.Name + ".cs")).Contains("DialogFrame.ShowAsync(", StringComparison.Ordinal))
            .Select(t => t.Name);
        Assert.Empty(withoutFrame);
    }
}
