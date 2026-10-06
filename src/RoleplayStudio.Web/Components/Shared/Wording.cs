using System.Globalization;

namespace RoleplayStudio.Web.Components.Shared;

public static class Wording
{
    public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>How long ago, as short as a list row allows: "12 min", "2 h", "Yesterday", "Mon", "3 Sep".</summary>
    public static string Ago(DateTimeOffset then, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var elapsed = now - then;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min";
        }

        var day = TimeZoneInfo.ConvertTime(then, time.LocalTimeZone).Date;
        var today = TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).Date;
        if (day == today)
        {
            return $"{(int)elapsed.TotalHours} h";
        }

        if (day == today.AddDays(-1))
        {
            return "Yesterday";
        }

        var format = day > today.AddDays(-7) ? "ddd" : day.Year == today.Year ? "d MMM" : "d MMM yyyy";
        return day.ToString(format, CultureInfo.InvariantCulture);
    }
}
