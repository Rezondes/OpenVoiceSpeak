namespace OVS.Client.Models;

/// <summary>An entry of a combo box: the value and its text.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
