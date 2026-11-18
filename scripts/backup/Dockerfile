# Nachtelijke database-backup: pg_dump -> (optioneel versleutelen) -> upload naar S3-compatibele
# opslag buiten de server (rclone). Zonder S3-configuratie vallen we terug op lokale dumps in het
# /backups-volume — beter dan niets, maar niet veilig bij servercrash (zie DEPLOY.md).
FROM alpine:3.20

RUN apk add --no-cache bash coreutils tzdata postgresql16-client rclone openssl curl

COPY backup.sh /usr/local/bin/backup.sh
COPY restore.sh /usr/local/bin/restore.sh
RUN chmod +x /usr/local/bin/backup.sh /usr/local/bin/restore.sh

ENV TZ=Europe/Amsterdam

CMD ["/usr/local/bin/backup.sh"]
