using Avalonia.Data.Converters;

namespace OVS.Client.Converters;

/// <summary>Comparisons of a bound number, for visibility and the signal icon.</summary>
public static class NumberConverters
{
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);

    /// <summary>Package 133: the signal icon; bar n (the parameter) is lit from n bars on, the colour goes by the number.</summary>
    public static readonly IValueConverter AtLeast = new FuncValueConverter<int, string, bool>((n, min) => n >= int.Parse(min!));
    public static readonly IValueConverter Is = new FuncValueConverter<int, string, bool>((n, value) => n == int.Parse(value!));
}
