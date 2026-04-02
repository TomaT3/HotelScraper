from fastapi import APIRouter, Depends
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.models import Hotel
from app.schemas import HotelOut, HotelUpdate

router = APIRouter(prefix="/api/hotels", tags=["hotels"])


@router.get("", response_model=list[HotelOut])
async def list_hotels(db: AsyncSession = Depends(get_db)):
    result = await db.execute(select(Hotel).order_by(Hotel.name))
    return result.scalars().all()


@router.patch("/{hotel_id}", response_model=HotelOut)
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
