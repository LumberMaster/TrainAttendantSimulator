from pydantic_settings import BaseSettings
from functools import lru_cache


class Settings(BaseSettings):
    database_url: str = "postgresql://postgres:postgres@localhost:5432/game_platform"
    secret_key: str = "devsecretkey"

    access_token_expire_minutes: int = 720
    jwt_algorithm: str = "HS256"

    cors_allow_origins: str = "*"
    cors_origin_regex: str = ""
    cors_allow_methods: str = "GET,POST,OPTIONS"
    cors_allow_headers: str = "Content-Type,Authorization"

    exp_base_complete: int = 50

    class Config:
        env_file = ".env"
        env_file_encoding = "utf-8"


@lru_cache()
def get_settings() -> Settings:
    return Settings()
