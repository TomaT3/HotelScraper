using Microsoft.Extensions.Logging;

namespace HotelScraper.Api.Configuration;

public class ScraperOptions
{
    public const string Section = "Scraper";

    public string RapidApiKey { get; set; } = "";
    public string DatabaseUrl { get; set; } = "Data Source=data/hotel_prices.db";
    public int DatesPerRun { get; set; } = 15;
    public int FetchHour { get; set; } = 3;
    public string SearchCities { get; set; } = "Stuttgart";

    // Optional fallback for the initial admin seed (primary source: ADMIN_EMAIL / ADMIN_PASSWORD env vars)
    public string AdminEmail { get; set; } = "";
    public string AdminPassword { get; set; } = "";


    public List<string> CityList
    {
        get
        {
            var rawCities = SearchCities
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c.Length > 0)
                .ToList();

            var cities = rawCities.Count > 0 ? rawCities : ["Stuttgart"];

            return cities;
        }
    }
}
