import logging
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from fastapi.responses import FileResponse

from app.database import init_db
from app.routers import hotels, prices
from app.services.scheduler import start_scheduler, stop_scheduler

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Starting Hotel Price Tracker...")
    await init_db()
    start_scheduler()
    yield
    stop_scheduler()
    logger.info("Shutting down Hotel Price Tracker.")


app = FastAPI(
    title="Stuttgart Hotel Price Tracker",
    version="1.0.0",
    lifespan=lifespan,
)

# API routes
app.include_router(hotels.router)
app.include_router(prices.router)

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
