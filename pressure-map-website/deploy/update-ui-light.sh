#!/bin/sh
set -eu
release_id=20261020-website-ui-light-v1
site_root=/opt/pressure-map-website
release_dir="$site_root/releases/$release_id"
archive=/tmp/pressuremap-20261020-ui-light.tar.gz
test -L "$site_root/current"
test ! -e "$release_dir"
test ! -e "$site_root/current.next"
previous=$(readlink "$site_root/current")
mkdir "$release_dir"
tar -xzf "$archive" -C "$release_dir"
(cd "$release_dir" && sha256sum -c manifest.sha256)
grep -q 'style.css?v=20261020-2' "$release_dir/index.html"
if grep -q prefers-color-scheme "$release_dir/index.html"; then exit 1; fi
test -s "$release_dir/assets/today-dashboard-v2.webp"
rollback() {
  ln -s "$previous" "$site_root/current.rollback"
  mv -Tf "$site_root/current.rollback" "$site_root/current"
}
ln -s "releases/$release_id" "$site_root/current.next"
trap rollback EXIT
mv -Tf "$site_root/current.next" "$site_root/current"
for asset in index.html app.js style.css assets/today-dashboard-v2.webp assets/course-calendar-v2.webp assets/fonts/pressuremap-headline.woff2; do
  url="$asset"
  if [ "$asset" = index.html ]; then url=''; fi
  curl --fail --silent --show-error "http://127.0.0.1/$url?v=20261020-2" -o /tmp/pressuremap-check-current
  cmp "$release_dir/$asset" /tmp/pressuremap-check-current
done
curl --fail --silent --show-error --cacert /opt/pressure-map-server/deploy/certs/server.crt https://121.43.32.85/api/health
printf '\nRelease: %s\nPrevious: %s\n' "$release_id" "$previous"
trap - EXIT
