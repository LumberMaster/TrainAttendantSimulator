from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session
from app.config import get_settings
from app.database import get_db
from app.models import User
from app.schemas import LoginRequest, RegisterRequest, TokenResponse
from app.security import create_access_token, get_current_user
from app.services import auth_service

router = APIRouter()
settings = get_settings()


def _build_token_response(user: User) -> TokenResponse:
    expires_in = settings.access_token_expire_minutes * 60
    return TokenResponse(
        access_token=create_access_token(str(user.user_uuid)),
        token_type="bearer",
        expires_in=expires_in,
        user={
            "user_uuid": str(user.user_uuid),
            "email": user.email,
            "first_name": user.first_name,
            "second_name": user.second_name,
            "third_name": user.third_name,
            "exp": user.exp or 0,
            "competension_score": user.competension_score or 0,
        },
    )


@router.post("/register", response_model=TokenResponse, status_code=201)
def register(data: RegisterRequest, db: Session = Depends(get_db)) -> TokenResponse:
    try:
        user = auth_service.register_user(db, data)
        return _build_token_response(user)
    except Exception as e:
        if hasattr(e, "status_code"):
            raise e
        raise Exception(f"Registration failed: {str(e)}")


@router.post("/login", response_model=TokenResponse)
def login(data: LoginRequest, db: Session = Depends(get_db)) -> TokenResponse:
    try:
        user = auth_service.login_user(db, data.email, data.password)
        return _build_token_response(user)
    except Exception as e:
        if hasattr(e, "status_code"):
            raise e
        raise Exception(f"Login failed: {str(e)}")


@router.get("/me", response_model=dict)
def me(current_user: User = Depends(get_current_user)):
    return {
        "user_uuid": str(current_user.user_uuid),
        "email": current_user.email,
        "first_name": current_user.first_name,
        "second_name": current_user.second_name,
        "third_name": current_user.third_name,
        "exp": current_user.exp or 0,
        "competension_score": current_user.competension_score or 0,
    }


@router.post("/logout")
def logout():
    return {"message": "Logged out"}
