using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using OVS.Shared.Protocol;

namespace OVS.Server;

/// <summary>
/// Package 83: what System.Text.Json lets through without complaint, checked once per request before its handler:
/// null in a non-nullable field (also a missing one), an enum value outside the enum, a null entry in a list. Works from
/// the nullable annotations of the request records, so new requests are covered without extra code.
/// </summary>
static class RequestShape
{
    static readonly ConcurrentDictionary<Type, Func<object, string?>[]> checks = new();

    /// <returns>A German reason, or null when the request is well-formed.</returns>
    public static string? Problem(Request request)
    {
        foreach (var check in checks.GetOrAdd(request.GetType(), Build))
            if (check(request) is { } problem) return problem;
        return null;
    }

    static Func<object, string?>[] Build(Type type)
    {
        var nullability = new NullabilityInfoContext(); // not thread-safe, so one per build
        var result = new List<Func<object, string?>>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var info = nullability.Create(p);
            var valueType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!p.PropertyType.IsValueType && info.ReadState == NullabilityState.NotNull)
                result.Add(o => p.GetValue(o) is null ? $"{p.Name} fehlt" : null);
            // Flags enums (Permission) are masked by their handlers; any other value must be a named one
            if (valueType.IsEnum && !valueType.IsDefined(typeof(FlagsAttribute), false))
                result.Add(o => p.GetValue(o) is { } v && !Enum.IsDefined(valueType, v) ? $"{p.Name} ungültig" : null);
            if (typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string)
                && info.GenericTypeArguments is [{ ReadState: NullabilityState.NotNull, Type.IsValueType: false }])
                result.Add(o => p.GetValue(o) is IEnumerable items && items.Cast<object?>().Any(x => x is null) ? $"{p.Name} enthält einen leeren Eintrag" : null);
        }
        return result.ToArray();
    }
}
