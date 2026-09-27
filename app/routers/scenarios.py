from typing import List
from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session
from app.database import get_db
from app.models import User
from app.schemas import (
    ScenarioResponse,
    ScenarioStartRequest,
    ScenarioStartResponse,
    ScenarioFinishRequest,
    ScenarioFinishResponse,
    LeaderboardRequest,
    LeaderboardEntry,
    ScenarioResultOut,
    ScenarioDetailOut,
)
from app.services import scenario_service
from app.security import get_current_user

router = APIRouter()


def _result_to_dict(r) -> dict:
    d = r.to_dict()
    d["scenario_name"] = r.scenario.name if r.scenario else None
    d["scenario_description"] = r.scenario.description if r.scenario else None
    return d


@router.get("", response_model=List[ScenarioResponse])
@router.get("/", response_model=List[ScenarioResponse], include_in_schema=False)
def get_scenarios(
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    scenarios = scenario_service.get_all_scenarios(db)
    return [s.to_dict() for s in scenarios]


@router.post("/start", response_model=ScenarioStartResponse, status_code=201)
def start_scenario(
    data: ScenarioStartRequest,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    result = scenario_service.start_scenario(
        db, str(current_user.user_uuid), str(data.scenario_uuid)
    )
    return {"id": str(result.id)}


@router.post("/finish", response_model=ScenarioFinishResponse)
def finish_scenario(
    data: ScenarioFinishRequest,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    result = scenario_service.finish_scenario(db, str(current_user.user_uuid), data)
    return result.to_dict()


@router.post("/leaderboard", response_model=List[LeaderboardEntry])
def get_leaderboard(
    data: LeaderboardRequest,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    leaderboard = scenario_service.get_leaderboard(db, str(data.scenario_uuid))
    return leaderboard


@router.get("/results", response_model=List[ScenarioResultOut])
def get_my_results(
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    results = scenario_service.get_user_results(db, str(current_user.user_uuid))
    return [_result_to_dict(r) for r in results]


@router.get("/results/{user_uuid}", response_model=List[ScenarioResultOut])
def get_user_results_by_uuid(
    user_uuid: str,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    results = scenario_service.get_user_results(db, user_uuid)
    return [_result_to_dict(r) for r in results]


@router.get("/{scenario_uuid}", response_model=ScenarioDetailOut)
def get_scenario(
    scenario_uuid: str,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    return scenario_service.get_scenario_detail(
        db, scenario_uuid, str(current_user.user_uuid)
    )
