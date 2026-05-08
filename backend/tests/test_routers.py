"""Tests for FastAPI endpoint contracts.

These tests use a TestClient wired to an in-memory SQLite database.
They verify that every API endpoint returns data in the expected shape,
with correct filtering, error handling, and schema conformance.

The test DB is seeded via the `seed_hotels_and_prices` fixture from conftest.py.
"""

import pytest


# ═══════════════════════════════════════════════════════════════════════════
# GET /api/version  &  GET /api/config
# ═══════════════════════════════════════════════════════════════════════════


class TestVersionAndConfig:
    def test_version_endpoint(self, test_app):
        response = test_app.get("/api/version")
        assert response.status_code == 200
        data = response.json()
        assert "version" in data
        assert isinstance(data["version"], str)

    def test_config_endpoint(self, test_app):
        response = test_app.get("/api/config")
        assert response.status_code == 200
        data = response.json()
        assert "dates_per_run" in data
        assert isinstance(data["dates_per_run"], int)
        assert data["dates_per_run"] == 5  # from override_settings fixture


# ═══════════════════════════════════════════════════════════════════════════
# GET /api/cities
# ═══════════════════════════════════════════════════════════════════════════


class TestCitiesEndpoint:
    @pytest.mark.asyncio
    async def test_returns_configured_cities_with_labels(self, test_app, test_session):
        """Should return cities from settings with their dest_labels from DB."""
        from app.models import Setting as SettingModel

        # Add dest_label settings
        test_session.add(SettingModel(key="dest_label:Stuttgart", value="Stuttgart, Germany"))
        await test_session.commit()

        response = test_app.get("/api/cities")

        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)
        assert len(data) >= 1

        stuttgart = next((c for c in data if c["name"] == "Stuttgart"), None)
        assert stuttgart is not None
        assert stuttgart["dest_label"] == "Stuttgart, Germany"

    def test_city_without_label(self, test_app):
        """City without a dest_label setting should have null label."""
        response = test_app.get("/api/cities")

        assert response.status_code == 200
        data = response.json()
        stuttgart = next((c for c in data if c["name"] == "Stuttgart"), None)
        assert stuttgart is not None
        # No label seeded in this test, so should be None
        assert stuttgart["dest_label"] is None


# ═══════════════════════════════════════════════════════════════════════════
# GET /api/hotels?city=X
# ═══════════════════════════════════════════════════════════════════════════


class TestHotelsEndpoint:
    @pytest.fixture(autouse=True)
    async def setup_data(self, seed_hotels_and_prices):
        """Seed test data before each test."""
        self.hotel_ids = seed_hotels_and_prices

    def test_returns_hotels_for_city(self, test_app):
        """Should return only hotels matching the city param, ordered by name."""
        response = test_app.get("/api/hotels?city=Stuttgart")

        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)
        assert len(data) == 2  # 1001 + 1002 (Berlin 2001 excluded)

        names = [h["name"] for h in data]
        assert names == sorted(names)  # ordered by name

    def test_hotel_schema_fields(self, test_app):
        """Each hotel should match the HotelOut schema."""
        response = test_app.get("/api/hotels?city=Stuttgart")

        data = response.json()
        for hotel in data:
            assert "id" in hotel and isinstance(hotel["id"], int)
            assert "booking_id" in hotel and isinstance(hotel["booking_id"], str)
            assert "name" in hotel and isinstance(hotel["name"], str)
            assert "stars" in hotel
            assert "review_score" in hotel
            assert "image_url" in hotel
            assert "distance_km" in hotel
            assert "active" in hotel and isinstance(hotel["active"], bool)
            assert "city" in hotel and hotel["city"] == "Stuttgart"

    def test_other_city_returns_different_hotels(self, test_app):
        """Berlin should return different hotels than Stuttgart."""
        response = test_app.get("/api/hotels?city=Berlin")

        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["name"] == "Berlin Central Hotel"
        assert data[0]["booking_id"] == "2001"

    def test_unknown_city_returns_empty(self, test_app):
        """Querying a city with no hotels should return empty list."""
        response = test_app.get("/api/hotels?city=Paris")

        assert response.status_code == 200
        data = response.json()
        assert data == []


