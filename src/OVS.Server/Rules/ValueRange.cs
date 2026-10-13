namespace OVS.Server.Rules;

/// <summary>An inclusive range of whole numbers a server limit may take.</summary>
/// <param name="Min">The smallest allowed value.</param>
/// <param name="Max">The largest allowed value.</param>
public readonly record struct ValueRange(int Min, int Max)
{
    /// <summary>Whether <paramref name="value"/> lies in the range, both ends included.</summary>
    public bool Contains(int value) => value >= Min && value <= Max;
}
