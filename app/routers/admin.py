from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session
from app.database import get_db
from app.models import Scenario

router = APIRouter(prefix="/api/v1/admin", tags=["Admin"])


@router.post("/seed")
def seed_data(db: Session = Depends(get_db)):
    try:
        existing = db.query(Scenario).count()
        if existing > 0:
            return {"message": f"Seed data already exists ({existing} scenarios)"}

        scenarios = [
            Scenario(name="Train Walkthrough", description="Standard train car walkthrough scenario"),
            Scenario(name="Emergency Evacuation", description="Emergency evacuation procedure training"),
            Scenario(name="Passenger Service", description="Customer service and passenger interaction"),
        ]

        db.add_all(scenarios)
        db.commit()

        for s in scenarios:
            db.refresh(s)

        return {
            "message": "Seed data created successfully",
            "scenarios": [s.to_dict() for s in scenarios]
        }
    except Exception as e:
        db.rollback()
        raise HTTPException(status_code=500, detail=str(e))
