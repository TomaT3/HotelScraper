using HotelScraper.Api.Configuration;
using Quartz;

namespace HotelScraper.Api.Services;

public class DailyFetchJob : IJob
{
    private readonly PriceFetcherService _priceFetcher;
    private readonly ScraperOptions _options;
    private readonly ILogger<DailyFetchJob> _logger;

    public DailyFetchJob(
        PriceFetcherService priceFetcher,
        Microsoft.Extensions.Options.IOptions<ScraperOptions> options,
        ILogger<DailyFetchJob> logger)
    {
        _priceFetcher = priceFetcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cities = _options.CityList;
        _logger.LogInformation(
            "Scheduled fetch starting for {Count} cities (max {MaxDates} dates each)...",
            cities.Count, _options.DatesPerRun
        );

        try
        {
            var result = await _priceFetcher.FetchAllCitiesAsync(maxDates: _options.DatesPerRun);
            _logger.LogInformation(
                "Scheduled fetch complete: {Dates} dates, {Prices} prices, {Errors} errors",
                result.DatesFetched, result.PricesSaved, result.Errors.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled fetch failed");
        }
    }
}
