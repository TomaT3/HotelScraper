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
}
