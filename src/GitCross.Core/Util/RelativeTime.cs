namespace GitCross.Core.Util;

public static class RelativeTime
{
    /// <summary>Short human text such as "just now", "5 min ago", "3 days ago"; older than ~a year falls back to a date.</summary>
    public static string Format(DateTimeOffset when, DateTimeOffset now)
    {
        var d = now - when;
        if (d < TimeSpan.Zero) { return when.ToString("yyyy-MM-dd"); }
        if (d < TimeSpan.FromMinutes(1)) { return "just now"; }
        if (d < TimeSpan.FromHours(1)) { return $"{(int)d.TotalMinutes} min ago"; }
        if (d < TimeSpan.FromDays(1)) { return $"{(int)d.TotalHours} h ago"; }
        if (d < TimeSpan.FromDays(30)) { return Plural((int)d.TotalDays, "day"); }
        if (d < TimeSpan.FromDays(365)) { return Plural((int)(d.TotalDays / 30), "month"); }
        return when.ToString("yyyy-MM-dd");
    }

    /// <summary>Up to two initials for an avatar badge.</summary>
    public static string Initials(string name)
    {
        var parts = name.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(parts[0][0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[^1][0])),
        };
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
}
