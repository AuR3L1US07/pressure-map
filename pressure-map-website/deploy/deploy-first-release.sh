#!/bin/sh
set -eu
release_id="$1"
case "$release_id" in *[!0-9A-Za-z-]*|'') echo 'Invalid release ID' >&2; exit 1;; esac
site_root=/opt/pressure-map-website
server_root=/opt/pressure-map-server
release_dir="$site_root/releases/$release_id"
backup_dir="$server_root/deploy/website-backup-$release_id"
cd "$server_root"
test ! -e compose.override.yaml
test ! -e "$site_root/current"
test ! -e "$release_dir"
mkdir -p "$release_dir" "$backup_dir"
cp deploy/Caddyfile "$backup_dir/Caddyfile"
cp compose.yaml "$backup_dir/compose.yaml"
tar -xzf /tmp/pressuremap-website.tar.gz -C "$release_dir"
test -s "$release_dir/index.html"
test -s "$release_dir/app.js"
ln -s "releases/$release_id" "$site_root/current"
rollback() {
    echo 'Deployment failed; restoring previous Caddy configuration.' >&2
    cp "$backup_dir/Caddyfile" deploy/Caddyfile
    if test -e compose.override.yaml; then mv compose.override.yaml "$backup_dir/failed-compose.override.yaml"; fi
    docker compose up -d --no-deps caddy
}
trap rollback EXIT
cp /tmp/pressuremap-Caddyfile.server deploy/Caddyfile
cp /tmp/pressuremap-compose.override.yaml compose.override.yaml
docker compose config --quiet
docker compose up -d --no-deps caddy
docker compose exec -T caddy caddy validate --config /etc/caddy/Caddyfile
# Retry only while Caddy finishes starting.
curl --retry 5 --retry-connrefused --retry-delay 1 --fail --silent --show-error http://127.0.0.1/ -o /tmp/pressuremap-home-check.html
grep -q 'id="hero-title"' /tmp/pressuremap-home-check.html
curl --fail --silent --show-error http://127.0.0.1/app.js -o /tmp/pressuremap-js-check.js
grep -q 'heroPhrases' /tmp/pressuremap-js-check.js
curl --retry 3 --retry-connrefused --retry-delay 1 --fail --silent --show-error --cacert deploy/certs/server.crt https://121.43.32.85/api/health
printf '\n'
trap - EXIT
printf 'Website URL: http://121.43.32.85/\nRelease: %s\nBackup: %s\n' "$release_dir" "$backup_dir"
docker compose ps
