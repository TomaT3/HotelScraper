---
name: hotelscraper-integration-tests
description: Use when adding, modifying, or debugging integration tests in the HotelScraper project. Covers RapidAPI mocking, database test fixtures, FastAPI TestClient patterns, and CI/CD integration. Always invoke this skill before writing any test code in this project.
---

# HotelScraper Integration Tests

## Overview

The test suite verifies three layers:

1. **RapidAPI interface** — `test_booking_api.py`: Response parsing, deduplication, error handling (mocked via `respx`)
2. **Database integrity** — `test_price_fetcher.py`: Write/read verification, upsert behavior, end-to-end fetch workflows
3. **API endpoint contracts** — `test_routers.py`: FastAPI endpoint schema conformance, filtering, status codes

All external API calls are mocked. No real RapidAPI calls happen during tests.

## Stack

| Component | Tool | Why |
|-----------|------|-----|
| Test runner | `pytest` + `pytest-asyncio` (mode=auto) | Async SQLAlchemy + httpx |
| HTTP mocking | `respx>=0.22.0` | Intercepts `httpx` at transport layer — works because `booking_api.py` creates `AsyncClient` internally with `async with` |
| Test DB | Temp-file SQLite (`sqlite+aiosqlite:///{tmp_path}/test.db`) | File-based avoids "database is locked" issues that in-memory shared cache has across different session instances |
| API testing | `fastapi.testclient.TestClient` | Dependency override for test DB session |

## File Layout

```
backend/tests/
├── __init__.py
├── conftest.py              # ALL shared fixtures
├── data/
│   ├── search_destination_stuttgart.json
│   ├── search_hotels_stuttgart_page1.json
│   ├── search_hotels_stuttgart_page2.json
│   ├── search_hotels_berlin_page1.json
│   └── search_hotels_berlin_page2.json
├── test_booking_api.py      # RapidAPI parsing, dedup, errors
├── test_price_fetcher.py    # DB upsert, save_price, fetch workflows
└── test_routers.py          # FastAPI endpoint contracts
```

## Critical Fixtures (conftest.py)

### `test_db_url` (session-scoped)
Temp file DB URL: `sqlite+aiosqlite:///{tmp_path}/test_hotel_prices.db`

### `test_engine` (session-scoped)
`create_async_engine(test_db_url)`. Created once per session.

### `_test_sessionmaker` (session-scoped)
`async_sessionmaker(test_engine, class_=AsyncSession, expire_on_commit=False)`

### `test_session` (function-scoped) — **USE THIS for direct DB access**
Creates all tables via `test_engine.begin()` BEFORE yielding the session, drops them AFTER. **Always use `test_engine` directly for DDL, never `session.bind`** — the latter causes SQLite locking in teardown.

```python
async def test_something(test_session):
    from app.models import Hotel
    hotel = Hotel(booking_id="123", name="Test", city="Stuttgart", active=True)
    test_session.add(hotel)
    await test_session.commit()
```

### `override_settings` (autouse, function-scoped)
Monkeypatches:
- `app.config.settings.rapidapi_key` → `"test-api-key-12345"`
- `app.config.settings.database_url` → `test_db_url`
- `app.config.settings.search_cities` → `"Stuttgart"`
- `app.config.settings.dates_per_run` → `5`
- **`app.database.async_session` → `_test_sessionmaker`** ← CRITICAL

The last one is essential because `fetch_prices_for_dates()` and `fetch_all_cities()` create their own sessions via `async_session()`. Without this monkeypatch, they'd use the production engine.

### `mock_rapidapi` (function-scoped)
```python
def test_something(mock_rapidapi):
    route = mock_rapidapi.get("https://booking-com15.p.rapidapi.com/...", params={...})
    route.respond(json={"status": True, "data": [...]})
    # Call function under test...
```
Uses `respx.mock(assert_all_called=False)` — unmatched routes are silently ignored (no false positives).

### `test_app` (function-scoped)
FastAPI `TestClient` with:
- Lifespan replaced to skip scheduler start
- `get_db` dependency overridden to use `test_session`

### `seed_hotels_and_prices` (function-scoped)
Seeds 3 hotels (2 Stuttgart, 1 Berlin) + 15 days of prices for Stuttgart hotels + settings rows. Returns `dict[booking_id → hotel_id]`.

### `load_fixture(name)` (helper function, not a fixture)
```python
from tests.conftest import load_fixture
data = load_fixture("search_destination_stuttgart.json")
```

## How to Add a New Test

### Adding a RapidAPI parsing test

Add to `test_booking_api.py`:

