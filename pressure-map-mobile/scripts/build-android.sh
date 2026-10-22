#!/usr/bin/env bash
# 构建 Android 安装包（不依赖 Gradle / Android Studio）
#
# 依赖：.tools 下的 JDK 21 + Android SDK（build-tools 34、platform 35）
#      由 scripts/fetch-toolchain.sh 自动下载。
#
# 用法： bash scripts/build-android.sh
# 产物： dist/PressureMap-android-1.0.0.apk
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
JDK="$(ls -d "$ROOT"/.tools/jdk-* 2>/dev/null | head -1 || true)"
BT="$ROOT/.tools/android/build-tools/34.0.0"
ANDROID_JAR="$ROOT/.tools/android/platforms/android-35/android.jar"
OUT="$ROOT/build/android"
VERSION_NAME="2.3.0"
VERSION_CODE="5"

if [ -z "$JDK" ] || [ ! -f "$JDK/bin/javac.exe" ]; then
  echo "缺少 JDK，请先运行： bash scripts/fetch-toolchain.sh" >&2
  exit 1
fi
if [ ! -f "$ANDROID_JAR" ] || [ ! -f "$BT/aapt2.exe" ]; then
  echo "缺少 Android SDK，请先运行： bash scripts/fetch-toolchain.sh" >&2
  exit 1
fi

export JAVA_HOME="$JDK"
export PATH="$JDK/bin:$PATH"

echo "==> 准备输出目录"
if [ -d "$OUT" ]; then rm -rf "$OUT" 2>/dev/null || true; fi
mkdir -p "$OUT/gen" "$OUT/classes" "$OUT/dex" "$OUT/assets/www"

echo "==> 同步 www/ -> assets/www/"
cp -r "$ROOT/www/." "$OUT/assets/www/"

echo "==> 编译资源 (aapt2 compile)"
"$BT/aapt2.exe" compile --dir "$ROOT/android/res" -o "$OUT/res.zip"

echo "==> 链接资源并打包 (aapt2 link)"
# 注意：不要用 aapt2 的 -A 打包 assets —— Windows 版 aapt2 会把条目名写成
# assets\www\app.css 这样的反斜杠，Android 运行时按 assets/www/app.css 找不到文件。
"$BT/aapt2.exe" link \
  -o "$OUT/base.apk" \
  -I "$ANDROID_JAR" \
  --manifest "$ROOT/android/AndroidManifest.xml" \
  -R "$OUT/res.zip" \
  --java "$OUT/gen" \
  --min-sdk-version 26 \
  --target-sdk-version 34 \
  --version-code "$VERSION_CODE" \
  --version-name "$VERSION_NAME" \
  --auto-add-overlay

echo "==> 编译 Java"
cd "$OUT"
rm -rf src 2>/dev/null || true
cp -r "$ROOT/android/src" src
find gen src -name '*.java' > sources.txt
"$JDK/bin/javac.exe" -encoding UTF-8 -source 11 -target 11 -nowarn \
  -classpath "$ANDROID_JAR" -d classes @sources.txt

echo "==> 转换为 dex (d8)"
find classes -name '*.class' > classes.list
"$BT/d8.bat" --release --min-api 26 --lib "$ANDROID_JAR" --output dex @classes.list >/dev/null

echo "==> 合并 assets 与 classes.dex"
cp base.apk unsigned.apk
"$JDK/bin/jar.exe" uf unsigned.apk -C "$OUT" assets
"$JDK/bin/jar.exe" uf unsigned.apk -C dex classes.dex

echo "==> 对齐 (zipalign)"
"$BT/zipalign.exe" -f -p 4 unsigned.apk aligned.apk

echo "==> 签名 (apksigner)"
KS="$ROOT/android/keystore/pressuremap-release.jks"
if [ ! -f "$KS" ]; then
  echo "    首次构建：生成签名密钥"
  mkdir -p "$(dirname "$KS")"
  "$JDK/bin/keytool.exe" -genkeypair -v \
    -keystore "$KS" -alias pressuremap -keyalg RSA -keysize 2048 -validity 10950 \
    -storepass pressuremap -keypass pressuremap \
    -dname "CN=PressureMap, OU=Mobile, O=PressureMap, L=Hangzhou, S=Zhejiang, C=CN" >/dev/null 2>&1
fi

mkdir -p "$ROOT/dist"
APK="$ROOT/dist/PressureMap-android-$VERSION_NAME.apk"
"$BT/apksigner.bat" sign \
  --ks "$KS" --ks-key-alias pressuremap \
  --ks-pass pass:pressuremap --key-pass pass:pressuremap \
  --min-sdk-version 26 \
  --out "$APK" aligned.apk

"$BT/apksigner.bat" verify --print-certs "$APK" | head -4

SIZE=$(du -h "$APK" | cut -f1)
echo ""
echo "✅ 构建完成： dist/PressureMap-android-$VERSION_NAME.apk ($SIZE)"
