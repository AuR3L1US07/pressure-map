#!/usr/bin/env bash
# 在 macOS 上构建 iOS 安装包（.ipa）
#
# 前置：macOS + Xcode 15 或更新，已用 Apple ID 登录 Xcode（免费帐号即可装到自己手机）
# 用法：
#   bash ios/build-ios.sh                 # 自动签名，产出 build/PressureMap.ipa
#   TEAM_ID=XXXXXXXXXX bash ios/build-ios.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
APP="$HERE/PressureMap"
BUILD="$HERE/build"

if [ "$(uname -s)" != "Darwin" ]; then
  echo "✗ iOS 安装包只能在 macOS 上构建（需要 Xcode）。" >&2
  echo "  Windows 上可以先用手机浏览器打开并「添加到主屏幕」，见 docs/INSTALL.md" >&2
  exit 1
fi

echo "==> 同步页面到工程"
bash "$ROOT/scripts/prepare-ios.sh"

mkdir -p "$BUILD"
echo "==> 归档 (xcodebuild archive)"
ARGS=(
  -project "$HERE/PressureMap.xcodeproj"
  -scheme PressureMap
  -configuration Release
  -sdk iphoneos
  -archivePath "$BUILD/PressureMap.xcarchive"
  -allowProvisioningUpdates
)
if [ -n "${TEAM_ID:-}" ]; then
  ARGS+=("DEVELOPMENT_TEAM=$TEAM_ID")
fi
xcodebuild "${ARGS[@]}" archive

echo "==> 导出 IPA"
xcodebuild -exportArchive \
  -archivePath "$BUILD/PressureMap.xcarchive" \
  -exportPath "$BUILD/ipa" \
  -exportOptionsPlist "$HERE/ExportOptions.plist" \
  -allowProvisioningUpdates

echo ""
echo "✅ 完成： $BUILD/ipa/PressureMap.ipa"
echo "   安装：用「Apple 配置实用工具」或 Xcode > Window > Devices and Simulators 拖进去。"
