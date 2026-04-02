from datetime import date, datetime
from pydantic import BaseModel


class HotelOut(BaseModel):
    id: int
    booking_id: str
    name: str
    address: str | None = None
    stars: int | None = None
    review_score: float | None = None
    image_url: str | None = None
    active: bool

    model_config = {"from_attributes": True}


class HotelUpdate(BaseModel):
    active: bool | None = None


class PricePoint(BaseModel):
    date: date
    price_eur: float


class HotelPrices(BaseModel):
    hotel_id: int
    hotel_name: str
    stars: int | None = None
    prices: list[PricePoint]


class StatusOut(BaseModel):
    total_hotels: int
    active_hotels: int
    total_prices: int
    dates_covered: int
    dates_total: int
    coverage_pct: float
    last_fetch: datetime | None = None
    scheduler_running: bool
    next_run: datetime | None = None


class FetchResult(BaseModel):
    dates_fetched: int
    hotels_found: int
    prices_saved: int
    errors: list[str]
