from datetime import date, datetime

from fastapi import APIRouter, Depends, Query
from sqlalchemy import select, func
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.models import Hotel, Price, Setting
from app.schemas import HotelPrices, PricePoint, StatusOut, FetchResult
from app.services.price_fetcher import fetch_prices_for_dates
from app.services.scheduler import scheduler
from app.config import settings

router = APIRouter(prefix="/api", tags=["prices"])


@router.get("/prices", response_model=list[HotelPrices])
async def get_prices(
    hotel_ids: str | None = Query(None, description="Comma-separated hotel IDs"),
    date_from: date | None = Query(None, alias="from"),
    date_to: date | None = Query(None, alias="to"),
    db: AsyncSession = Depends(get_db),
):
    # Build hotel query
    hotel_query = select(Hotel)
    if hotel_ids:
        ids = [int(x.strip()) for x in hotel_ids.split(",") if x.strip().isdigit()]
        hotel_query = hotel_query.where(Hotel.id.in_(ids))

    hotels_result = await db.execute(hotel_query.order_by(Hotel.name))
    hotels = hotels_result.scalars().all()

    result = []
    for hotel in hotels:
        price_query = select(Price).where(Price.hotel_id == hotel.id)
        if date_from:
            price_query = price_query.where(Price.date >= date_from)
        if date_to:
            price_query = price_query.where(Price.date <= date_to)
        price_query = price_query.order_by(Price.date)

        prices_result = await db.execute(price_query)
        prices = prices_result.scalars().all()

        if prices:
            result.append(HotelPrices(
                hotel_id=hotel.id,
                hotel_name=hotel.name,
                stars=hotel.stars,
                prices=[PricePoint(date=p.date, price_eur=p.price_eur) for p in prices],
            ))

    return result


@router.get("/status", response_model=StatusOut)
async def get_status(db: AsyncSession = Depends(get_db)):
    total_hotels = (await db.execute(select(func.count(Hotel.id)))).scalar() or 0
    active_hotels = (await db.execute(
        select(func.count(Hotel.id)).where(Hotel.active == True)
    )).scalar() or 0
    total_prices = (await db.execute(select(func.count(Price.id)))).scalar() or 0

    today = date.today()
    dates_covered = (await db.execute(
        select(func.count(func.distinct(Price.date))).where(Price.date >= today)
    )).scalar() or 0
    dates_total = 365

    # Last fetch time
    last_fetch_result = await db.execute(select(Setting).where(Setting.key == "last_fetch"))
    last_fetch_setting = last_fetch_result.scalar_one_or_none()
    last_fetch = None
    if last_fetch_setting:
        try:
            last_fetch = datetime.fromisoformat(last_fetch_setting.value)
        except ValueError:
            pass

    # Next scheduled run
    next_run = None
    if scheduler.running:
        job = scheduler.get_job("daily_fetch")
        if job and job.next_run_time:
            next_run = job.next_run_time.replace(tzinfo=None)

    return StatusOut(
        total_hotels=total_hotels,
        active_hotels=active_hotels,
        total_prices=total_prices,
        dates_covered=dates_covered,
        dates_total=dates_total,
        coverage_pct=round((dates_covered / dates_total) * 100, 1) if dates_total > 0 else 0,
        last_fetch=last_fetch,
        scheduler_running=scheduler.running,
        next_run=next_run,
    )


@router.post("/fetch", response_model=FetchResult)
async def trigger_fetch(max_dates: int = Query(default=None)):
    """Manually trigger a price fetch for unfetched dates."""
    result = await fetch_prices_for_dates(max_dates=max_dates or settings.dates_per_run)
    return FetchResult(**result)
