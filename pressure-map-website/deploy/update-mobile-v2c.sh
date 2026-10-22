#!/bin/sh
# 在服务器 /opt/pressure-map-website 上执行：把手机版应用挂到官网 /app/ 并让 Android 卡片指向真实安装包。
# 用法： sh /tmp/update-mobile-v1.sh
set -eu
release_id=20261021-mobile-v2c
site_root=/opt/pressure-map-website
release_dir="$site_root/releases/$release_id"
archive=/tmp/pressuremap-20261021-mobile-v2c.tar.gz
test -L "$site_root/current"
test ! -e "$release_dir"
test ! -e "$site_root/current.next"
previous=$(readlink "$site_root/current")
mkdir "$release_dir"
tar -xzf "$archive" -C "$release_dir"
(cd "$release_dir" && sha256sum -c manifest.sha256 >/dev/null)
test -s "$release_dir/app/index.html"
test -s "$release_dir/app/app.js"
test -s "$release_dir/download/PressureMap-android-2.1.0.apk"
grep -q 'href="app/"' "$release_dir/index.html"
grep -q 'download/PressureMap-android-2.1.0.apk' "$release_dir/app.js"
grep -q 'href="../download/PressureMap-android-2.1.0.apk"' "$release_dir/app/index.html"
rollback() {
  ln -s "$previous" "$site_root/current.rollback"
  mv -Tf "$site_root/current.rollback" "$site_root/current"
}
ln -s "releases/$release_id" "$site_root/current.next"
trap rollback EXIT
mv -Tf "$site_root/current.next" "$site_root/current"
# 逐字节比对线上产物
for asset in index.html app.js style.css app/index.html app/app.js app/app.css app/pm-snapshot.js app/pm-calendar.js app/pm-cloud.js app/pm-ai.js app/pm-ui.js download/PressureMap-android-2.1.0.apk manifest.webmanifest; do
  case "$asset" in
    manifest.webmanifest) url='app/manifest.webmanifest' ;;
    *) url="$asset" ;;
  esac
  curl --fail --silent --show-error "http://127.0.0.1/$url" -o /tmp/pm-check
  if [ "$asset" = manifest.webmanifest ]; then
    cmp "$release_dir/app/manifest.webmanifest" /tmp/pm-check
  else
    cmp "$release_dir/$asset" /tmp/pm-check
  fi
done
# 线上安装包哈希必须与 app.js 里声明的校验值一致
apk_hash=$(sha256sum "$release_dir/download/PressureMap-android-2.1.0.apk" | cut -d' ' -f1)
grep -q "$apk_hash" "$release_dir/app.js"
curl --fail --silent --show-error --cacert /opt/pressure-map-server/deploy/certs/server.crt https://121.43.32.85/api/health >/dev/null
printf '\nRelease: %s\nPrevious: %s\nAPK sha256: %s\n' "$release_id" "$previous" "$apk_hash"
trap - EXIT
