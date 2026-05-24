using System.Globalization;
using System.Text.RegularExpressions;

namespace HotelScraper.Api.Services;

public static partial class DistanceParser
{
    [GeneratedRegex(@"([\d.]+)\s*miles?\s*from\s*(the\s+)?(city\s+)?(cent(er|re)|downtown)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MilesFromCentreRegex();

    [GeneratedRegex(@"([\d.]+)\s*km\s*from\s*(the\s+)?(city\s+)?(cent(er|re)|downtown)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex KmFromCentreRegex();

    // German: "3,2 km vom Zentrum entfernt", "5 km vom Stadtzentrum entfernt"
    [GeneratedRegex(@"([\d,]+)\s*km\s+vom\s+(Stadt)?[Zz]entrum\s+entfernt", RegexOptions.IgnoreCase, "de-DE")]
    private static partial Regex KmFromCentreRegexDe();

    /// <summary>
    /// Parse distance from city centre from an accessibilityLabel string.
    /// Handles: "11 miles from centre", "4.1 miles from city center",
    /// "5 km from the city centre", "3 km from downtown",
    /// "In city centre" (0), "In city center" (0), "In downtown" (0).
    /// Returns distance in kilometres.
    /// </summary>
    public static double? ParseDistanceFromLabel(string? accessibilityLabel)
    {
        if (string.IsNullOrWhiteSpace(accessibilityLabel))
            return null;

        // In city centre / In city center / In downtown → distance 0
        // German: "Im Zentrum", "Im Stadtzentrum"
        if (Regex.IsMatch(accessibilityLabel, @"\b[iI](?:n|m)\s+(the\s+)?(city\s+)?(cent(?:er|re)|downtown|(?:Stadt)?[Zz]entrum)\b", RegexOptions.IgnoreCase))
            return 0.0;

        // "11 miles from centre"
        var milesMatch = MilesFromCentreRegex().Match(accessibilityLabel);
        if (milesMatch.Success && double.TryParse(milesMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var miles))
            return Math.Round(miles * 1.60934, 2);

        // "5 km from centre"
        var kmMatch = KmFromCentreRegex().Match(accessibilityLabel);
        if (kmMatch.Success && double.TryParse(kmMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var km))
            return Math.Round(km, 2);

        // German: "3,2 km vom Zentrum entfernt" (comma decimal separator)
        var kmMatchDe = KmFromCentreRegexDe().Match(accessibilityLabel);
        if (kmMatchDe.Success && double.TryParse(kmMatchDe.Groups[1].Value, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out var kmDe))
            return Math.Round(kmDe, 2);

        return null;
    }
}
