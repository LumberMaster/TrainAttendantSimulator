from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from app.database import engine, Base
from app.config import get_settings
from app.routers import auth, scenarios, admin

settings = get_settings()

app = FastAPI(title="Game Platform API", version="1.0.0")


def _split_csv(value: str) -> list[str]:
    return [item.strip() for item in value.split(",") if item.strip()]


cors_origins = _split_csv(settings.cors_allow_origins)
allow_origin_regex = settings.cors_origin_regex or None

app.add_middleware(
    CORSMiddleware,
    allow_origins=cors_origins,
    allow_origin_regex=allow_origin_regex,
    allow_credentials=(cors_origins != ["*"]),
    allow_methods=_split_csv(settings.cors_allow_methods),
    allow_headers=_split_csv(settings.cors_allow_headers),
    max_age=600,
)

app.include_router(auth.router, prefix="/api/v1/auth", tags=["Authentication"])
app.include_router(scenarios.router, prefix="/api/v1/scenarios", tags=["Scenarios"])
app.include_router(admin.router)


@app.on_event("startup")
def startup():
    Base.metadata.create_all(bind=engine)


@app.get("/")
def root():
    return {
        "message": "Game Platform API is running",
        "version": "v1",
        "docs": "/docs",
        "endpoints": {
            "auth": "/api/v1/auth",
            "scenarios": "/api/v1/scenarios",
            "admin": "/api/v1/admin",
        },
    }


@app.get("/health")
def health():
    return {"status": "ok"}


@app.options("/api/v1/{path:path}")
@app.options("/api/v1")
def cors_preflight_placeholder():
    return {"status": "ok"}
