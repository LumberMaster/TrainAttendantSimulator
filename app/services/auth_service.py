from sqlalchemy.orm import Session
from fastapi import HTTPException
from app.models import User
from app.schemas import RegisterRequest


def register_user(db: Session, data: RegisterRequest) -> User:
    existing_user = db.query(User).filter(User.email == data.email).first()
    if existing_user:
        raise HTTPException(status_code=400, detail="Email already registered")

    new_user = User(
        email=data.email,
        password=data.password,
        first_name=data.first_name,
        second_name=data.second_name,
        third_name=data.third_name,
        exp=0,
        competension_score=0,
    )
    db.add(new_user)
    db.commit()
    db.refresh(new_user)
    return new_user


def login_user(db: Session, email: str, password: str) -> User:
    user = db.query(User).filter(User.email == email).first()
    if not user:
        raise HTTPException(status_code=401, detail="Invalid email or password")

    if user.password != password:
        raise HTTPException(status_code=401, detail="Invalid email or password")

    return user