# ═══════════════════════════════════════════════════════════════════════════
# PATCH /api/hotels/{id}
# ═══════════════════════════════════════════════════════════════════════════


class TestPatchHotel:
    @pytest.fixture(autouse=True)
    async def setup_data(self, seed_hotels_and_prices):
        self.hotel_ids = seed_hotels_and_prices

    def test_set_active_false(self, test_app):
        """PATCH with active=false should deactivate hotel."""
        hotel_id = self.hotel_ids["1001"]

        response = test_app.patch(f"/api/hotels/{hotel_id}", json={"active": False})

        assert response.status_code == 200
        data = response.json()
        assert data["active"] is False
        assert data["id"] == hotel_id

    def test_set_active_true(self, test_app):
        """PATCH with active=true should reactivate."""
        hotel_id = self.hotel_ids["2001"]  # Berlin is inactive initially

        response = test_app.patch(f"/api/hotels/{hotel_id}", json={"active": True})

        assert response.status_code == 200
        assert response.json()["active"] is True

    def test_not_found_returns_404(self, test_app):
        """PATCH on nonexistent ID should return 404."""
        response = test_app.patch("/api/hotels/99999", json={"active": False})

        assert response.status_code == 404
        assert "detail" in response.json()

    def test_empty_body_no_change(self, test_app):
        """PATCH with no fields should not change anything."""
        hotel_id = self.hotel_ids["1001"]

        response = test_app.patch(f"/api/hotels/{hotel_id}", json={})

        assert response.status_code == 200
        assert response.json()["active"] is True  # unchanged


# ═══════════════════════════════════════════════════════════════════════════
# GET /api/prices
# ═══════════════════════════════════════════════════════════════════════════


class TestPricesEndpoint:
    @pytest.fixture(autouse=True)
    async def setup_data(self, seed_hotels_and_prices):
        self.hotel_ids = seed_hotels_and_prices

    def test_returns_all_hotels_with_prices(self, test_app):
        """No filters: all hotels that have prices should be returned."""
        response = test_app.get("/api/prices")

        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)

        # Stuttgart hotels have prices, Berlin hotel doesn't
        returned_ids = {h["hotel_id"] for h in data}
        assert self.hotel_ids["1001"] in returned_ids
        assert self.hotel_ids["1002"] in returned_ids
        # 2001 (Berlin) has no prices → excluded
        assert self.hotel_ids["2001"] not in returned_ids

    def test_schema_shape(self, test_app):
        """Each result should match HotelPrices schema."""
        response = test_app.get("/api/prices")

        data = response.json()
        for item in data:
            assert "hotel_id" in item and isinstance(item["hotel_id"], int)
            assert "hotel_name" in item and isinstance(item["hotel_name"], str)
            assert "stars" in item
            assert "prices" in item and isinstance(item["prices"], list)
            for price in item["prices"]:
                assert "date" in price
                assert "price_eur" in price and isinstance(price["price_eur"], float)

    def test_filter_by_hotel_ids(self, test_app):
        """hotel_ids param should filter to only specified hotels."""
        id1 = self.hotel_ids["1001"]

        response = test_app.get(f"/api/prices?hotel_ids={id1}")

        data = response.json()
        assert len(data) == 1
        assert data[0]["hotel_id"] == id1

        # Price count: seeded 15 days for hotel 1001
        assert len(data[0]["prices"]) == 15

    def test_filter_by_multiple_hotel_ids(self, test_app):
        """Comma-separated hotel_ids should return multiple hotels."""
        id1 = self.hotel_ids["1001"]
        id2 = self.hotel_ids["1002"]

        response = test_app.get(f"/api/prices?hotel_ids={id1},{id2}")

        data = response.json()
        assert len(data) == 2
        returned_ids = {h["hotel_id"] for h in data}
        assert returned_ids == {id1, id2}

    def test_filter_by_date_range(self, test_app):
        """from/to params should filter prices within date range."""
        from datetime import date, timedelta
        today = date.today()
        date_from = (today + timedelta(days=3)).isoformat()
        date_to = (today + timedelta(days=7)).isoformat()

        response = test_app.get(f"/api/prices?from={date_from}&to={date_to}")

        data = response.json()
        for item in data:
            for p in item["prices"]:
                assert p["date"] >= date_from
                assert p["date"] <= date_to

    def test_non_numeric_hotel_ids_ignored(self, test_app):
        """Non-numeric hotel_ids should be silently ignored."""
        id1 = self.hotel_ids["1001"]

        response = test_app.get(f"/api/prices?hotel_ids={id1},abc,999")

        data = response.json()
        # Only hotel 1001 returned (abc and 999 ignored/invalid)
        assert len(data) == 1
        assert data[0]["hotel_id"] == id1


