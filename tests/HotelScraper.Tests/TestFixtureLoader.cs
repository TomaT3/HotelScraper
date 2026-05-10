using System.Text.Json;

namespace HotelScraper.Tests;

public static class TestFixtureLoader
{
    private static readonly string DataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "backend", "tests", "data"
    );

    public static T LoadFixture<T>(string filename)
    {
        var path = Path.Combine(DataDir, filename);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }
}
