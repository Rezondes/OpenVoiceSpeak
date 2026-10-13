namespace OVS.Client.Models;

/// <summary>An entry of a combo box: the value and its text.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    /// <summary>The label, as a list shows it.</summary>
    public override string ToString() => Label;
}
