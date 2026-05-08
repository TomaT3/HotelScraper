"""Shared test fixtures for HotelScraper integration tests.

Provides:
- In-memory SQLite database (isolated per test)
- Mocked RapidAPI (respx) — no real network calls
- FastAPI TestClient with dependency overrides
- Helper to load JSON fixtures from tests/data/
"""

import json
import os
from contextlib import asynccontextmanager
from datetime import date, datetime, timedelta
from pathlib import Path
from typing import AsyncGenerator

import pytest
import respx
from fastapi import FastAPI
from fastapi.testclient import TestClient
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

TEST_DATA_DIR = Path(__file__).parent / "data"


# ── Database ────────────────────────────────────────────────────────────────


@pytest.fixture(scope="session")
def test_db_url(tmp_path_factory):
    """Use a temporary file-based SQLite database.

    File-based ensures different connections (service-layer sessions vs
    test fixtures) see the same committed data. Cleaned up after the session.
    """
    tmp_dir = tmp_path_factory.mktemp("testdb")
    db_path = tmp_dir / "test_hotel_prices.db"
    return f"sqlite+aiosqlite:///{db_path}"


@pytest.fixture(scope="session")
def test_engine(test_db_url):
    """Session-scoped async engine for the test database."""
    engine = create_async_engine(test_db_url, echo=False)
    yield engine


@pytest.fixture(scope="session")
def _test_sessionmaker(test_engine):
    """Session-scoped async sessionmaker."""
    return async_sessionmaker(test_engine, class_=AsyncSession, expire_on_commit=False)


@pytest.fixture
async def test_session(_test_sessionmaker, test_engine) -> AsyncGenerator[AsyncSession, None]:
    """Per-test async session with fresh tables.

    Creates all tables at the start and drops them at the end of each test,
    ensuring complete isolation. Uses the engine directly for DDL to avoid
    SQLite locking issues.
    """
    from app.models import Base

    # Create tables directly on the engine (not through the session)
    async with test_engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async with _test_sessionmaker() as session:
        yield session

    # Drop tables after the session is fully closed
    async with test_engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)


# ── Settings override ───────────────────────────────────────────────────────


@pytest.fixture(autouse=True)
def override_settings(monkeypatch, test_db_url, _test_sessionmaker):
    """Override app settings and database session for all tests.

    'autouse=True' ensures every test runs with test-safe settings:
    - In-memory SQLite instead of file-based DB
    - Fake RapidAPI key (all calls are mocked by respx)
    - Stuttgart as the only city (deterministic)
    - Replaces the production async_session with test sessionmaker
    """
    monkeypatch.setattr("app.config.settings.rapidapi_key", "test-api-key-12345")
    monkeypatch.setattr("app.config.settings.database_url", test_db_url)
    monkeypatch.setattr("app.config.settings.search_cities", "Stuttgart")
    monkeypatch.setattr("app.config.settings.dates_per_run", 5)
    monkeypatch.setattr("app.config.settings.fetch_hour", 3)
    # Critical: replace the module-level async_session so that service-layer
    # code (fetch_prices_for_dates, etc.) uses the test DB, not production.
    monkeypatch.setattr("app.database.async_session", _test_sessionmaker)


# ── RapidAPI mocking (respx) ───────────────────────────────────────────────


@pytest.fixture
def mock_rapidapi():
    """Mock all calls to the Booking.com RapidAPI base URL.

    Uses respx to intercept httpx requests at the transport layer.
    Each test registers its own expected routes on the returned mock object.

    Example usage in a test::

        def test_search(mock_rapidapi):
            route = mock_rapidapi.get(
                "https://booking-com15.p.rapidapi.com/api/v1/hotels/searchDestination",
                params={"query": "Stuttgart"},
            )
            route.respond(json=load_fixture("search_destination_stuttgart.json"))
            # ... call function under test ...
    """
    with respx.mock(assert_all_called=False) as mock:
        yield mock


# ── Fixture loader ──────────────────────────────────────────────────────────


def load_fixture(name: str) -> dict:
    """Load a JSON fixture from tests/data/."""
    path = TEST_DATA_DIR / name
    if not path.exists():
        raise FileNotFoundError(f"Fixture not found: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


# ── FastAPI TestClient ──────────────────────────────────────────────────────


@pytest.fixture
def test_app(test_session) -> TestClient:
    """FastAPI TestClient wired to the test database.

    Overrides the get_db dependency so all API endpoints use the
    isolated test session instead of the production database.
    """
    from app.database import get_db
    from app.main import app

    # Override the lifespan to skip scheduler start & migrations
    # (migrations are for production DBs; test DB starts fresh)
    @asynccontextmanager
    async def test_lifespan(app: FastAPI):
        async with test_session.bind.begin() as conn:
            from app.models import Base
            await conn.run_sync(Base.metadata.create_all)
        yield
        # Scheduler never started, so no shutdown needed

    app.router.lifespan_context = test_lifespan

    # Override DB dependency to use our test session
    async def override_get_db():
        yield test_session

    app.dependency_overrides[get_db] = override_get_db

    client = TestClient(app)
    yield client

    # Cleanup overrides
    app.dependency_overrides.clear()


# ── Helper: seed test data ──────────────────────────────────────────────────


@pytest.fixture
async def seed_hotels_and_prices(test_session: AsyncSession) -> dict:
    """Seed the test DB with known hotels and prices.

    Returns a dict with hotel IDs keyed by booking_id for easy reference.
    """
    from app.models import Hotel, Price, Setting

    today = date.today()

    hotels_data = [
        {
            "booking_id": "1001",
            "name": "Stuttgart Grand Hotel",
            "stars": 5,
            "review_score": 8.7,
            "image_url": "https://example.com/grand.jpg",
            "distance_km": 0.8,
            "active": True,
            "city": "Stuttgart",
        },
        {
            "booking_id": "1002",
            "name": "City Center Inn",
            "stars": 3,
            "review_score": 7.4,
            "image_url": "https://example.com/cityinn.jpg",
            "distance_km": 0.0,
            "active": True,
            "city": "Stuttgart",
        },
        {
            "booking_id": "2001",
            "name": "Berlin Central Hotel",
            "stars": 4,
            "review_score": 8.9,
            "image_url": "https://example.com/berlin.jpg",
            "distance_km": 0.5,
            "active": False,
            "city": "Berlin",
        },
    ]

    hotel_ids = {}
    for hd in hotels_data:
        hotel = Hotel(**hd)
        test_session.add(hotel)
        await test_session.flush()
        hotel_ids[hd["booking_id"]] = hotel.id

    # Add prices for Stuttgart hotels for the next 15 days
    for h_booking_id, h_id in hotel_ids.items():
        if h_booking_id in ("1001", "1002"):
            for i in range(15):
                price_date = today + timedelta(days=i + 1)
                price = Price(
                    hotel_id=h_id,
                    date=price_date,
                    price_eur=100.0 + i * 5.0 if h_booking_id == "1001" else 70.0 + i * 2.0,
                    fetched_at=datetime.utcnow(),
                )
                test_session.add(price)

    # Add settings for cities
    test_session.add(Setting(key="dest_id:Stuttgart", value="-1873147"))
    test_session.add(Setting(key="dest_label:Stuttgart", value="Stuttgart, Germany"))
    test_session.add(Setting(key="dest_id:Berlin", value="-1746443"))
    test_session.add(Setting(key="dest_label:Berlin", value="Berlin, Germany"))
    test_session.add(Setting(key="last_fetch", value=datetime.utcnow().isoformat()))

    await test_session.commit()

    return hotel_ids
