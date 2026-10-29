#!/usr/bin/env bash
# Maakt .env.production met sterke, willekeurige geheimen voor Wattzicht.
# Gebruik:  ./scripts/generate-secrets.sh [pad/naar/.env.production]
# Bestaande bestanden worden niet overschreven (verwijder ze eerst bewust).

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT_DIR/.env.production}"

if [[ -e "$OUT" ]]; then
  echo "Bestand bestaat al: $OUT" >&2
  echo "Verwijder het eerst als je echt nieuwe geheimen wilt (let op: bestaande versleutelde credentials worden dan onleesbaar)." >&2
  exit 1
fi

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is nodig maar niet gevonden." >&2
  exit 1
fi

# Alleen URL-veilige tekens zodat de waarden zonder quoting in compose en connection strings passen.
rand_alnum() {
  openssl rand -base64 "$1" | tr -dc 'A-Za-z0-9' | head -c "$2"
}

POSTGRES_PASSWORD="$(rand_alnum 48 40)"
JWT_SECRET="$(rand_alnum 64 64)"
# Data Protection hoofdsleutel: 32 bytes als base64 (de API herkent dit formaat).
WZ_PROTECTION_KEY="$(openssl rand -base64 32)"

umask 077
cat > "$OUT" <<EOF
# Gegenereerd door scripts/generate-secrets.sh op $(date -u +%Y-%m-%dT%H:%M:%SZ)
# NIET committen. Bewaar WZ_PROTECTION_KEY veilig: zonder deze sleutel zijn apparaat-credentials onleesbaar.

POSTGRES_DB=wattzicht
POSTGRES_USER=wattzicht
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}

JWT_SECRET=${JWT_SECRET}
WZ_PROTECTION_KEY=${WZ_PROTECTION_KEY}

CORS_ORIGIN_0=https://wattzicht.nl
CORS_ORIGIN_1=https://www.wattzicht.nl

SEED_ENABLED=true
SEED_INSTALLER_PASSWORD=$(rand_alnum 24 16)
SEED_DEMO_PASSWORD=$(rand_alnum 24 12)
SWAGGER_ENABLED=false
EOF

echo "Geheimen weggeschreven naar: $OUT"
echo "Gebruik in Dokploy: docker compose --env-file $OUT up -d --build"
