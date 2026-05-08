"""Tests for database write/read integrity.

Verifies that data fetched from RapidAPI is correctly upserted into the DB
and that queries return expected results before serving to the client.

All RapidAPI calls are mocked via respx.
"""

from datetime import date, datetime

import pytest
from sqlalchemy import select, func

from app.models import Hotel, Price, Setting


# ═══════════════════════════════════════════════════════════════════════════
# upsert_hotel
# ═══════════════════════════════════════════════════════════════════════════


class TestUpsertHotel:
    @pytest.mark.asyncio
    async def test_insert_new_hotel(self, test_session):
        """Inserting a new hotel should create a row with all fields set."""
        from app.services.price_fetcher import upsert_hotel

        hotel_data = {
            "booking_id": "5001",
            "name": "Test Hotel One",
            "stars": 4,
            "review_score": 8.5,
            "image_url": "https://example.com/test.jpg",
            "distance_km": 2.5,
        }

        hotel_id = await upsert_hotel(test_session, hotel_data, "Stuttgart")

        result = await test_session.execute(select(Hotel).where(Hotel.id == hotel_id))
        hotel = result.scalar_one()

        assert hotel.booking_id == "5001"
        assert hotel.name == "Test Hotel One"
        assert hotel.stars == 4
        assert hotel.review_score == 8.5
        assert hotel.image_url == "https://example.com/test.jpg"
        assert hotel.distance_km == 2.5
        assert hotel.active is True
        assert hotel.city == "Stuttgart"

    @pytest.mark.asyncio
    async def test_upsert_updates_existing_hotel(self, test_session):
        """Inserting same (booking_id, city) should update, not duplicate."""
        from app.services.price_fetcher import upsert_hotel

        hotel_data = {
            "booking_id": "5002",
            "name": "Original Name",
            "stars": 3,
            "review_score": 7.0,
            "image_url": "https://example.com/old.jpg",
            "distance_km": 1.0,
        }

        first_id = await upsert_hotel(test_session, hotel_data, "Stuttgart")

        # Update with new values
        hotel_data["name"] = "Updated Name"
        hotel_data["stars"] = 5
        hotel_data["review_score"] = 9.2
        hotel_data["image_url"] = "https://example.com/new.jpg"
        hotel_data["distance_km"] = 0.5

        second_id = await upsert_hotel(test_session, hotel_data, "Stuttgart")

        # Same ID returned
        assert first_id == second_id

        # Only one row exists for this booking_id + city
        count = await test_session.execute(
            select(func.count(Hotel.id)).where(
                Hotel.booking_id == "5002", Hotel.city == "Stuttgart"
            )
        )
        assert count.scalar() == 1

        # Values are updated
        result = await test_session.execute(select(Hotel).where(Hotel.id == first_id))
        hotel = result.scalar_one()
        assert hotel.name == "Updated Name"
        assert hotel.stars == 5
        assert hotel.review_score == 9.2
        assert hotel.image_url == "https://example.com/new.jpg"
        assert hotel.distance_km == 0.5

    @pytest.mark.asyncio
    async def test_same_booking_id_different_city(self, test_session):
        """Same booking_id in different cities should create separate rows."""
        from app.services.price_fetcher import upsert_hotel

        hotel_data = {
            "booking_id": "5003",
            "name": "Chain Hotel",
            "stars": 4,
            "review_score": 8.0,
            "image_url": "",
            "distance_km": None,
        }

        stuttgart_id = await upsert_hotel(test_session, hotel_data, "Stuttgart")
        berlin_id = await upsert_hotel(test_session, hotel_data, "Berlin")

        assert stuttgart_id != berlin_id

        count = await test_session.execute(
            select(func.count(Hotel.id)).where(Hotel.booking_id == "5003")
        )
        assert count.scalar() == 2


# ═══════════════════════════════════════════════════════════════════════════
# save_price
# ═══════════════════════════════════════════════════════════════════════════


