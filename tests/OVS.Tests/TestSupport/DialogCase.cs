using OVS.Client.Controls;

namespace OVS.Tests.TestSupport;

/// <summary>
/// One way to open a dialog and what it must show: its answer buttons in order (read when asked, in the current UI
/// culture), whether the default one is red, and what Enter gives (null: not checked, a form whose answer needs input).
/// </summary>
/// <param name="Open">Opens the dialog; the task tells whether it was accepted.</param>
sealed record DialogCase(string Name, Type Type, Func<OverlayHost, Task<bool>> Open, Func<string[]> Buttons, bool Red, bool? EnterAccepts);
