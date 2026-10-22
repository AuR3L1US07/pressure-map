#!/usr/bin/env bash
# 下载构建所需的工具链（JDK 21 + Android SDK 组件），全部放在 .tools/ 内，不污染系统。
# 用法： bash scripts/fetch-toolchain.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS="$ROOT/.tools"
JDK_DIR="$TOOLS/jdk-21"
SDK="$TOOLS/android"
MIRROR="https://mirrors.cloud.tencent.com/AndroidSDK"

mkdir -p "$TOOLS" "$SDK/build-tools" "$SDK/platforms"

if [ -d "$TOOLS"/jdk-21* ]; then
  echo "✓ JDK 已存在，跳过"
else
  echo "==> 下载 JDK 21（Temurin）"
  curl -L --fail -o "$TOOLS/jdk21.zip" \
    "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jdk/hotspot/normal/eclipse"
  unzip -q -o "$TOOLS/jdk21.zip" -d "$TOOLS"
  mv "$TOOLS"/jdk-21* "$JDK_DIR" 2>/dev/null || true
fi

if [ -f "$SDK/build-tools/34.0.0/aapt2.exe" ]; then
  echo "✓ build-tools 34 已存在，跳过"
else
  echo "==> 下载 build-tools 34（腾讯云镜像，约 58MB）"
  curl -L --fail -o "$TOOLS/build-tools-34.zip" "$MIRROR/build-tools_r34-windows.zip"
  rm -rf "$TOOLS/bt-tmp" && unzip -q -o "$TOOLS/build-tools-34.zip" -d "$TOOLS/bt-tmp"
  mv "$TOOLS/bt-tmp"/* "$SDK/build-tools/34.0.0"
  rm -rf "$TOOLS/bt-tmp"
fi

if [ -f "$SDK/platforms/android-35/android.jar" ]; then
  echo "✓ platform 35 已存在，跳过"
else
  echo "==> 下载 platform 35（腾讯云镜像，约 46MB）"
  curl -L --fail -o "$TOOLS/platform-35.zip" "$MIRROR/platform-35_r02.zip"
  rm -rf "$TOOLS/plat-tmp" && unzip -q -o "$TOOLS/platform-35.zip" -d "$TOOLS/plat-tmp"
  mv "$TOOLS/plat-tmp/android-35" "$SDK/platforms/android-35"
  rm -rf "$TOOLS/plat-tmp"
fi

echo ""
echo "✅ 工具链就绪："
"$JDK_DIR/bin/java.exe" -version 2>&1 | head -1
echo "   build-tools: $SDK/build-tools/34.0.0"
echo "   platform:    $SDK/platforms/android-35"
