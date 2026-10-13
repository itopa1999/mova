#!/bin/sh
set -eu

if [ -z "${ConnectionStrings__Postgres:-}" ]; then
    echo "ERROR: ConnectionStrings__Postgres is not configured."
    exit 1
fi

echo "Applying pending MOVA database migrations..."

/app/migrate --connection "$ConnectionStrings__Postgres"

echo "Database migrations completed successfully."
echo "Starting MOVA API..."

exec dotnet Mova.Api.dll
