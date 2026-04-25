import logging
import os
import tomllib
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from fastapi.responses import FileResponse
from sqlalchemy import text

from app.database import init_db, engine
from app.config import settings
from app.routers import hotels, prices
from app.services.scheduler import start_scheduler, stop_scheduler


def _get_project_version() -> str:
    """Return the app version.

    Priority:
    1. APP_VERSION env var (set via Docker build-arg, e.g. "v1.6.0")
    2. version field from pyproject.toml (fallback for dev / local)
    3. "unknown"
    """
    env_version = os.getenv("APP_VERSION")
    if env_version and env_version != "unknown":
        return env_version
    pyproject = Path(__file__).parent.parent / "pyproject.toml"
    if pyproject.exists():
        try:
            with open(pyproject, "rb") as f:
                data = tomllib.load(f)
            return data.get("project", {}).get("version", "unknown")
        except Exception:
            pass
    return "unknown"



logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)


async def run_migrations():
    """Run database migrations for columns added to models after initial table creation."""
    async with engine.begin() as conn:
        result = await conn.execute(text("PRAGMA table_info(hotels)"))
        columns = [row[1] for row in result.fetchall()]

        # Migration: add 'city' column
        if "city" not in columns:
            logger.info("Migrating: adding 'city' column to hotels table...")
            await conn.execute(text("ALTER TABLE hotels ADD COLUMN city TEXT NOT NULL DEFAULT ''"))
            await conn.execute(text("UPDATE hotels SET city = :city WHERE city = ''"), {"city": settings.city_list[0]})
            try:
                await conn.execute(text("DROP INDEX IF EXISTS ix_hotels_booking_id"))
            except Exception:
                pass
            await conn.execute(text("CREATE INDEX IF NOT EXISTS ix_hotels_city ON hotels (city)"))
            await conn.execute(text("CREATE UNIQUE INDEX IF NOT EXISTS uq_booking_city ON hotels (booking_id, city)"))
            logger.info("Migration complete: city column added, default='%s'", settings.city_list[0])
        else:
            # Backfill any empty city values
            await conn.execute(text("UPDATE hotels SET city = :city WHERE city = '' OR city IS NULL"), {"city": settings.city_list[0]})

        # Migration: add 'distance_km' column
        if "distance_km" not in columns:
            logger.info("Migrating: adding 'distance_km' column to hotels table...")
            await conn.execute(text("ALTER TABLE hotels ADD COLUMN distance_km FLOAT"))
            logger.info("Migration complete: distance_km column added.")


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Starting Hotel Price Tracker...")
    await init_db()
    await run_migrations()
    start_scheduler()
    yield
    stop_scheduler()
    logger.info("Shutting down Hotel Price Tracker.")


app = FastAPI(
    title="Hotel Price Tracker",
    version=_get_project_version(),
    lifespan=lifespan,
)


# API routes
app.include_router(hotels.router)
app.include_router(prices.router)


@app.get("/api/version")
async def get_version():
    return {"version": _get_project_version()}


# Serve React frontend (built static files)
STATIC_DIR = Path(__file__).parent.parent / "static"

if STATIC_DIR.exists():
    app.mount("/assets", StaticFiles(directory=STATIC_DIR / "assets"), name="assets")

    @app.get("/{full_path:path}")
    async def serve_spa(full_path: str):
        """Serve the React SPA for any non-API route."""
        file_path = STATIC_DIR / full_path
        if full_path and file_path.exists() and file_path.is_file():
            return FileResponse(file_path)
        return FileResponse(STATIC_DIR / "index.html")
else:
    @app.get("/")
    async def root():
        return {"message": "Hotel Price Tracker API running. Frontend not built yet — run 'npm run build' in frontend/."}
