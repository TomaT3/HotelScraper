"""Tests for RapidAPI Booking.com interface — response parsing & error handling.

These tests mock the RapidAPI at the HTTP transport layer (via respx) and
verify that booking_api.py parses responses correctly, handles errors,
deduplicates, and filters invalid data.
"""

from datetime import date

import httpx
import pytest

from app.services.booking_api import (
    _parse_distance_from_label,
    search_location,
    search_hotels,
)
from tests.conftest import load_fixture


# ═══════════════════════════════════════════════════════════════════════════
# _parse_distance_from_label (pure function — no async needed)
# ═══════════════════════════════════════════════════════════════════════════


class TestParseDistance:
    def test_miles_from_centre(self):
        assert _parse_distance_from_label("11 miles from centre") == pytest.approx(17.70, abs=0.01)

    def test_miles_from_centre_with_decimal(self):
        assert _parse_distance_from_label("4.1 miles from centre") == pytest.approx(6.60, abs=0.01)

    def test_in_city_centre(self):
        assert _parse_distance_from_label("In city centre") == 0.0

    def test_in_city_centre_case_insensitive(self):
        assert _parse_distance_from_label("IN CITY CENTRE") == 0.0

    def test_km_from_centre(self):
        assert _parse_distance_from_label("5 km from centre") == 5.0

    def test_km_from_centre_with_space(self):
        assert _parse_distance_from_label("12.5km from centre") == 12.5

    def test_empty_string(self):
        assert _parse_distance_from_label("") is None

    def test_none_input(self):
        assert _parse_distance_from_label(None) is None

    def test_garbage_string(self):
        assert _parse_distance_from_label("some random text") is None

    def test_miles_missing_number(self):
        assert _parse_distance_from_label("miles from centre") is None


# ═══════════════════════════════════════════════════════════════════════════
# search_location (async)
# ═══════════════════════════════════════════════════════════════════════════


class TestSearchLocation:
    """Tests for search_location() — destination lookup."""

    @pytest.mark.asyncio
    async def test_finds_city_type_result(self, mock_rapidapi):
        """Should return the city-type destination when multiple types exist."""
        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchDestination",
            params={"query": "Stuttgart"},
        )
        route.respond(json=load_fixture("search_destination_stuttgart.json"))

        result = await search_location("Stuttgart")

        assert result is not None
        assert result["dest_id"] == "-1873147"
        assert result["dest_type"] == "city"
        assert "Stuttgart" in result["label"]

    @pytest.mark.asyncio
    async def test_no_results_returns_none(self, mock_rapidapi):
        """Should return None when API returns empty data array."""
        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchDestination",
            params={"query": "UnknownCity12345"},
        )
        route.respond(json={"status": True, "data": []})

        result = await search_location("UnknownCity12345")

        assert result is None

    @pytest.mark.asyncio
    async def test_falls_back_to_first_when_no_city_type(self, mock_rapidapi):
        """Should return first result when no dest_type='city' is found."""
        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchDestination",
            params={"query": "SomePlace"},
        )
        route.respond(json={
            "status": True,
            "data": [
                {"dest_id": "123", "dest_type": "region", "label": "SomePlace, Region", "name": "SomePlace"},
            ],
        })

        result = await search_location("SomePlace")

        assert result is not None
        assert result["dest_id"] == "123"

    @pytest.mark.asyncio
    async def test_http_error_propagates(self, mock_rapidapi):
        """Should raise HTTPStatusError on 4xx/5xx responses."""
        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchDestination",
            params={"query": "Stuttgart"},
        )
        route.respond(status_code=429, json={"message": "Too Many Requests"})

        with pytest.raises(httpx.HTTPStatusError):
            await search_location("Stuttgart")


# ═══════════════════════════════════════════════════════════════════════════
# search_hotels (async)
# ═══════════════════════════════════════════════════════════════════════════