class TestSavePrice:
    @pytest.mark.asyncio
    async def test_insert_new_price(self, test_session):
        """Saving a new price should insert a row."""
        from app.services.price_fetcher import save_price

        test_date = date(2026, 6, 15)
        await save_price(test_session, hotel_id=1, price_date=test_date, price_eur=150.0)

        result = await test_session.execute(
            select(Price).where(Price.hotel_id == 1, Price.date == test_date)
        )
        price = result.scalar_one()

        assert price.price_eur == 150.0
        assert price.date == test_date
        assert price.fetched_at is not None

    @pytest.mark.asyncio
    async def test_upsert_price_updates_existing(self, test_session):
        """Saving same (hotel_id, date) should update price, not duplicate."""
        from app.services.price_fetcher import save_price

        test_date = date(2026, 7, 1)
        await save_price(test_session, hotel_id=1, price_date=test_date, price_eur=100.0)
        await test_session.commit()

        # Verify first insert
        result = await test_session.execute(
            select(Price).where(Price.hotel_id == 1, Price.date == test_date)
        )
        first = result.scalar_one()
        assert first.price_eur == 100.0

        # Upsert: same hotel_id + date, different price
        await save_price(test_session, hotel_id=1, price_date=test_date, price_eur=120.0)
        await test_session.commit()

        # Must use a fresh query (expire_on_commit=False keeps old objects alive)
        result2 = await test_session.execute(
            select(Price.price_eur).where(Price.hotel_id == 1, Price.date == test_date)
        )
        price_value = result2.scalar_one()
        assert price_value == 120.0

        # Only one row
        count = await test_session.execute(
            select(func.count(Price.id)).where(
                Price.hotel_id == 1, Price.date == test_date
            )
        )
        assert count.scalar() == 1

    @pytest.mark.asyncio
    async def test_different_hotels_same_date(self, test_session):
        """Different hotels can have prices for the same date."""
        from app.services.price_fetcher import save_price

        test_date = date(2026, 8, 1)
        await save_price(test_session, hotel_id=1, price_date=test_date, price_eur=100.0)
        await save_price(test_session, hotel_id=2, price_date=test_date, price_eur=200.0)

        count = await test_session.execute(
            select(func.count(Price.id)).where(Price.date == test_date)
        )
        assert count.scalar() == 2


# ═══════════════════════════════════════════════════════════════════════════
# get_dest_id
# ═══════════════════════════════════════════════════════════════════════════


class TestGetDestId:
    @pytest.mark.asyncio
    async def test_returns_cached_value_no_api_call(self, test_session, mock_rapidapi):
        """When dest_id is cached in settings, no API call should be made."""
        from app.services.price_fetcher import get_dest_id

        test_session.add(Setting(key="dest_id:Stuttgart", value="-1873147"))
        await test_session.commit()

        dest_id = await get_dest_id(test_session, "Stuttgart")

        assert dest_id == "-1873147"
        # respx would raise if any unmocked call was attempted
        # (verify no API call was made — no route was registered)

    @pytest.mark.asyncio
    async def test_falls_back_to_api_and_caches(self, test_session, mock_rapidapi):
        """When no cached value, should call API and store result."""
        from app.services.price_fetcher import get_dest_id
        from tests.conftest import load_fixture

        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchDestination",
            params={"query": "Stuttgart"},
        )
        route.respond(json=load_fixture("search_destination_stuttgart.json"))

        dest_id = await get_dest_id(test_session, "Stuttgart")

        assert dest_id == "-1873147"

        # Verify cached in DB
        result = await test_session.execute(
            select(Setting).where(Setting.key == "dest_id:Stuttgart")
        )
        setting = result.scalar_one()
        assert setting.value == "-1873147"

        # Verify label also cached
        label_result = await test_session.execute(
            select(Setting).where(Setting.key == "dest_label:Stuttgart")
        )
        label = label_result.scalar_one()
        assert "Stuttgart" in label.value

    @pytest.mark.asyncio
    async def test_raises_when_api_returns_none(self, test_session, mock_rapidapi):
        """When API returns no results, should raise RuntimeError."""
        from app.services.price_fetcher import get_dest_id

        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchDestination",
            params={"query": "UnknownCity"},
        )
        route.respond(json={"status": True, "data": []})

        with pytest.raises(RuntimeError, match="Could not find destination"):
            await get_dest_id(test_session, "UnknownCity")


