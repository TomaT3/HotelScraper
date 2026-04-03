import logging

from apscheduler.schedulers.asyncio import AsyncIOScheduler
from apscheduler.triggers.cron import CronTrigger

from app.config import settings
from app.services.price_fetcher import fetch_all_cities

logger = logging.getLogger(__name__)

scheduler = AsyncIOScheduler()


async def scheduled_fetch():
    """Job that runs daily to fetch new hotel prices for all configured cities."""
    cities = settings.city_list
    logger.info("Scheduled fetch starting for %d cities (max %d dates each)...", len(cities), settings.dates_per_run)
    try:
        result = await fetch_all_cities(max_dates=settings.dates_per_run)
        logger.info(
            "Scheduled fetch complete: %d dates, %d prices, %d errors",
            result["dates_fetched"],
            result["prices_saved"],
            len(result["errors"]),
        )
    except Exception as e:
        logger.error("Scheduled fetch failed: %s", e)


def start_scheduler():
    """Start the APScheduler with a daily cron job."""
    if not settings.rapidapi_key or settings.rapidapi_key == "your_rapidapi_key_here":
        logger.warning("RAPIDAPI_KEY not set — scheduler will NOT start. Set it in .env and restart.")
        return

    scheduler.add_job(
        scheduled_fetch,
        trigger=CronTrigger(hour=settings.fetch_hour, minute=0),
        id="daily_fetch",
        replace_existing=True,
        name="Daily hotel price fetch",
    )
    scheduler.start()
    logger.info("Scheduler started — daily fetch at %02d:00", settings.fetch_hour)


def stop_scheduler():
    if scheduler.running:
        scheduler.shutdown(wait=False)
        logger.info("Scheduler stopped")
