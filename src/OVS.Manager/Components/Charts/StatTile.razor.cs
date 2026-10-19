using Microsoft.AspNetCore.Components;

namespace OVS.Manager.Components.Charts;

/// <summary>Package M22 (AC1): one figure with its label and unit.</summary>
sealed partial class StatTile
{
    /// <summary>The tile's name for the page (data-tile).</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = "";

    /// <summary>What the figure is.</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = "";

    /// <summary>The figure, formatted.</summary>
    [Parameter, EditorRequired]
    public string Value { get; set; } = "";

    /// <summary>Its unit.</summary>
    [Parameter]
    public string Unit { get; set; } = "";
}
