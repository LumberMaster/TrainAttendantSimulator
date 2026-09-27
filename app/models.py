import uuid
from datetime import datetime
from sqlalchemy import Column, String, Integer, Text, DateTime, ForeignKey, JSON, text
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import relationship
from app.database import Base

_UUID_DEFAULT = text("gen_random_uuid()")


class User(Base):
    __tablename__ = "users"

    user_uuid = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4, server_default=_UUID_DEFAULT)
    email = Column(String, unique=True, nullable=False, index=True)
    password = Column(String, nullable=False)
    first_name = Column(String, nullable=False)
    second_name = Column(String, nullable=False)
    third_name = Column(String, nullable=True)
    exp = Column(Integer, default=0)
    competension_score = Column(Integer, default=0)

    results = relationship("ScenarioResult", back_populates="user")

    def to_dict(self):
        return {
            "user_uuid": str(self.user_uuid),
            "email": self.email,
            "first_name": self.first_name,
            "second_name": self.second_name,
            "third_name": self.third_name,
            "exp": self.exp,
            "competension_score": self.competension_score,
        }


class Scenario(Base):
    __tablename__ = "scenarios"

    scenario_uuid = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4, server_default=_UUID_DEFAULT)
    name = Column(String, nullable=False)
    description = Column(Text, nullable=True)

    results = relationship("ScenarioResult", back_populates="scenario")

    def to_dict(self):
        return {
            "scenario_uuid": str(self.scenario_uuid),
            "name": self.name,
            "description": self.description,
        }


class ScenarioResult(Base):
    __tablename__ = "scenarios_results"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4, server_default=_UUID_DEFAULT)
    user_uuid = Column(UUID(as_uuid=True), ForeignKey("users.user_uuid"), nullable=False)
    scenario_uuid = Column(UUID(as_uuid=True), ForeignKey("scenarios.scenario_uuid"), nullable=False)
    passenger_loyality = Column(Integer, nullable=True)
    security_rating = Column(Integer, nullable=True)
    time_end = Column(DateTime, nullable=True)
    duration_playtime = Column(Integer, nullable=True)
    result_json = Column(JSON, nullable=True)

    user = relationship("User", back_populates="results")
    scenario = relationship("Scenario", back_populates="results")

    def to_dict(self):
        return {
            "id": str(self.id),
            "user_uuid": str(self.user_uuid),
            "scenario_uuid": str(self.scenario_uuid),
            "passenger_loyality": self.passenger_loyality,
            "security_rating": self.security_rating,
            "time_end": self.time_end.isoformat() if self.time_end else None,
            "duration_playtime": self.duration_playtime,
            "result_json": self.result_json,
            "exp_earned": getattr(self, "exp_earned", 0),
            "exp_total": getattr(self, "exp_total", 0),
        }
