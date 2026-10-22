#!/bin/sh
# 每日备份：从容器内导出一致性副本，拷到宿主机目录，保留 14 天。
# 由 root 的 crontab 每天 03:30 调用；也可手动执行。
set -eu
cd /opt/pressure-map-server
STAMP=$(date +%F)
DEST=/opt/pressure-map-backups
mkdir -p "$DEST"
docker compose exec -T api dotnet PressureMap.Server.dll backup --output "/data/backup-$STAMP.db"
docker compose cp "api:/data/backup-$STAMP.db" "$DEST/backup-$STAMP.db" >/dev/null
docker compose exec -T api rm -f "/data/backup-$STAMP.db"
find "$DEST" -name 'backup-*.db' -mtime +14 -delete
echo "[$(date '+%F %T')] ok $DEST/backup-$STAMP.db $(du -h "$DEST/backup-$STAMP.db" | cut -f1)"
