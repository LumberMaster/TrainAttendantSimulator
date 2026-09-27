from pydantic import BaseModel, EmailStr, Field
from typing import Optional, Any
from uuid import UUID



class RegisterRequest(BaseModel):
    email: EmailStr
    password: str = Field(..., min_length=1)
    first_name: str = Field(..., min_length=1)
    second_name: str = Field(..., min_length=1)
    third_name: Optional[str] = None


class LoginRequest(BaseModel):
    email: EmailStr
    password: str = Field(..., min_length=1)


class UserOut(BaseModel):
    user_uuid: str
    email: str
    first_name: str
    second_name: str
    third_name: Optional[str] = None
    exp: int = 0
    competension_score: int = 0

    class Config:
        from_attributes = True


class TokenResponse(BaseModel):

    access_token: str
    token_type: str = "bearer"
    expires_in: int
    user: UserOut



class ScenarioResponse(BaseModel):
    scenario_uuid: str
    name: str
    description: Optional[str] = None

    class Config:
        from_attributes = True


class ScenarioStartRequest(BaseModel):
    scenario_uuid: UUID


class ScenarioStartResponse(BaseModel):
    id: str


class ScenarioFinishRequest(BaseModel):
    result_id: UUID
    passenger_loyality: int = Field(..., ge=0)
    security_rating: int = Field(..., ge=0)
    duration_playtime: int = Field(..., ge=0)
    result_json: Optional[Any] = None


class ScenarioFinishResponse(BaseModel):
    id: str
    user_uuid: str
    scenario_uuid: str
    passenger_loyality: int
    security_rating: int
    time_end: Optional[str] = None
    duration_playtime: int
    result_json: Optional[Any] = None
    exp_earned: int = 0
    exp_total: int = 0


class LeaderboardRequest(BaseModel):
    scenario_uuid: UUID


class LeaderboardEntry(BaseModel):
    first_name: str
    second_name: str
    duration_playtime: int
    passenger_loyality: Optional[int] = None
    security_rating: Optional[int] = None
    time_end: Optional[str] = None


class ScenarioResultOut(BaseModel):

    id: str
    user_uuid: str
    scenario_uuid: str
    scenario_name: Optional[str] = None
    scenario_description: Optional[str] = None
    passenger_loyality: Optional[int] = None
    security_rating: Optional[int] = None
    time_end: Optional[str] = None
    duration_playtime: Optional[int] = None
    result_json: Optional[Any] = None


class ScenarioDetailOut(BaseModel):

    scenario_uuid: str
    name: str
    description: Optional[str] = None
    best_result: Optional[ScenarioResultOut] = None
    completed_attempts: int = 0
