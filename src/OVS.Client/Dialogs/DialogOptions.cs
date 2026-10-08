using OVS.Client.ViewModels;

namespace OVS.Client.Dialogs;

/// <summary>How the frame of a dialog answers: its buttons, what Enter does, its colour and whether it sends.</summary>
/// <param name="OkIsDefault">The default button decides what Enter does; a risky choice (changed certificate) defaults to cancel.</param>
/// <param name="Submit">
/// Package 98: sends the answer and returns its waiting mark. The card stays open with a busy primary button until the
/// mark ends: confirmed it closes, refused or unanswered it shows the error and can be sent again.
/// </param>
/// <param name="ThirdAnswer">Package 114: a third answer between cancel and the default one.</param>
/// <param name="WithCancel">Package 133: false for a card that only informs.</param>
internal sealed record DialogOptions<T>(string OkText = "OK", bool OkIsDefault = true, DialogKind Kind = DialogKind.Normal, string? CancelText = null,
    Func<T, PendingViewModel>? Submit = null, (string Text, T Value)? ThirdAnswer = null, bool WithCancel = true) where T : class;
