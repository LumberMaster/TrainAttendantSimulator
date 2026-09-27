import uuid
from datetime import datetime
from typing import List, Dict, Any
from sqlalchemy.orm import Session, joinedload
from sqlalchemy import asc
from fastapi import HTTPException
from app.config import get_settings
from app.models import Scenario, ScenarioResult, User
from app.schemas import ScenarioFinishRequest

settings = get_settings()


def get_all_scenarios(db: Session) -> List[Scenario]:
    return db.query(Scenario).all()


def get_scenario(db: Session, scenario_uuid: str) -> Scenario:
    scenario_pk = _to_uuid(scenario_uuid)
    if scenario_pk is None:
        raise HTTPException(status_code=404, detail="Scenario not found")
    scenario = db.query(Scenario).filter(Scenario.scenario_uuid == scenario_pk).first()
    if not scenario:
        raise HTTPException(status_code=404, detail="Scenario not found")
    return scenario


def get_scenario_detail(db: Session, scenario_uuid: str, user_uuid: str) -> Dict[str, Any]:
    scenario = get_scenario(db, scenario_uuid)

    user_pk = _to_uuid(user_uuid)
    if user_pk is None:
        raise HTTPException(status_code=404, detail="User not found")

    completed = (
        db.query(ScenarioResult)
        .filter(
            ScenarioResult.scenario_uuid == scenario.scenario_uuid,
            ScenarioResult.user_uuid == user_pk,
            ScenarioResult.time_end.isnot(None),
        )
        .order_by(
            ScenarioResult.security_rating.desc().nulls_last(),
            ScenarioResult.passenger_loyality.desc().nulls_last(),
        )
        .all()
    )

    best_dict = None
    if completed:
        best_dict = completed[0].to_dict()
        best_dict["scenario_name"] = scenario.name
        best_dict["scenario_description"] = scenario.description

    return {
        "scenario_uuid": str(scenario.scenario_uuid),
        "name": scenario.name,
        "description": scenario.description,
        "best_result": best_dict,
        "completed_attempts": len(completed),
    }


def start_scenario(db: Session, user_uuid: str, scenario_uuid: str) -> ScenarioResult:
    scenario = db.query(Scenario).filter(Scenario.scenario_uuid == scenario_uuid).first()
    if not scenario:
        raise HTTPException(status_code=404, detail="Scenario not found")

    result = ScenarioResult(
        user_uuid=user_uuid,
        scenario_uuid=scenario_uuid,
        passenger_loyality=None,
        security_rating=None,
        time_end=None,
        duration_playtime=None,
        result_json=None,
    )
    db.add(result)
    db.commit()
    db.refresh(result)
    return result


def finish_scenario(db: Session, user_uuid: str, data: ScenarioFinishRequest) -> ScenarioResult:
    result = db.query(ScenarioResult).filter(ScenarioResult.id == data.result_id).first()
    if not result:
        raise HTTPException(status_code=404, detail="Result not found")

    if str(result.user_uuid) != user_uuid:
        raise HTTPException(status_code=403, detail="Access denied: result does not belong to current user")

    if result.time_end is not None:
        raise HTTPException(status_code=409, detail="Scenario already finished")

    result.passenger_loyality = data.passenger_loyality
    result.security_rating = data.security_rating
    result.duration_playtime = data.duration_playtime
    result.result_json = data.result_json
    result.time_end = datetime.utcnow()

    user = db.query(User).filter(User.user_uuid == result.user_uuid).first()
    exp_earned = settings.exp_base_complete + data.security_rating + data.passenger_loyality
    if user:
        user.exp = (user.exp or 0) + exp_earned

    db.commit()
    db.refresh(result)

    result.exp_earned = exp_earned
    result.exp_total = user.exp if user else 0
    return result


def get_leaderboard(db: Session, scenario_uuid: str, limit: int = 10) -> List[Dict[str, Any]]:
    scenario = db.query(Scenario).filter(Scenario.scenario_uuid == scenario_uuid).first()
    if not scenario:
        raise HTTPException(status_code=404, detail="Scenario not found")

    results = (
        db.query(ScenarioResult, User)
        .join(User, ScenarioResult.user_uuid == User.user_uuid)
        .filter(
            ScenarioResult.scenario_uuid == scenario_uuid,
            ScenarioResult.time_end.isnot(None),
            ScenarioResult.duration_playtime.isnot(None),
        )
        .order_by(asc(ScenarioResult.duration_playtime))
        .limit(limit)
        .all()
    )

    leaderboard = []
    for result, user in results:
        leaderboard.append({
            "first_name": user.first_name,
            "second_name": user.second_name,
            "duration_playtime": result.duration_playtime,
            "passenger_loyality": result.passenger_loyality,
            "security_rating": result.security_rating,
            "time_end": result.time_end.isoformat() if result.time_end else None,
        })

    return leaderboard


def get_user_results(db: Session, user_uuid: str) -> List[ScenarioResult]:
    user_pk = _to_uuid(user_uuid)
    if user_pk is None:
        raise HTTPException(status_code=404, detail="User not found")

    return (
        db.query(ScenarioResult)
        .options(joinedload(ScenarioResult.scenario))
        .filter(ScenarioResult.user_uuid == user_pk)
        .order_by(ScenarioResult.time_end.desc().nulls_last())
        .all()
    )


def _to_uuid(value: str):
    try:
        return uuid.UUID(value)
    except (ValueError, TypeError):
        return None
