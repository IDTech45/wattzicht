#!/usr/bin/env bash
# Lokale ontwikkeling: start alleen Postgres in Docker en draai de API met dotnet run.
# Gebruik:  ./scripts/dev-up.sh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

docker run -d --name wattzicht-postgres \
  -e POSTGRES_DB=wattzicht -e POSTGRES_USER=wattzicht -e POSTGRES_PASSWORD=wattzicht \
  -p 5432:5432 postgres:16 >/dev/null 2>&1 || docker start wattzicht-postgres >/dev/null

echo "Postgres draait op localhost:5432 (wattzicht/wattzicht)."
echo "API starten: dotnet run --project $ROOT_DIR/src/Wattzicht.Api  → http://localhost:8080/swagger"
echo "Inloggen demo: demo@wattzicht.nl / Demo1234!   installateur: installateur@triple-energie.nl / Installateur123!"
