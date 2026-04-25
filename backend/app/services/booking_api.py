import logging
import re
from datetime import date

import httpx

from app.config import settings

logger = logging.getLogger(__name__)

BASE_URL = "https://booking-com15.p.rapidapi.com"
HEADERS = {
    "X-RapidAPI-Host": "booking-com15.p.rapidapi.com",
}


def _get_headers() -> dict[str, str]:
    return {**HEADERS, "X-RapidAPI-Key": settings.rapidapi_key}


def _parse_distance_from_label(accessibility_label: str) -> float | None:
    """Parse distance from city centre out of an accessibilityLabel string.

    Handles patterns like:
      - "11 miles from centre"
      - "4.1 miles from centre"
      - "In city centre" (distance = 0)
    Returns distance in kilometres.
    """
    if not accessibility_label:
        return None

    # Check if it's explicitly in the city centre
    if "in city centre" in accessibility_label.lower():
        return 0.0

    # Match patterns like "11 miles from centre" or "4.1 miles from centre"
    match = re.search(
        r'([\d.]+)\s*miles?\s*from\s*centre',
        accessibility_label,
        re.IGNORECASE,
    )
    if match:
        miles = float(match.group(1))
        return round(miles * 1.60934, 2)  # Convert miles to km

    # Match patterns like "11 km from centre"
    match = re.search(
        r'([\d.]+)\s*km\s*from\s*centre',
        accessibility_label,
        re.IGNORECASE,
    )
    if match:
        return round(float(match.group(1)), 2)

    return None


async def search_location(city: str) -> dict | None:
    """Search for a city destination and return its dest_id and dest_type."""
    async with httpx.AsyncClient(timeout=30) as client:
        resp = await client.get(
            f"{BASE_URL}/api/v1/hotels/searchDestination",
            headers=_get_headers(),
            params={"query": city},
        )
        resp.raise_for_status()
        data = resp.json()

    # The response structure: { "status": true, "data": [ { "dest_id": "...", ... } ] }
    destinations = data.get("data", [])
    if not destinations:
        return None

    # Find city-type result
    for item in destinations:
        if item.get("dest_type") == "city":
            return {
                "dest_id": str(item["dest_id"]),
                "dest_type": "city",
                "label": item.get("label", item.get("name", city)),
            }

    # Fallback to first result
    first = destinations[0]
    return {
        "dest_id": str(first["dest_id"]),
        "dest_type": first.get("dest_type", "city"),
        "label": first.get("label", first.get("name", city)),
    }


async def _fetch_hotel_page(
    dest_id: str, checkin: date, checkout: date, page_number: int
) -> list[dict]:
    """Fetch a single page of hotel search results from the API."""
    params = {
        "dest_id": dest_id,
        "search_type": "CITY",
        "arrival_date": checkin.isoformat(),
        "departure_date": checkout.isoformat(),
        "adults": "2",
        "room_qty": "1",
        "units": "metric",
        "temperature_unit": "c",
        "languagecode": "en-us",
        "currency_code": "EUR",
        "page_number": str(page_number),
        "sort_by": "distance",
    }

    async with httpx.AsyncClient(timeout=60) as client:
        resp = await client.get(
            f"{BASE_URL}/api/v1/hotels/searchHotels",
            headers=_get_headers(),
            params=params,
        )
        resp.raise_for_status()
        data = resp.json()

    results = []
    hotels = data.get("data", {}).get("hotels", [])
    for hotel in hotels:
        try:
            prop = hotel.get("property", {})
            price_breakdown = prop.get("priceBreakdown", {})
            gross_price = price_breakdown.get("grossPrice", {})

            price = gross_price.get("value")
            if price is None:
                continue

            price_value = float(price)
            if price_value <= 0:
                continue

            # Extract distance from accessibility label
            accessibility_label = hotel.get("accessibilityLabel", "")
            distance_km = _parse_distance_from_label(accessibility_label)

            # Get photo URL (first one from the array)
            photo_urls = prop.get("photoUrls", [])
            image_url = photo_urls[0] if photo_urls else ""

            results.append({
                "booking_id": str(prop.get("id", "")),
                "name": prop.get("name", "Unknown"),
                "stars": int(prop.get("accuratePropertyClass", 0)) if prop.get("accuratePropertyClass") else None,
                "review_score": float(prop.get("reviewScore", 0)) if prop.get("reviewScore") else None,
                "image_url": image_url,
                "price_eur": round(price_value, 2),
                "distance_km": distance_km,
            })
        except (ValueError, TypeError, KeyError) as e:
            logger.warning("Failed to parse hotel data: %s — %s", e, prop.get("name", "?"))
            continue

    return results


async def search_hotels(dest_id: str, checkin: date, checkout: date) -> list[dict]:
    """Search for hotels with prices for a specific date range.

    Fetches page 1 and page 2 of results, then deduplicates by booking_id
    to avoid saving the same hotel twice. Returns a list of dicts with
    hotel info, price, and distance from centre.
    """
    # Fetch page 1
    page1 = await _fetch_hotel_page(dest_id, checkin, checkout, 1)

    # Fetch page 2 (some hotels may appear on both pages)
    page2 = await _fetch_hotel_page(dest_id, checkin, checkout, 2)

    # Deduplicate by booking_id — keep the first occurrence
    seen: set[str] = set()
    combined: list[dict] = []
    for hotel in page1 + page2:
        bid = hotel["booking_id"]
        if bid not in seen:
            seen.add(bid)
            combined.append(hotel)

    logger.info(
        "Found %d unique hotels (page1=%d, page2=%d) for %s",
        len(combined), len(page1), len(page2), checkin.isoformat(),
    )
    return combined
