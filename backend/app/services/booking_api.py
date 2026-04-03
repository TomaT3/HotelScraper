import logging
from datetime import date
from decimal import Decimal
from typing import Any

import httpx

from app.config import settings

logger = logging.getLogger(__name__)

def _get_headers() -> dict[str, str]:
    return {
        "x-rapidapi-host": settings.rapidapi_host,
        "x-rapidapi-key": settings.rapidapi_key,
    }


def _normalize_path(path: str) -> str:
    return path if path.startswith("/") else f"/{path}"


def _extract_list(data: Any, keys: list[str]) -> list[dict[str, Any]]:
    if isinstance(data, list):
        return [x for x in data if isinstance(x, dict)]
    if not isinstance(data, dict):
        return []

    for key in keys:
        value = data.get(key)
        if isinstance(value, list):
            return [x for x in value if isinstance(x, dict)]
        if isinstance(value, dict):
            nested = _extract_list(value, keys)
            if nested:
                return nested
    return []


def _pick_location_item(items: list[dict[str, Any]]) -> dict[str, Any] | None:
    for item in items:
        if str(item.get("dest_type", "")).lower() == "city":
            return item
    return items[0] if items else None


def _extract_location(item: dict[str, Any], fallback_city: str) -> dict[str, str] | None:
    dest_id = item.get("dest_id") or item.get("destination_id") or item.get("id")
    if dest_id is None:
        return None
    dest_type = (
        item.get("dest_type")
        or item.get("destination_type")
        or item.get("type")
        or "city"
    )
    label = (
        item.get("label")
        or item.get("name")
        or item.get("city_name")
        or fallback_city
    )
    return {"dest_id": str(dest_id), "dest_type": str(dest_type), "label": str(label)}


def _as_float(value: Any) -> float | None:
    if value is None:
        return None
    if isinstance(value, (int, float, Decimal)):
        return float(value)
    if isinstance(value, str):
        cleaned = value.strip().replace(",", ".")
        if not cleaned:
            return None
        try:
            return float(cleaned)
        except ValueError:
            return None
    if isinstance(value, dict):
        for key in ["value", "amount", "price", "gross_price"]:
            nested = _as_float(value.get(key))
            if nested is not None:
                return nested
    return None


def _extract_hotel_price(hotel: dict[str, Any]) -> float | None:
    candidates = [
        hotel.get("min_total_price"),
        hotel.get("min_price"),
        hotel.get("price"),
        hotel.get("price_eur"),
        hotel.get("composite_price_breakdown", {}).get("gross_amount_per_night", {}).get("value"),
        hotel.get("price_breakdown", {}).get("gross_price"),
        hotel.get("price_breakdown", {}).get("gross_amount"),
        hotel.get("property", {}).get("price", {}).get("lead", {}).get("amount"),
    ]
    for candidate in candidates:
        parsed = _as_float(candidate)
        if parsed is not None and parsed > 0:
            return parsed
    return None


def _extract_hotels(data: Any) -> list[dict[str, Any]]:
    hotel_items = _extract_list(
        data,
        ["result", "results", "hotels", "items", "data", "property", "properties"],
    )

    results: list[dict[str, Any]] = []
    for hotel in hotel_items:
        price = _extract_hotel_price(hotel)
        if price is None:
            continue

        booking_id = hotel.get("hotel_id") or hotel.get("hotelId") or hotel.get("id")
        if booking_id is None:
            continue

        name = (
            hotel.get("hotel_name_trans")
            or hotel.get("hotel_name")
            or hotel.get("name")
            or hotel.get("title")
            or "Unknown"
        )
        stars = _as_float(hotel.get("class") or hotel.get("stars") or hotel.get("star_rating"))
        review_score = _as_float(hotel.get("review_score") or hotel.get("reviewScore") or hotel.get("rating"))

        image_url = (
            hotel.get("max_photo_url")
            or hotel.get("main_photo_url")
            or hotel.get("image_url")
            or hotel.get("photoMainUrl")
            or ""
        )

        results.append(
            {
                "booking_id": str(booking_id),
                "name": str(name),
                "address": hotel.get("address") or hotel.get("address_trans") or "",
                "stars": int(stars) if stars is not None else None,
                "review_score": float(review_score) if review_score is not None else None,
                "image_url": str(image_url),
                "price_eur": round(float(price), 2),
            }
        )
    return results


async def _request_json(path: str, params: dict[str, Any]) -> Any:
    async with httpx.AsyncClient(timeout=60) as client:
        resp = await client.get(
            f"{settings.rapidapi_base_url}{_normalize_path(path)}",
            headers=_get_headers(),
            params=params,
        )
        resp.raise_for_status()
        return resp.json()


async def search_location(city: str) -> dict | None:
    """Search for a city destination and return its dest_id and dest_type."""
    candidates: list[tuple[str, dict[str, Any]]] = [
        (settings.rapidapi_location_endpoint, {"name": city, "locale": "de"}),
        ("/api/v1/hotels/searchDestination", {"query": city, "locale": "de-DE"}),
        ("/api/v1/hotels/searchDestination", {"query": city}),
        ("/v1/hotels/locations", {"name": city, "locale": "de"}),
    ]

    last_error: Exception | None = None
    for endpoint, params in candidates:
        try:
            data = await _request_json(endpoint, params)
            items = _extract_list(data, ["result", "results", "data", "destinations", "items"])
            item = _pick_location_item(items)
            location = _extract_location(item, city) if item else None
            if location:
                return location
        except httpx.HTTPStatusError as e:
            last_error = e
            if e.response.status_code in {400, 404}:
                continue
            raise
        except Exception as e:
            last_error = e
            continue

    if last_error:
        logger.warning("Location search fallback exhausted for %s: %s", city, last_error)
    return None


async def search_hotels(dest_id: str, checkin: date, checkout: date) -> list[dict]:
    """Search for hotels with prices for a specific date range.

    Returns a list of dicts with hotel info and price.
    """
    parameter_candidates: list[dict[str, Any]] = [
        {
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
        },
        {
            "dest_id": dest_id,
            "search_type": "CITY",
            "arrival_date": checkin.isoformat(),
            "departure_date": checkout.isoformat(),
            "adults": "2",
            "room_qty": "1",
            "currency_code": "EUR",
            "languagecode": "de",
            "page_number": "1",
        },
    ]
    endpoint_candidates = [
        settings.rapidapi_hotels_endpoint,
        "/api/v1/hotels/searchHotels",
        "/v1/hotels/search",
    ]

    last_error: Exception | None = None
    for endpoint in endpoint_candidates:
        for params in parameter_candidates:
            try:
                data = await _request_json(endpoint, params)
                results = _extract_hotels(data)
                if results:
                    logger.info("Found %d hotels with prices for %s", len(results), checkin.isoformat())
                    return results
            except httpx.HTTPStatusError as e:
                last_error = e
                if e.response.status_code in {400, 404}:
                    continue
                raise
            except Exception as e:
                last_error = e
                continue

    if last_error:
        logger.warning(
            "Hotel search fallback exhausted for %s to %s: %s",
            checkin.isoformat(),
            checkout.isoformat(),
            last_error,
        )
    return []
