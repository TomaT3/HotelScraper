from pydantic_settings import BaseSettings
from pathlib import Path


class Settings(BaseSettings):
    rapidapi_key: str = ""
    database_url: str = "sqlite+aiosqlite:///./data/hotel_prices.db"
    dates_per_run: int = 15
    fetch_hour: int = 3
    search_city: str = "Stuttgart"

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()


def get_data_dir() -> Path:
    """Ensure data directory exists and return its path."""
    db_path = settings.database_url.replace("sqlite+aiosqlite:///", "")
    data_dir = Path(db_path).parent
    data_dir.mkdir(parents=True, exist_ok=True)
    return data_dir
