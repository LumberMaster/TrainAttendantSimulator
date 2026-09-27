#!/bin/bash
set -e

echo "Waiting for PostgreSQL at ${DB_HOST}:${DB_PORT}..."
until nc -z "$DB_HOST" "$DB_PORT"; do
  sleep 1
done
echo "PostgreSQL is ready!"

python -c "
from app.database import engine, Base
from app.models import User, Scenario, ScenarioResult
Base.metadata.create_all(bind=engine)
print('Tables ensured.')
"

echo "Starting FastAPI server..."
exec uvicorn main:app --host 0.0.0.0 --port 8000