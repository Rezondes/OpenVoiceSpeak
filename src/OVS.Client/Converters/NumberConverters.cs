using System.Globalization;
using Avalonia.Data.Converters;

namespace OVS.Client.Converters;

/// <summary>Comparisons of a bound number, for visibility and the signal icon.</summary>
public static class NumberConverters
{
    /// <summary>True for 0.</summary>
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(count => count == 0);

    /// <summary>Package 133: the signal icon; bar n (the parameter) is lit from n bars on, the colour goes by the number.</summary>
    public static readonly IValueConverter AtLeast = new FuncValueConverter<int, string, bool>((count, min) => count >= int.Parse(min!, CultureInfo.CurrentCulture));
    /// <summary>True when the number equals the parameter.</summary>
    public static readonly IValueConverter Is = new FuncValueConverter<int, string, bool>((count, value) => count == int.Parse(value!, CultureInfo.CurrentCulture));
}
