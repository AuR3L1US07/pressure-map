#!/bin/sh
set -eu
release_id="$1"
case "$release_id" in *[!0-9A-Za-z-]*|'') exit 1;; esac
site_root=/opt/pressure-map-website
release_dir="$site_root/releases/$release_id"
test -L "$site_root/current"
test ! -e "$release_dir"
previous=$(readlink "$site_root/current")
mkdir -p "$release_dir"
tar -xzf /tmp/pressuremap-website-v3.tar.gz -C "$release_dir"
test -s "$release_dir/assets/fonts/pressuremap-headline.woff2"
ln -s "releases/$release_id" "$site_root/current.next"
mv -Tf "$site_root/current.next" "$site_root/current"
rollback() {
  ln -s "$previous" "$site_root/current.rollback"
  mv -Tf "$site_root/current.rollback" "$site_root/current"
}
trap rollback EXIT
curl --fail --silent --show-error http://127.0.0.1/ -o /tmp/pressuremap-home-v2.html
grep -q 'style.css?v=20261019-3' /tmp/pressuremap-home-v2.html
if grep -q 'hero-pause\|class="release"' /tmp/pressuremap-home-v2.html; then exit 1; fi
curl --fail --silent --show-error http://127.0.0.1/app.js -o /tmp/pressuremap-script-v2.js
grep -q 'setTimeout(advanceHero,3400)' /tmp/pressuremap-script-v2.js
curl --fail --silent --show-error http://127.0.0.1/assets/fonts/pressuremap-headline.woff2 -o /tmp/pressuremap-font-v2.woff2
sha256sum /tmp/pressuremap-font-v2.woff2
curl --fail --silent --show-error --cacert /opt/pressure-map-server/deploy/certs/server.crt https://121.43.32.85/api/health
printf '\nRelease: %s\nPrevious: %s\n' "$release_id" "$previous"
trap - EXIT
