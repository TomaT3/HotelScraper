namespace HotelScraper.Api.Configuration;

public class ScraperOptions
{
    public const string Section = "Scraper";

    public string RapidApiKey { get; set; } = "";
    public string DatabaseUrl { get; set; } = "Data Source=data/hotel_prices.db";
    public int DatesPerRun { get; set; } = 15;
    public int FetchHour { get; set; } = 3;
    public string SearchCities { get; set; } = "Stuttgart";
    public string SearchCity { get; set; } = "";

    public List<string> CityList
    {
        get
        {
            var cities = SearchCities
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c.Length > 0)
                .ToList();

            if (cities.Count == 0 && !string.IsNullOrWhiteSpace(SearchCity))
                cities = [SearchCity.Trim()];

            return cities.Count > 0 ? cities : ["Stuttgart"];
        }
    }
}