# ═══════════════════════════════════════════════════════════════════════════
# get_next_dates
# ═══════════════════════════════════════════════════════════════════════════


class TestGetNextDates:
    def test_returns_n_dates_from_tomorrow(self):
        from app.services.price_fetcher import get_next_dates

        dates = get_next_dates(5)

        assert len(dates) == 5
        today = date.today()
        expected = [today.replace(day=today.day + i + 1) for i in range(5)]
        # Cross-month boundaries handled by timedelta internally
        from datetime import timedelta
        expected = [today + timedelta(days=i + 1) for i in range(5)]
        assert dates == expected

    def test_zero_dates(self):
        from app.services.price_fetcher import get_next_dates

        dates = get_next_dates(0)
        assert dates == []


# ═══════════════════════════════════════════════════════════════════════════
# fetch_prices_for_dates (end-to-end with mocked API)
# ═══════════════════════════════════════════════════════════════════════════


class TestFetchPricesForDates:
    @pytest.mark.asyncio
    async def test_full_fetch_workflow(self, test_session, mock_rapidapi):
        """End-to-end: mock API → fetch → verify DB has correct data."""
        from app.services.price_fetcher import fetch_prices_for_dates
        from tests.conftest import load_fixture

        # Cache the dest_id so fetch_prices_for_dates doesn't do a searchDestination call
        test_session.add(Setting(key="dest_id:Stuttgart", value="-1873147"))
        await test_session.commit()

        # Mock 2 dates of hotel search results
        dates = [date(2026, 6, 10), date(2026, 6, 11)]
        for d in dates:
            checkout = d.replace(day=d.day + 1)
            # Page 1 for this date (3 hotels)
            mock_rapidapi.get(
                "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
                params={
                    "dest_id": "-1873147",
                    "search_type": "CITY",
                    "arrival_date": d.isoformat(),
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
            ).respond(json=load_fixture("search_hotels_stuttgart_page1.json"))
            # Page 2 for this date (2 hotels, one duplicate)
            mock_rapidapi.get(
                "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
                params={
                    "dest_id": "-1873147",
                    "search_type": "CITY",
                    "arrival_date": d.isoformat(),
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
            ).respond(json=load_fixture("search_hotels_stuttgart_page2.json"))

        result = await fetch_prices_for_dates(city="Stuttgart", dates=dates)

        assert result["dates_fetched"] == 2
        # 4 unique hotels per date (1001, 1002, 1003, 1004)
        assert result["hotels_found"] == 4
        assert result["prices_saved"] == 8  # 4 hotels × 2 dates
        assert result["errors"] == []

        # Verify DB: 4 hotels in Stuttgart
        hotels_count = await test_session.execute(
            select(func.count(Hotel.id)).where(Hotel.city == "Stuttgart")
        )
        assert hotels_count.scalar() == 4

        # Verify DB: 8 prices total
        prices_count = await test_session.execute(
            select(func.count(Price.id))
        )
        assert prices_count.scalar() == 8

        # Verify last_fetch setting updated
        last_fetch = await test_session.execute(
            select(Setting).where(Setting.key == "last_fetch:Stuttgart")
        )
        assert last_fetch.scalar_one_or_none() is not None

    @pytest.mark.asyncio
    async def test_error_handling_partial_success(self, test_session, mock_rapidapi):
        """One date fails → error recorded, other dates still processed."""
        from app.services.price_fetcher import fetch_prices_for_dates
        from tests.conftest import load_fixture

        test_session.add(Setting(key="dest_id:Stuttgart", value="-1873147"))
        await test_session.commit()

        good_date = date(2026, 6, 10)
        bad_date = date(2026, 6, 11)

        # Good date: return hotels
        for pg in [1, 2]:
            mock_rapidapi.get(
                "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
                params={
                    "dest_id": "-1873147",
                    "search_type": "CITY",
                    "arrival_date": good_date.isoformat(),
                    "departure_date": good_date.replace(day=good_date.day + 1).isoformat(),
                    "adults": "2",
                    "room_qty": "1",
                    "page_number": str(pg),
                    "sort_by": "distance",
                    "units": "metric",
                    "temperature_unit": "c",
                    "languagecode": "en-us",
                    "currency_code": "EUR",
                },
            ).respond(json=load_fixture("search_hotels_stuttgart_page1.json"))

        # Bad date: return 500 error on page 1
        mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
            params={
                "dest_id": "-1873147",
                "search_type": "CITY",
                "arrival_date": bad_date.isoformat(),
                "departure_date": bad_date.replace(day=bad_date.day + 1).isoformat(),
                "adults": "2",
                "room_qty": "1",
                "page_number": "1",
                "sort_by": "distance",
                "units": "metric",
                "temperature_unit": "c",
                "languagecode": "en-us",
                "currency_code": "EUR",
            },
        ).respond(status_code=500, json={"error": "fail"})

        result = await fetch_prices_for_dates(city="Stuttgart", dates=[good_date, bad_date])

        assert result["dates_fetched"] == 2
        assert len(result["errors"]) == 1
        assert bad_date.isoformat() in result["errors"][0]
        # Good date's data should still be in DB
        prices_count = await test_session.execute(select(func.count(Price.id)))
        assert prices_count.scalar() > 0


# ═══════════════════════════════════════════════════════════════════════════
# fetch_all_cities
# ═══════════════════════════════════════════════════════════════════════════


class TestFetchAllCities:
    @pytest.mark.asyncio
    async def test_aggregates_multiple_cities(self, mock_rapidapi, monkeypatch, test_session):
        """fetch_all_cities should aggregate results from all configured cities."""
        # Override city list to include two cities (set the raw string; city_list is a property)
        monkeypatch.setattr("app.config.settings.search_cities", "Stuttgart,Berlin")

        from app.services.price_fetcher import fetch_all_cities
        from tests.conftest import load_fixture

        # Pre-cache dest_id for both cities (so get_dest_id doesn't query settings table)
        from app.models import Setting
        test_session.add(Setting(key="dest_id:Stuttgart", value="-1873147"))
        test_session.add(Setting(key="dest_id:Berlin", value="-1746443"))
        await test_session.commit()

        stuttgart_dates = [date(2026, 6, 10), date(2026, 6, 11)]

        # Mock Stuttgart (2 dates, 4 unique hotels per date)
        for d in stuttgart_dates:
            checkout = d.replace(day=d.day + 1)
            for pg, fixture_name in [(1, "search_hotels_stuttgart_page1.json"),
                                      (2, "search_hotels_stuttgart_page2.json")]:
                mock_rapidapi.get(
                    "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
                    params={
                        "dest_id": "-1873147", "search_type": "CITY",
                        "arrival_date": d.isoformat(),
                        "departure_date": checkout.isoformat(),
                        "adults": "2", "room_qty": "1",
                        "page_number": str(pg),
                        "sort_by": "distance", "units": "metric",
                        "temperature_unit": "c", "languagecode": "en-us",
                        "currency_code": "EUR",
                    },
                ).respond(json=load_fixture(fixture_name))

        # Mock Berlin (2 dates, 1 hotel each date — same data for both)
        for d in stuttgart_dates:
            checkout = d.replace(day=d.day + 1)
            for pg in [1, 2]:
                fixture = "search_hotels_berlin_page1.json" if pg == 1 else "search_hotels_berlin_page2.json"
                mock_rapidapi.get(
                    "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchHotels",
                    params={
                        "dest_id": "-1746443", "search_type": "CITY",
                        "arrival_date": d.isoformat(),
                        "departure_date": checkout.isoformat(),
                        "adults": "2", "room_qty": "1",
                        "page_number": str(pg),
                        "sort_by": "distance", "units": "metric",
                        "temperature_unit": "c", "languagecode": "en-us",
                        "currency_code": "EUR",
                    },
                ).respond(json=load_fixture(fixture))

        result = await fetch_all_cities(dates=stuttgart_dates)

        # Stuttgart: 2 dates × 4 hotels = 8 prices
        # Berlin: 2 dates × 1 hotel = 2 prices
        assert result["dates_fetched"] == 4  # 2 per city
        assert result["prices_saved"] == 10  # 8 + 2
