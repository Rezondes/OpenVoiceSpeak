using Avalonia.Controls;

namespace OVS.Client.Models;

/// <summary>Package 158: one hit of the settings search: the text found, the page it is on and the control that shows it.</summary>
/// <param name="Text">The text as the page shows it.</param>
/// <param name="Page">The page of the side navigation it is on.</param>
/// <param name="Target">The control that shows it, scrolled into view and lit when chosen.</param>
internal sealed record NavSearchHit(string Text, TabItem Page, Control Target)
{
    /// <summary>The page's name, shown under the hit.</summary>
    public string PageName => Page.Header as string ?? "";
}
