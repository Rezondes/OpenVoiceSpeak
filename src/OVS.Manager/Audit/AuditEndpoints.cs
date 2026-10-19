using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace OVS.Manager.Audit;

/// <summary>Package M5 (AC3): <c>GET /audit/export</c> gives the lines of the audit page's filter as CSV.</summary>
internal static class AuditEndpoints
{
    /// <summary>Adds the endpoint to <paramref name="app"/>.</summary>
    public static void Map(WebApplication app) =>
        app.MapGet("/audit/export", async (HttpContext context, AuditLog audit, TimeProvider time, string? user, string? server, string? action, string? from, string? to) =>
        {
            var lines = await audit.QueryAsync(Filter(user, server, action, from, to, time.LocalTimeZone), AuditScope.For(context.User), context.RequestAborted);
            return Results.Text(AuditCsv.Format(lines, time.LocalTimeZone), "text/csv", System.Text.Encoding.UTF8);
        });

    /// <summary>The filter of the page's fields; the dates are local days (yyyy-MM-dd), "to" includes its day.</summary>
    public static AuditFilter Filter(string? user, string? server, string? action, string? from, string? to, TimeZoneInfo zone) =>
        new(Blank(user), Blank(server), Blank(action), Day(from, zone), Day(to, zone)?.AddDays(1));

    static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static DateTime? Day(string? value, TimeZoneInfo zone) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone)
            : null;
}
