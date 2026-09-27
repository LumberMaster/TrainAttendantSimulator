# Train Attendant Simulator — Backend

Бэкенд иммерсивного тренажёра проводников ВСМ. REST API на FastAPI + PostgreSQL.

## Структура

```
.
├── app/
│   ├── routers/            # auth, scenarios, admin (seed)
│   ├── services/           # бизнес-логика
│   ├── config.py           # настройки (читает переменные окружения / .env)
│   ├── database.py         # движок SQLAlchemy
│   ├── models.py           # таблицы User, Scenario, ScenarioResult
│   ├── schemas.py          # Pydantic-модели
│   └── security.py         # JWT
├── main.py                 # точка входа FastAPI
├── Dockerfile
├── docker-compose.yml
├── docker-entrypoint.sh
├── requirements.txt
└── .env.example
```

## Быстрый запуск (Docker, локальная машина)

Требуется: Docker Desktop (Docker Engine + Docker Compose).

```powershell
# 1. Создать локальный .env (скопировать пример)
Copy-Item .env.example .env

# 2. Собрать и поднять сервисы (api + postgres)
docker compose up -d --build

# 3. Проверить
docker compose ps
curl.exe http://localhost:8000/health
```

После старта:

- Swagger UI: http://localhost:8000/docs
- API: http://localhost:8000
- PostgreSQL: `localhost:5433` (пользователь/пароль/БД из `.env`). Хост-порт `5433`, чтобы не конфликтовать с локально установленным PostgreSQL на `5432`; внутри контейнера БД слушает `5432`.

Остановить: `docker compose down`. Пересобрать после изменения кода: `docker compose up -d --build`.