class TestSearchHotels:
    """Tests for search_hotels() — hotel search with prices."""

    @pytest.fixture
    def checkin(self):
        return date(2026, 6, 1)

    @pytest.fixture
    def checkout(self):
        return date(2026, 6, 2)

    def _register_stuttgart_pages(self, mock_rapidapi, checkin, checkout):
        """Helper: register mocked responses for both pages."""
        page1 = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
            params={
                "dest_id": "-1873147",
                "search_type": "CITY",
                "arrival_date": checkin.isoformat(),
                "departure_date": checkout.isoformat(),
                "adults": "2",
                "room_qty": "1",
                "page_number": "1",
                "sort_by": "distance",
                "units": "metric",
                "temperature_unit": "c",
                "languagecode": "en-us",
                "currency_code": "EUR",
            },
        )
        page1.respond(json=load_fixture("search_hotels_stuttgart_page1.json"))

        page2 = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
            params={
                "dest_id": "-1873147",
                "search_type": "CITY",
                "arrival_date": checkin.isoformat(),
                "departure_date": checkout.isoformat(),
                "adults": "2",
                "room_qty": "1",
                "page_number": "2",
                "sort_by": "distance",
                "units": "metric",
                "temperature_unit": "c",
                "languagecode": "en-us",
                "currency_code": "EUR",
            },
        )
        page2.respond(json=load_fixture("search_hotels_stuttgart_page2.json"))

    @pytest.mark.asyncio
    async def test_parses_all_hotels_correctly(self, mock_rapidapi, checkin, checkout):
        """Should parse all hotels from both pages with correct fields."""
        self._register_stuttgart_pages(mock_rapidapi, checkin, checkout)

        results = await search_hotels("-1873147", checkin, checkout)

        # Page1 has 3 + Page2 has 2, but booking_id=1001 is duplicated → 4 unique
        assert len(results) == 4
        booking_ids = {r["booking_id"] for r in results}
        assert booking_ids == {"1001", "1002", "1003", "1004"}

    @pytest.mark.asyncio
    async def test_result_shape_is_correct(self, mock_rapidapi, checkin, checkout):
        """Each result must have the expected dictionary shape."""
        self._register_stuttgart_pages(mock_rapidapi, checkin, checkout)

        results = await search_hotels("-1873147", checkin, checkout)

        for hotel in results:
            assert isinstance(hotel["booking_id"], str)
            assert isinstance(hotel["name"], str) and hotel["name"]
            assert isinstance(hotel["stars"], (int, type(None)))
            assert isinstance(hotel["review_score"], (float, type(None)))
            assert isinstance(hotel["image_url"], str)
            assert isinstance(hotel["price_eur"], float) and hotel["price_eur"] > 0
            assert isinstance(hotel["distance_km"], (float, type(None)))

    @pytest.mark.asyncio
    async def test_distance_parsing(self, mock_rapidapi, checkin, checkout):
        """Should correctly parse distance from accessibility label."""
        self._register_stuttgart_pages(mock_rapidapi, checkin, checkout)

        results = await search_hotels("-1873147", checkin, checkout)

        by_id = {r["booking_id"]: r for r in results}
        assert by_id["1001"]["distance_km"] == pytest.approx(0.80, abs=0.01)
        assert by_id["1002"]["distance_km"] == 0.0
        assert by_id["1003"]["distance_km"] == pytest.approx(3.38, abs=0.01)
        assert by_id["1004"]["distance_km"] == pytest.approx(8.05, abs=0.01)

    @pytest.mark.asyncio
    async def test_image_url_fallback_to_empty_string(self, mock_rapidapi, checkin, checkout):
        """Hotel with empty photoUrls should get '' as image_url."""
        self._register_stuttgart_pages(mock_rapidapi, checkin, checkout)

        results = await search_hotels("-1873147", checkin, checkout)

        by_id = {r["booking_id"]: r for r in results}
        assert by_id["1003"]["image_url"] == ""

    @pytest.mark.asyncio
    async def test_deduplication_by_booking_id(self, mock_rapidapi, checkin, checkout):
        """Duplicate booking_id across pages should appear only once."""
        self._register_stuttgart_pages(mock_rapidapi, checkin, checkout)

        results = await search_hotels("-1873147", checkin, checkout)

        count_1001 = sum(1 for r in results if r["booking_id"] == "1001")
        assert count_1001 == 1

    @pytest.mark.asyncio
    async def test_http_error_propagates(self, mock_rapidapi, checkin, checkout):
        """HTTP errors from the API should raise HTTPStatusError."""
        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
            params={
                "dest_id": "-1873147",
                "search_type": "CITY",
                "arrival_date": checkin.isoformat(),
                "departure_date": checkout.isoformat(),
                "adults": "2",
                "room_qty": "1",
                "page_number": "1",
                "sort_by": "distance",
                "units": "metric",
                "temperature_unit": "c",
                "languagecode": "en-us",
                "currency_code": "EUR",
            },
        )
        route.respond(status_code=500, json={"error": "Internal Server Error"})

        with pytest.raises(httpx.HTTPStatusError):
            await search_hotels("-1873147", checkin, checkout)
