#!/bin/sh
set -eu
site=/opt/pressure-map-website
release=20261020-windows-v1
dest="$site/releases/$release"
test ! -e "$dest"
previous=$(readlink "$site/current")
mkdir "$dest"
cp -a "$site/current/." "$dest/"
cp /tmp/pm-windows-app.js "$dest/app.js"
cp /tmp/pm-windows-index.html "$dest/index.html"
cp /tmp/PressureMap-2.0.0-setup.exe "$dest/download/PressureMap-2.0.0-setup.exe"
hash=$(sha256sum "$dest/download/PressureMap-2.0.0-setup.exe" | cut -d' ' -f1)
grep -q "$hash" "$dest/app.js"
(cd "$dest" && find . -type f ! -name manifest.sha256 -exec sha256sum {} \; > manifest.sha256 && sha256sum -c manifest.sha256 >/dev/null)
rollback() {
  ln -s "$previous" "$site/rollback-windows"
  mv -Tf "$site/rollback-windows" "$site/current"
}
ln -s "releases/$release" "$site/next-windows"
trap rollback EXIT
mv -Tf "$site/next-windows" "$site/current"
for path in app.js index.html download/PressureMap-2.0.0-setup.exe app/index.html download/PressureMap-android-1.0.0.apk; do
  curl -fsS "http://127.0.0.1/$path" -o /tmp/pm-windows-verify
  cmp "$dest/$path" /tmp/pm-windows-verify
done
curl -fsS --cacert /opt/pressure-map-server/deploy/certs/server.crt https://121.43.32.85/api/health
trap - EXIT
printf '\nRelease: %s\nPrevious: %s\nSHA256: %s\n' "$release" "$previous" "$hash"
