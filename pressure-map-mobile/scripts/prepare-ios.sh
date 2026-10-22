#!/usr/bin/env bash
# 把移动端页面同步进 iOS 工程（Xcode 会把 PressureMap/www 整个目录打进 App）
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="$ROOT/ios/PressureMap/www"

rm -rf "$TARGET"
mkdir -p "$TARGET"
cp -R "$ROOT/www/." "$TARGET/"
echo "已同步 www -> ios/PressureMap/www ($(find "$TARGET" -type f | wc -l | tr -d ' ') 个文件)"
