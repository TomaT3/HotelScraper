from datetime import date, datetime
from sqlalchemy import Boolean, Date, DateTime, Float, Integer, String, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column
from app.database import Base


class Hotel(Base):
    __tablename__ = "hotels"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    booking_id: Mapped[str] = mapped_column(String, index=True)
    name: Mapped[str] = mapped_column(String)
    address: Mapped[str | None] = mapped_column(String, nullable=True)
    stars: Mapped[int | None] = mapped_column(Integer, nullable=True)
    review_score: Mapped[float | None] = mapped_column(Float, nullable=True)
    image_url: Mapped[str | None] = mapped_column(String, nullable=True)
    active: Mapped[bool] = mapped_column(Boolean, default=True)
    city: Mapped[str] = mapped_column(String, index=True, default="")

    __table_args__ = (
        UniqueConstraint("booking_id", "city", name="uq_booking_city"),
    )


class Price(Base):
    __tablename__ = "prices"
    __table_args__ = (
        UniqueConstraint("hotel_id", "date", name="uq_hotel_date"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    hotel_id: Mapped[int] = mapped_column(Integer, index=True)
    date: Mapped[date] = mapped_column(Date, index=True)
    price_eur: Mapped[float] = mapped_column(Float)
    fetched_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)


class Setting(Base):
    __tablename__ = "settings"

    key: Mapped[str] = mapped_column(String, primary_key=True)
    value: Mapped[str] = mapped_column(String)
