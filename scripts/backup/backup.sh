#!/bin/bash
# Nachtelijke Postgres-backup met off-server upload.
#
# Gedrag:
#   - Bij start (BACKUP_ON_START=true, default): direct één backup draaien — zo zie je meteen in de
#     logs of alles werkt, en heeft een verse deploy altijd een recent herstelpunt.
#   - Daarna: elke nacht om BACKUP_TIME (default 03:30, Europe/Amsterdam).
#   - pg_dump -Fc (gecomprimeerd, geschikt voor pg_restore), optioneel symmetrisch versleuteld
#     met BACKUP_PASSPHRASE (aes-256, openssl).
#   - Upload naar S3-compatibele opslag via rclone (remote "s3", geconfigureerd via
#     RCLONE_CONFIG_S3_*-omgevingsvariabelen in docker-compose.yml) + retentie-opruiming.
#   - Zonder BACKUP_S3_PATH: alleen lokaal in /backups (met eigen retentie) + duidelijke waarschuwing.
#   - Optioneel: BACKUP_HEARTBEAT_URL wordt na een geslaagde run geping'd (healthchecks.io-stijl),
#     zodat je een alarm krijgt als de backup juist NIET meer draait.
set -u

BACKUP_TIME="${BACKUP_TIME:-03:30}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
LOCAL_RETENTION_DAYS="${BACKUP_LOCAL_RETENTION_DAYS:-3}"
S3_PATH="${BACKUP_S3_PATH:-}"          # bijv. "wattzicht-backups/db" (bucket/prefix, zonder s3:)
LOCAL_DIR=/backups

log() { echo "[backup] $(date '+%Y-%m-%d %H:%M:%S') $*"; }

do_backup() {
  local ts file
  ts=$(date +%Y%m%d-%H%M%S)
  file="$LOCAL_DIR/${PGDATABASE}-${ts}.dump"

  log "pg_dump starten (database=${PGDATABASE}, host=${PGHOST})…"
  if ! pg_dump -Fc --no-password -f "${file}.part"; then
    rm -f "${file}.part"
    log "FOUT: pg_dump mislukt — geen backup gemaakt."
    return 1
  fi
  mv "${file}.part" "$file"

  if [ -n "${BACKUP_PASSPHRASE:-}" ]; then
    if openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt \
        -pass env:BACKUP_PASSPHRASE -in "$file" -out "${file}.enc"; then
      rm -f "$file"
      file="${file}.enc"
    else
      log "FOUT: versleutelen mislukt — onversleutelde dump blijft lokaal staan, NIET geüpload."
      return 1
    fi
  fi
  log "dump klaar: $(basename "$file") ($(du -h "$file" | cut -f1))"

  if [ -n "$S3_PATH" ]; then
    if rclone copyto "$file" "s3:${S3_PATH}/$(basename "$file")" --s3-no-check-bucket; then
      log "geüpload naar s3:${S3_PATH}/$(basename "$file")"
      # Retentie: verwijder remote backups ouder dan RETENTION_DAYS.
      rclone delete --min-age "${RETENTION_DAYS}d" "s3:${S3_PATH}" \
        && log "remote retentie toegepast (> ${RETENTION_DAYS} dagen verwijderd)" \
        || log "WAARSCHUWING: remote retentie-opruiming mislukt (upload zelf is gelukt)."
    else
      log "FOUT: upload naar s3:${S3_PATH} mislukt — dump staat nog lokaal in ${LOCAL_DIR}."
      return 1
    fi
  else
    log "WAARSCHUWING: BACKUP_S3_PATH is leeg — backup staat ALLEEN lokaal op deze server."
    log "WAARSCHUWING: bij een servercrash/hack ben je deze backups ook kwijt. Zie DEPLOY.md → Back-ups."
  fi

  # Lokale retentie: houd de laatste dagen als snelle herstelpunten, ruim de rest op.
  find "$LOCAL_DIR" -maxdepth 1 -name "*.dump" -o -name "*.dump.enc" | while read -r f; do
    if [ -n "$(find "$f" -mtime +"$LOCAL_RETENTION_DAYS" 2>/dev/null)" ]; then rm -f "$f"; fi
  done

  if [ -n "${BACKUP_HEARTBEAT_URL:-}" ]; then
    curl -fsS -m 10 --retry 3 "$BACKUP_HEARTBEAT_URL" >/dev/null \
      && log "heartbeat geping'd" \
      || log "WAARSCHUWING: heartbeat-ping mislukt."
  fi
  return 0
}

mkdir -p "$LOCAL_DIR"
log "backup-service gestart — schema: dagelijks ${BACKUP_TIME} (${TZ:-UTC}), remote: ${S3_PATH:-'(geen — alleen lokaal)'}"

if [ "${BACKUP_ON_START:-true}" = "true" ]; then
  do_backup || true
fi

while true; do
  now=$(date +%s)
  target=$(date -d "today ${BACKUP_TIME}" +%s)
  [ "$target" -le "$now" ] && target=$(date -d "tomorrow ${BACKUP_TIME}" +%s)
  log "volgende backup: $(date -d "@$target" '+%Y-%m-%d %H:%M')"
  sleep $(( target - now ))
  do_backup || true
done
