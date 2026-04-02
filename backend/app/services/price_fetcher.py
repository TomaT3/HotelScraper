import logging
from datetime import date, datetime, timedelta

from sqlalchemy import select, func
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.dialects.sqlite import insert as sqlite_upsert

from app.database import async_session
from app.models import Hotel, Price, Setting
from app.config import settings
from app.services import booking_api

logger = logging.getLogger(__name__)


async def get_dest_id(session: AsyncSession) -> str:
    """Get the destination ID from DB or fetch it from the API."""
    result = await session.execute(select(Setting).where(Setting.key == "dest_id"))
    setting = result.scalar_one_or_none()
    if setting:
        return setting.value

    location = await booking_api.search_location(settings.search_city)
    if not location:
        raise RuntimeError(f"Could not find destination for city: {settings.search_city}")

    session.add(Setting(key="dest_id", value=location["dest_id"]))
    session.add(Setting(key="dest_label", value=location["label"]))
    await session.commit()
    logger.info("Stored dest_id=%s for %s", location["dest_id"], location["label"])
    return location["dest_id"]


async def get_unfetched_dates(session: AsyncSession, max_dates: int) -> list[date]:
    """Find dates in the next 365 days that haven't been fetched yet.

    Prioritizes nearest dates first.
    """
    today = date.today()
    all_dates = {today + timedelta(days=i) for i in range(1, 366)}

    result = await session.execute(
        select(Price.date).where(Price.date >= today).distinct()
    )
    fetched_dates = {row[0] for row in result.all()}

    unfetched = sorted(all_dates - fetched_dates)
    return unfetched[:max_dates]


async def upsert_hotel(session: AsyncSession, hotel_data: dict) -> int:
    """Insert or update a hotel, returning its ID."""
    result = await session.execute(
        select(Hotel).where(Hotel.booking_id == hotel_data["booking_id"])
    )
    hotel = result.scalar_one_or_none()

    if hotel:
        hotel.name = hotel_data["name"]
        hotel.address = hotel_data.get("address")
        hotel.stars = hotel_data.get("stars")
        hotel.review_score = hotel_data.get("review_score")
        hotel.image_url = hotel_data.get("image_url")
        await session.flush()
        return hotel.id

    new_hotel = Hotel(
        booking_id=hotel_data["booking_id"],
        name=hotel_data["name"],
        address=hotel_data.get("address"),
        stars=hotel_data.get("stars"),
        review_score=hotel_data.get("review_score"),
        image_url=hotel_data.get("image_url"),
        active=True,
    )
    session.add(new_hotel)
    await session.flush()
    return new_hotel.id


async def save_price(session: AsyncSession, hotel_id: int, price_date: date, price_eur: float):
    """Insert or update a price for a hotel on a specific date."""
    stmt = sqlite_upsert(Price).values(
        hotel_id=hotel_id,
        date=price_date,
        price_eur=price_eur,
        fetched_at=datetime.utcnow(),
    )
    stmt = stmt.on_conflict_do_update(
        index_elements=["hotel_id", "date"],
        set_={"price_eur": price_eur, "fetched_at": datetime.utcnow()},
    )
    await session.execute(stmt)


async def fetch_prices_for_dates(dates: list[date] | None = None, max_dates: int | None = None) -> dict:
    """Fetch hotel prices for the given dates (or auto-select unfetched dates).

    Returns a summary dict with counts and errors.
    """
    errors: list[str] = []
    total_hotels = 0
    total_prices = 0

    async with async_session() as session:
        dest_id = await get_dest_id(session)

        if dates is None:
            dates = await get_unfetched_dates(session, max_dates or settings.dates_per_run)

        if not dates:
            logger.info("No unfetched dates remaining — all 365 days covered!")
            return {"dates_fetched": 0, "hotels_found": 0, "prices_saved": 0, "errors": []}

        logger.info("Fetching prices for %d dates: %s ... %s", len(dates), dates[0], dates[-1])

        for check_date in dates:
            checkout = check_date + timedelta(days=1)
            try:
                hotels = await booking_api.search_hotels(dest_id, check_date, checkout)
                total_hotels = max(total_hotels, len(hotels))

                for hotel_data in hotels:
                    hotel_id = await upsert_hotel(session, hotel_data)
                    await save_price(session, hotel_id, check_date, hotel_data["price_eur"])
                    total_prices += 1

                await session.commit()
                logger.info("Saved %d prices for %s", len(hotels), check_date.isoformat())
            except Exception as e:
                logger.error("Error fetching %s: %s", check_date.isoformat(), e)
                errors.append(f"{check_date.isoformat()}: {str(e)}")
                await session.rollback()

        # Update last_fetch timestamp
        async with async_session() as s2:
            result = await s2.execute(select(Setting).where(Setting.key == "last_fetch"))
            setting = result.scalar_one_or_none()
            now_str = datetime.utcnow().isoformat()
            if setting:
                setting.value = now_str
            else:
                s2.add(Setting(key="last_fetch", value=now_str))
            await s2.commit()

    return {
        "dates_fetched": len(dates),
        "hotels_found": total_hotels,
        "prices_saved": total_prices,
        "errors": errors,
    }