# ═══════════════════════════════════════════════════════════════════════════
# GET /api/status
# ═══════════════════════════════════════════════════════════════════════════


class TestStatusEndpoint:
    @pytest.fixture(autouse=True)
    async def setup_data(self, seed_hotels_and_prices):
        self.hotel_ids = seed_hotels_and_prices

    def test_global_status(self, test_app):
        """Without city param, returns global aggregate status."""
        response = test_app.get("/api/status")

        assert response.status_code == 200
        data = response.json()

        assert "total_hotels" in data
        assert "active_hotels" in data
        assert "total_prices" in data
        assert "dates_covered" in data
        assert "dates_total" in data
        assert "coverage_pct" in data
        assert "last_fetch" in data
        assert "scheduler_running" in data
        assert "next_run" in data

        # 3 hotels total (2 Stuttgart + 1 Berlin)
        assert data["total_hotels"] == 3
        # 2 active (1001, 1002) + 1 inactive (2001)
        assert data["active_hotels"] == 2
        # 30 prices (15 for 1001 + 15 for 1002)
        assert data["total_prices"] == 30
        assert data["city"] is None  # global

    def test_city_specific_status(self, test_app):
        """With city param, returns scoped status for that city."""
        response = test_app.get("/api/status?city=Stuttgart")

        assert response.status_code == 200
        data = response.json()

        assert data["total_hotels"] == 2
        assert data["active_hotels"] == 2
        assert data["total_prices"] == 30
        assert data["city"] == "Stuttgart"

    def test_other_city_status(self, test_app):
        """City with hotels but no prices."""
        response = test_app.get("/api/status?city=Berlin")

        assert response.status_code == 200
        data = response.json()

        assert data["total_hotels"] == 1
        assert data["active_hotels"] == 0  # Berlin hotel is inactive
        assert data["total_prices"] == 0
        assert data["dates_covered"] == 0
        assert data["city"] == "Berlin"

    def test_status_schema_types(self, test_app):
        """All status fields should have correct types."""
        response = test_app.get("/api/status")

        data = response.json()
        assert isinstance(data["total_hotels"], int)
        assert isinstance(data["active_hotels"], int)
        assert isinstance(data["total_prices"], int)
        assert isinstance(data["dates_covered"], int)
        assert isinstance(data["dates_total"], int)
        assert isinstance(data["coverage_pct"], float)
        assert isinstance(data["scheduler_running"], bool)


# ═══════════════════════════════════════════════════════════════════════════
# POST /api/fetch
# ═══════════════════════════════════════════════════════════════════════════


class TestFetchEndpoint:
    @pytest.mark.skip(reason="POST /api/fetch triggers real service logic that requires respx mocking per-date; "
                             "worth adding as follow-up with proper fixture isolation")
    def test_fetch_endpoint_placeholder(self, test_app):
        """Placeholder: fetch endpoint test requires separate respx fixture scope."""
        pass
