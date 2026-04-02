import logging
from datetime import date

import httpx

from app.config import settings

logger = logging.getLogger(__name__)

BASE_URL = "https://booking-com.p.rapidapi.com"
HEADERS = {
    "X-RapidAPI-Host": "booking-com.p.rapidapi.com",
}


def _get_headers() -> dict[str, str]:
    return {**HEADERS, "X-RapidAPI-Key": settings.rapidapi_key}


async def search_location(city: str) -> dict | None:
    """Search for a city destination and return its dest_id and dest_type."""
    async with httpx.AsyncClient(timeout=30) as client:
        resp = await client.get(
            f"{BASE_URL}/v1/hotels/locations",
            headers=_get_headers(),
            params={"name": city, "locale": "de"},
        )
        resp.raise_for_status()
        data = resp.json()
        if not data:
            return None
        # Find city-type result
        for item in data:
            if item.get("dest_type") == "city":
                return {"dest_id": item["dest_id"], "dest_type": "city", "label": item.get("label", city)}
        # Fallback to first result
        return {"dest_id": data[0]["dest_id"], "dest_type": data[0].get("dest_type", "city"), "label": data[0].get("label", city)}


async def search_hotels(dest_id: str, checkin: date, checkout: date) -> list[dict]:
    """Search for hotels with prices for a specific date range.

    Returns a list of dicts with hotel info and price.
    """
    params = {
        "dest_id": dest_id,
        "dest_type": "city",
        "checkin_date": checkin.isoformat(),
        "checkout_date": checkout.isoformat(),
        "adults_number": "2",
        "room_number": "1",
        "units": "metric",
        "filter_by_currency": "EUR",
        "order_by": "popularity",
        "locale": "de",
        "page_number": "0",
        "include_adjacency": "true",
    }

    async with httpx.AsyncClient(timeout=60) as client:
        resp = await client.get(
            f"{BASE_URL}/v1/hotels/search",
            headers=_get_headers(),
            params=params,
        )
        resp.raise_for_status()
        data = resp.json()

    results = []
    for hotel in data.get("result", []):
        try:
            price = hotel.get("min_total_price") or hotel.get("composite_price_breakdown", {}).get("gross_amount_per_night", {}).get("value")
            if price is None:
                price_str = hotel.get("price_breakdown", {}).get("gross_price", "0")
                price = float(str(price_str).replace(",", ".")) if price_str else None

            if price is None or price <= 0:
                continue

            results.append({
                "booking_id": str(hotel.get("hotel_id", "")),
                "name": hotel.get("hotel_name_trans") or hotel.get("hotel_name", "Unknown"),
                "address": hotel.get("address", ""),
                "stars": int(hotel.get("class", 0)) if hotel.get("class") else None,
                "review_score": float(hotel.get("review_score", 0)) if hotel.get("review_score") else None,
                "image_url": hotel.get("max_photo_url") or hotel.get("main_photo_url", ""),
                "price_eur": round(float(price), 2),
            })
        except (ValueError, TypeError, KeyError) as e:
            logger.warning("Failed to parse hotel data: %s — %s", e, hotel.get("hotel_name", "?"))
            continue

    logger.info("Found %d hotels with prices for %s", len(results), checkin.isoformat())
    return results
