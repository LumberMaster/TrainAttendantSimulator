import logging
import uuid
from datetime import datetime, timedelta, timezone

import jwt
from fastapi import Depends, HTTPException, Request
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from sqlalchemy.orm import Session

from app.config import get_settings
from app.database import get_db
from app.models import User

settings = get_settings()
logger = logging.getLogger(__name__)

_bearer = HTTPBearer(auto_error=False)


def create_access_token(user_uuid: str) -> str:
    now = datetime.now(timezone.utc)
    payload = {
        "sub": str(user_uuid),
        "iat": now,
        "exp": now + timedelta(minutes=settings.access_token_expire_minutes),
    }
    return jwt.encode(payload, settings.secret_key, algorithm=settings.jwt_algorithm)


def decode_access_token(token: str) -> str:
    try:
        payload = jwt.decode(
            token,
            settings.secret_key,
            algorithms=[settings.jwt_algorithm],
        )
    except jwt.ExpiredSignatureError:
        raise HTTPException(status_code=401, detail="Token expired")
    except jwt.InvalidTokenError as exc:
        raise HTTPException(status_code=401, detail="Invalid or malformed token") from exc

    user_uuid = payload.get("sub")
    if not user_uuid:
        raise HTTPException(status_code=401, detail="Token missing subject")
    return str(user_uuid)


def get_current_user(
    credentials: HTTPAuthorizationCredentials | None = Depends(_bearer),
    db: Session = Depends(get_db),
) -> User:
    if credentials is None or not credentials.credentials:
        logger.warning("401: отсутствует Authorization: Bearer <token>")
        raise HTTPException(status_code=401, detail="Not authenticated")

    token = credentials.credentials
    user_uuid = decode_access_token(token)

    try:
        user_pk = uuid.UUID(user_uuid)
    except ValueError:
        logger.warning("401: некорректный user_uuid в токене: %r", user_uuid)
        raise HTTPException(status_code=401, detail="Invalid token subject")

    user = db.query(User).filter(User.user_uuid == user_pk).first()
    if not user:
        logger.warning("401: пользователь %s не найден в БД", user_uuid)
        raise HTTPException(status_code=401, detail="User not found")

    return user
