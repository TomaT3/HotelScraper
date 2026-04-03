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


async def get_dest_id(session: AsyncSession, city: str) -> str:
    """Get the destination ID for a city from DB or fetch it from the API."""
    key = f"dest_id:{city}"
    result = await session.execute(select(Setting).where(Setting.key == key))
    setting = result.scalar_one_or_none()
    if setting:
        return setting.value

    # Fallback: check old key format (migration from single-city)
    if city == settings.city_list[0]:
        old_result = await session.execute(select(Setting).where(Setting.key == "dest_id"))
        old_setting = old_result.scalar_one_or_none()
        if old_setting:
            # Migrate old key to new format
            session.add(Setting(key=key, value=old_setting.value))
            old_label = await session.execute(select(Setting).where(Setting.key == "dest_label"))
            old_label_setting = old_label.scalar_one_or_none()
            if old_label_setting:
                session.add(Setting(key=f"dest_label:{city}", value=old_label_setting.value))
            await session.commit()
            return old_setting.value

    location = await booking_api.search_location(city)
    if not location:
        raise RuntimeError(f"Could not find destination for city: {city}")

    session.add(Setting(key=key, value=location["dest_id"]))
    session.add(Setting(key=f"dest_label:{city}", value=location["label"]))
    await session.commit()
    logger.info("Stored dest_id=%s for %s", location["dest_id"], location["label"])
    return location["dest_id"]


async def get_unfetched_dates(session: AsyncSession, city: str, max_dates: int) -> list[date]:
    """Find dates in the next 365 days that haven't been fetched yet for a specific city.

    Prioritizes nearest dates first.
    """
    today = date.today()
    all_dates = {today + timedelta(days=i) for i in range(1, 366)}

    # Only consider dates fetched for hotels in this city
    city_hotel_ids = select(Hotel.id).where(Hotel.city == city)
    result = await session.execute(
        select(Price.date).where(
            Price.date >= today,
            Price.hotel_id.in_(city_hotel_ids),
        ).distinct()
    )
    fetched_dates = {row[0] for row in result.all()}

    unfetched = sorted(all_dates - fetched_dates)
    return unfetched[:max_dates]


async def upsert_hotel(session: AsyncSession, hotel_data: dict, city: str) -> int:
    """Insert or update a hotel for a specific city, returning its ID."""
    result = await session.execute(
        select(Hotel).where(Hotel.booking_id == hotel_data["booking_id"], Hotel.city == city)
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
        city=city,
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


async def fetch_prices_for_dates(
    city: str,
    dates: list[date] | None = None,
    max_dates: int | None = None,
) -> dict:
    """Fetch hotel prices for a specific city for the given dates (or auto-select unfetched dates).

    Returns a summary dict with counts and errors.
    """
    errors: list[str] = []
    total_hotels = 0
    total_prices = 0

    async with async_session() as session:
        dest_id = await get_dest_id(session, city)

        if dates is None:
            dates = await get_unfetched_dates(session, city, max_dates or settings.dates_per_run)

        if not dates:
            logger.info("[%s] No unfetched dates remaining — all 365 days covered!", city)
            return {"dates_fetched": 0, "hotels_found": 0, "prices_saved": 0, "errors": []}

        logger.info("[%s] Fetching prices for %d dates: %s ... %s", city, len(dates), dates[0], dates[-1])

        for check_date in dates:
            checkout = check_date + timedelta(days=1)
            try:
                hotels = await booking_api.search_hotels(dest_id, check_date, checkout)
                total_hotels = max(total_hotels, len(hotels))

                for hotel_data in hotels:
                    hotel_id = await upsert_hotel(session, hotel_data, city)
                    await save_price(session, hotel_id, check_date, hotel_data["price_eur"])
                    total_prices += 1

                await session.commit()
                logger.info("[%s] Saved %d prices for %s", city, len(hotels), check_date.isoformat())
            except Exception as e:
                logger.error("[%s] Error fetching %s: %s", city, check_date.isoformat(), e)
                errors.append(f"{check_date.isoformat()}: {str(e)}")
                await session.rollback()

        # Update last_fetch timestamp per city
        async with async_session() as s2:
            now_str = datetime.utcnow().isoformat()
            for key in [f"last_fetch:{city}", "last_fetch"]:
                result = await s2.execute(select(Setting).where(Setting.key == key))
                setting = result.scalar_one_or_none()
                if setting:
                    setting.value = now_str
                else:
                    s2.add(Setting(key=key, value=now_str))
            await s2.commit()

    return {
        "dates_fetched": len(dates),
        "hotels_found": total_hotels,
        "prices_saved": total_prices,
        "errors": errors,
    }


async def fetch_all_cities(
    dates: list[date] | None = None,
    max_dates: int | None = None,
) -> dict:
    """Fetch hotel prices for all configured cities sequentially.

    Returns an aggregated summary dict.
    """
    total = {"dates_fetched": 0, "hotels_found": 0, "prices_saved": 0, "errors": []}

    for city in settings.city_list:
        logger.info("Fetching prices for city: %s", city)
        result = await fetch_prices_for_dates(city=city, dates=dates, max_dates=max_dates)
        total["dates_fetched"] += result["dates_fetched"]
        total["hotels_found"] += result["hotels_found"]
        total["prices_saved"] += result["prices_saved"]
        total["errors"].extend(f"[{city}] {e}" for e in result["errors"])

    return total
