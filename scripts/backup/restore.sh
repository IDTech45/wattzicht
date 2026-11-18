#!/bin/bash
# Herstelt een backup in de database. Draai dit IN de backup-container:
#
#   # lijst beschikbare backups (remote):
#   docker exec wattzicht-backup rclone lsl "s3:<bucket/prefix>"
#   # download er één naar /backups:
#   docker exec wattzicht-backup rclone copyto "s3:<bucket/prefix>/<bestand>" /backups/<bestand>
#   # herstel (LET OP: overschrijft de huidige database-inhoud):
#   docker exec wattzicht-backup restore.sh /backups/<bestand>
#
# Restore-TEST zonder productie te raken: herstel naar een aparte database:
#   docker exec wattzicht-backup restore.sh /backups/<bestand> wattzicht_restoretest
set -euo pipefail

FILE="${1:?gebruik: restore.sh /backups/<bestand> [doeldatabase]}"
TARGET_DB="${2:-$PGDATABASE}"

[ -f "$FILE" ] || { echo "FOUT: $FILE bestaat niet."; exit 1; }

SRC="$FILE"
if [[ "$FILE" == *.enc ]]; then
  : "${BACKUP_PASSPHRASE:?FOUT: dit bestand is versleuteld maar BACKUP_PASSPHRASE is niet gezet}"
  SRC="${FILE%.enc}.decrypted"
  openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 \
    -pass env:BACKUP_PASSPHRASE -in "$FILE" -out "$SRC"
  trap 'rm -f "$SRC"' EXIT
fi

if [ "$TARGET_DB" != "$PGDATABASE" ]; then
  echo "Doeldatabase '$TARGET_DB' aanmaken (als die nog niet bestaat)…"
  createdb --no-password "$TARGET_DB" 2>/dev/null || true
fi

echo "Herstellen van $(basename "$FILE") naar database '$TARGET_DB'…"
pg_restore --no-password --clean --if-exists --no-owner -d "$TARGET_DB" "$SRC"
echo "✅ Klaar. Controleer met bijv.:  psql -d $TARGET_DB -c 'select count(*) from \"Customers\";'"
