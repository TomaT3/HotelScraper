using HotelScraper.Api.Services;
using Xunit;

namespace HotelScraper.Tests;

public class DistanceParserTests
{
    [Fact]
    public void MilesFromCentre_ShouldConvertToKm()
    {
        var result = DistanceParser.ParseDistanceFromLabel("11 miles from centre");
        Assert.NotNull(result);
        Assert.True(Math.Abs(17.70 - result!.Value) < 0.01, $"Expected ~17.70 but got {result.Value}");
    }

    [Fact]
    public void MilesFromCentreWithDecimal_ShouldConvertToKm()
    {
        var result = DistanceParser.ParseDistanceFromLabel("4.1 miles from centre");
        Assert.NotNull(result);
        Assert.True(Math.Abs(6.60 - result!.Value) < 0.01, $"Expected ~6.60 but got {result.Value}");
    }

    [Fact]
    public void InCityCentre_ShouldReturnZero()
    {
        var result = DistanceParser.ParseDistanceFromLabel("In city centre");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void InCityCentre_CaseInsensitive()
    {
        var result = DistanceParser.ParseDistanceFromLabel("IN CITY CENTRE");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void KmFromCentre_ShouldParseDirectly()
    {
        var result = DistanceParser.ParseDistanceFromLabel("5 km from centre");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void KmFromCentre_NoSpaceBeforeKm()
    {
        var result = DistanceParser.ParseDistanceFromLabel("12.5km from centre");
        Assert.Equal(12.5, result);
    }

    [Fact]
    public void EmptyString_ShouldReturnNull()
    {
        var result = DistanceParser.ParseDistanceFromLabel("");
        Assert.Null(result);
    }

    [Fact]
    public void NullInput_ShouldReturnNull()
    {
        var result = DistanceParser.ParseDistanceFromLabel(null);
        Assert.Null(result);
    }

    [Fact]
    public void GarbageString_ShouldReturnNull()
    {
        var result = DistanceParser.ParseDistanceFromLabel("some random text");
        Assert.Null(result);
    }

    [Fact]
    public void MilesMissingNumber_ShouldReturnNull()
    {
        var result = DistanceParser.ParseDistanceFromLabel("miles from centre");
        Assert.Null(result);
    }

    [Fact]
    public void MilesFromCityCenter_AmericanSpelling_ShouldConvertToKm()
    {
        // "2.2 miles from city center" (American spelling + "city" qualifier)
        var result = DistanceParser.ParseDistanceFromLabel("2.2 miles from city center");
        Assert.NotNull(result);
        Assert.True(Math.Abs(3.54 - result!.Value) < 0.01, $"Expected ~3.54 but got {result.Value}");
    }

    [Fact]
    public void KmFromTheCityCentre_ShouldParseDirectly()
    {
        // "5 km from the city centre" (British spelling + "the city" qualifier)
        var result = DistanceParser.ParseDistanceFromLabel("5 km from the city centre");
        Assert.NotNull(result);
        Assert.True(Math.Abs(5.0 - result!.Value) < 0.01);
    }

    [Fact]
    public void KmFromCityCenter_ShouldParseDirectly()
    {
        // "3 km from city center" (American spelling + "city" qualifier)
        var result = DistanceParser.ParseDistanceFromLabel("3 km from city center");
        Assert.NotNull(result);
        Assert.True(Math.Abs(3.0 - result!.Value) < 0.01);
    }

    [Fact]
    public void InCityCenter_American_ShouldReturnZero()
    {
        var result = DistanceParser.ParseDistanceFromLabel("In city center");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void InTheCityCentre_ShouldReturnZero()
    {
        var result = DistanceParser.ParseDistanceFromLabel("In the city centre");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void RealisticLabel_MumbaiStyle_WithAreaAndDistance()
    {
        // Simulates real API label format: area name followed by distance
        var label = "Colaba • 11 miles from centre\nTravel Sustainable\nFree cancellation";
        var result = DistanceParser.ParseDistanceFromLabel(label);
        Assert.NotNull(result);
        Assert.True(Math.Abs(17.70 - result!.Value) < 0.01, $"Expected ~17.70 but got {result.Value}");
    }

    [Fact]
    public void RealisticLabel_GermanCityStyle()
    {
        // Simulates possible German city label format
        var label = "Hauptbahnhof • 2.5 km from the city centre\nTravel Sustainable\nFree cancellation";
        var result = DistanceParser.ParseDistanceFromLabel(label);
        Assert.NotNull(result);
        Assert.True(Math.Abs(2.5 - result!.Value) < 0.01);
    }

    [Fact]
    public void LabelWithUnicodeFormattingChars()
    {
        // Unicode directional formatting chars (U+200E, U+202C) as in real API responses
        var label = "\u200eMitte\u202c \u2022 \u200e1.5 km from city centre\u202c";
        var result = DistanceParser.ParseDistanceFromLabel(label);
        Assert.NotNull(result);
        Assert.True(Math.Abs(1.5 - result!.Value) < 0.01);
    }

    [Fact]
    public void InDowntown_ShouldReturnZero()
    {
        // "In downtown" — used for German cities like Stuttgart
        var result = DistanceParser.ParseDistanceFromLabel("In downtown");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void KmFromDowntown_ShouldParseDirectly()
    {
        var result = DistanceParser.ParseDistanceFromLabel("3 km from downtown");
        Assert.NotNull(result);
        Assert.True(Math.Abs(3.0 - result!.Value) < 0.01);
    }

    [Fact]
    public void MilesFromDowntown_ShouldConvertToKm()
    {
        var result = DistanceParser.ParseDistanceFromLabel("5 miles from downtown");
        Assert.NotNull(result);
        Assert.True(Math.Abs(8.05 - result!.Value) < 0.01, $"Expected ~8.05 but got {result.Value}");
    }

    [Fact]
    public void RealisticLabel_StuttgartStyle_InDowntown()
    {
        // Simulates real Stuttgart API label: hotel name, rating, "In downtown", room details
        var label = "EmiLu Design Hotel.\n4 out of 5 for accommodation quality.\n9.0 Wonderful 2304 reviews.\n\u200eIn downtown\u202c.\n Hotel room : 1 bed.\n120 EUR.";
        var result = DistanceParser.ParseDistanceFromLabel(label);
        Assert.Equal(0.0, result);
    }

    // ── German language tests ──

    [Fact]
    public void German_KmVomZentrumEntfernt_ShouldParse()
    {
        var result = DistanceParser.ParseDistanceFromLabel("3,2 km vom Zentrum entfernt");
        Assert.NotNull(result);
        Assert.True(Math.Abs(3.2 - result!.Value) < 0.01, $"Expected ~3.2 but got {result.Value}");
    }

    [Fact]
    public void German_KmVomStadtzentrumEntfernt_ShouldParse()
    {
        var result = DistanceParser.ParseDistanceFromLabel("5 km vom Stadtzentrum entfernt");
        Assert.NotNull(result);
        Assert.True(Math.Abs(5.0 - result!.Value) < 0.01);
    }

    [Fact]
    public void German_ImZentrum_ShouldReturnZero()
    {
        var result = DistanceParser.ParseDistanceFromLabel("Im Zentrum");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void German_ImStadtzentrum_ShouldReturnZero()
    {
        var result = DistanceParser.ParseDistanceFromLabel("Im Stadtzentrum");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void German_RealisticLabel_WithCommaDecimal()
    {
        // Real German accessibilityLabel as returned by Booking API with language=de
        var label = "Forrest Hostels. Diese Unterkunft ist Teil unseres Preferred-Programms. 8,6 Fabelhaft 800 Bewertungen. \u200eBandra\u202c • \u200e3,2 km vom Zentrum entfernt\u202c \u200e2,5 km vom Strand entfernt\u202c. Bett im Schlafsaal : 1 Bett. 12 EUR.";
        var result = DistanceParser.ParseDistanceFromLabel(label);
        Assert.NotNull(result);
        Assert.True(Math.Abs(3.2 - result!.Value) < 0.01, $"Expected ~3.2 but got {result.Value}");
    }

    [Fact]
    public void German_RealisticLabel_ImZentrum()
    {
        var label = "EmiLu Design Hotel.\n9,0 Wunderbar 2304 Bewertungen.\n\u200eIm Zentrum\u202c.\nHotelzimmer : 1 Bett.\n120 EUR.";
        var result = DistanceParser.ParseDistanceFromLabel(label);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void German_WholeNumber_ShouldParse()
    {
        var result = DistanceParser.ParseDistanceFromLabel("12 km vom Zentrum entfernt");
        Assert.NotNull(result);
        Assert.True(Math.Abs(12.0 - result!.Value) < 0.01);
    }
}
