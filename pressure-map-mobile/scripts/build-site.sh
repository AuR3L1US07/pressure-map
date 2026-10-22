#!/usr/bin/env bash
# 组装可直接发布的静态站点：www/ 的副本 + 最新的 Android 安装包 → site/
# 用法：bash scripts/build-site.sh   （之后把 site/ 目录发布出去即可）
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SITE="$ROOT/site"
APK="$(ls -t "$ROOT"/dist/PressureMap-android-*.apk 2>/dev/null | head -1 || true)"

if [ -z "${APK:-}" ]; then
  echo "!! dist/ 里没有 APK，先执行 bash scripts/build-android.sh" >&2
  exit 1
fi

rm -rf "$SITE"
mkdir -p "$SITE/download"
cp -R "$ROOT/www/." "$SITE/"

# 固定文件名给网页里的下载链接用，同时保留版本号文件名方便存档
cp "$APK" "$SITE/download/PressureMap-android.apk"
cp "$APK" "$SITE/download/$(basename "$APK")"

echo "site  -> $SITE  ($(du -sh "$SITE" | cut -f1))"
echo "apk   -> site/download/PressureMap-android.apk ($(wc -c < "$SITE/download/PressureMap-android.apk" | tr -d ' ') 字节)"
echo "entry -> site/index.html"
