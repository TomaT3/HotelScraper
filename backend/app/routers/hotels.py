from fastapi import APIRouter, Depends, Query
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.models import Hotel, Setting
from app.schemas import HotelOut, HotelUpdate, CityOut
from app.config import settings

router = APIRouter(prefix="/api", tags=["hotels"])


@router.get("/cities", response_model=list[CityOut])
async def list_cities(db: AsyncSession = Depends(get_db)):
    """Return all configured cities with their dest_labels."""
    result = []
    for city in settings.city_list:
        label_result = await db.execute(
            select(Setting).where(Setting.key == f"dest_label:{city}")
        )
        label_setting = label_result.scalar_one_or_none()
        result.append(CityOut(
            name=city,
            dest_label=label_setting.value if label_setting else None,
        ))
    return result


@router.get("/hotels", response_model=list[HotelOut])
async def list_hotels(
    city: str = Query(..., description="City to filter hotels by"),
    db: AsyncSession = Depends(get_db),
):
    result = await db.execute(
        select(Hotel).where(Hotel.city == city).order_by(Hotel.name)
    )
    return result.scalars().all()


@router.patch("/hotels/{hotel_id}", response_model=HotelOut)
async def update_hotel(hotel_id: int, body: HotelUpdate, db: AsyncSession = Depends(get_db)):
    result = await db.execute(select(Hotel).where(Hotel.id == hotel_id))
    hotel = result.scalar_one_or_none()
    if not hotel:
        from fastapi import HTTPException
        raise HTTPException(status_code=404, detail="Hotel not found")

    if body.active is not None:
        hotel.active = body.active

    await db.commit()
    await db.refresh(hotel)
    return hotel
