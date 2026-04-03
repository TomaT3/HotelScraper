from pydantic import field_validator
from pydantic_settings import BaseSettings
from pathlib import Path


class Settings(BaseSettings):
    rapidapi_key: str = ""
    database_url: str = "sqlite+aiosqlite:///./data/hotel_prices.db"
    dates_per_run: int = 15
    fetch_hour: int = 3
    search_cities: str = "Stuttgart"
    # Fallback: old single-city env var
    search_city: str = ""

    @field_validator("search_cities", mode="before")
    @classmethod
    def _parse_cities(cls, v: str) -> str:
        # Keep raw string; parsed via property
        return v

    @property
    def city_list(self) -> list[str]:
        """Return list of cities from comma-separated SEARCH_CITIES (or fallback to SEARCH_CITY)."""
        raw = self.search_cities
        cities = [c.strip() for c in raw.split(",") if c.strip()]
        if not cities and self.search_city:
            cities = [self.search_city.strip()]
        return cities if cities else ["Stuttgart"]

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()


def get_data_dir() -> Path:
    """Ensure data directory exists and return its path."""
    db_path = settings.database_url.replace("sqlite+aiosqlite:///", "")
    data_dir = Path(db_path).parent
    data_dir.mkdir(parents=True, exist_ok=True)
    return data_dir