```python
class TestNewFeature:
    @pytest.mark.asyncio
    async def test_something(self, mock_rapidapi):
        # 1. Register mock route
        route = mock_rapidapi.get(
            "https://booking-com15.p.rapidapi.com/api/v1/hotels/someEndpoint",
            params={"param": "value"},
        )
        route.respond(json={"expected": "response"})

        # 2. Call the function
        result = await some_function("value")

        # 3. Assert
        assert result["field"] == "expected"

    @pytest.mark.asyncio
    async def test_http_error(self, mock_rapidapi):
        route = mock_rapidapi.get("...")
        route.respond(status_code=500)

        with pytest.raises(httpx.HTTPStatusError):
            await some_function("value")
```

**Key rule**: Async tests MUST use `@pytest.mark.asyncio` and `await` the async function. Pure functions (like `_parse_distance_from_label`) don't need it.

### Adding a DB integrity test

Add to `test_price_fetcher.py`:

```python
class TestNewDbFeature:
    @pytest.mark.asyncio
    async def test_upsert_behavior(self, test_session):
        from app.services.price_fetcher import some_function

        result = await some_function(test_session, ...)
        await test_session.commit()

        # Verify via fresh query
        row = await test_session.execute(select(Model).where(...))
        assert row.scalar_one().field == expected
```

**Key rule**: After calling service functions that execute SQL (like `save_price`, `upsert_hotel`), call `await test_session.commit()` before re-querying. Without it, uncommitted changes won't be visible in a fresh `select()`.

### Adding an endpoint contract test

Add to `test_routers.py`:

```python
class TestNewEndpoint:
    @pytest.fixture(autouse=True)
    async def setup_data(self, seed_hotels_and_prices):
        self.hotel_ids = seed_hotels_and_prices

    def test_endpoint_response(self, test_app):
        response = test_app.get("/api/new-endpoint?param=value")
        assert response.status_code == 200
        data = response.json()
        assert "expected_field" in data
```

**Key rules**:
- If you need to add DB data before the test, create an `async` fixture with `@pytest.mark.asyncio` and `autouse=True`
- `test_app.get()` is synchronous (FastAPI TestClient wraps async in an event loop)
- If you need `await` in the test (e.g., `await test_session.commit()`), mark the test `@pytest.mark.asyncio` — sync `test_app.get()` still works fine inside async tests

### Adding a new JSON fixture

1. Add a `.json` file to `tests/data/`
2. Load it with `load_fixture("filename.json")`

## Common Pitfalls

### "database is locked" on teardown
**Cause**: Using `session.bind.begin()` for DDL instead of `test_engine.begin()`.
**Fix**: Always use `test_engine` directly for `create_all`/`drop_all`.

### "no such table: settings"
**Cause**: A test using service-layer functions (like `fetch_prices_for_dates`) without `test_session` fixture. The service creates its own session, but tables only exist if `test_session` ran.
**Fix**: Add `test_session` to the test function parameters, even if you don't use the session object directly.

### "RESPX: ... not mocked!"
**Cause**: `fetch_prices_for_dates` or `fetch_all_cities` generates dates dynamically and calls the API for each date. You must mock ALL expected calls.
**Fix**: Loop over the dates in your mock setup and register a route for each one.

### Price upsert test shows old value
**Cause**: `expire_on_commit=False` in `_test_sessionmaker` means objects stay in the session after commit. A `select()` that matches the same PK returns the cached object.
**Fix**: Select only the column you care about: `select(Price.price_eur).where(...)` instead of `select(Price).where(...)`.

### `city_list` property has no setter
**Cause**: `Settings.city_list` is a Pydantic `@property`, not a field. You cannot `monkeypatch.setattr("app.config.settings.city_list", ...)`.
**Fix**: Monkeypatch `search_cities` instead: `monkeypatch.setattr("app.config.settings.search_cities", "CityA,CityB")`.

### `await` in sync test function
**Cause**: Using `await test_session.commit()` in a non-async test.
**Fix**: Mark the test `@pytest.mark.asyncio` and make it `async def`. This is fine even with sync `test_app.get()` calls.

## Running Tests

```bash
# Install dependencies (one-time)
cd backend
pip install -e ".[dev]"

# Run all tests
pytest tests/ -v

# Run specific file
pytest tests/test_booking_api.py -v

# Run specific test
pytest tests/test_booking_api.py::TestSearchLocation::test_finds_city_type_result -v

# Run with short traceback (good for CI)
pytest tests/ -v --tb=short
```

## CI/CD

`.github/workflows/test.yml` triggers on:
- `pull_request` to `main`
- `push` to `main`
- `workflow_dispatch` (manual)

No secrets needed — all RapidAPI calls are mocked.

## What NOT to Do

- **NEVER** make real RapidAPI calls in tests. Always use `mock_rapidapi`.
- **NEVER** use `session.bind.begin()` for DDL — use `test_engine.begin()`.
- **NEVER** monkeypatch `city_list` property — use `search_cities` string.
- **NEVER** add the production database file to test fixtures — use the temp file URL from `test_db_url`.
- **NEVER** forget `await` before `test_session.commit()` in async tests.
- **NEVER** start the APScheduler in tests — the `test_app` fixture replaces the lifespan to skip it.
