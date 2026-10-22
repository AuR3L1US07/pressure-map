"""把官网下载卡的 Android 入口指向 2.0.0 安装包（路径 / 版本 / 大小 / 哈希 / 日期）。
用法： python deploy/patch-mobile-appjs-v2.py <apk 路径>
"""

import hashlib
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOBILE = os.path.join(os.path.dirname(ROOT), "pressure-map-mobile")
APK = sys.argv[1] if len(sys.argv) > 1 else os.path.join(MOBILE, "dist", "PressureMap-android-2.0.0.apk")


def main():
    if not os.path.exists(APK):
        raise SystemExit("缺少安装包：%s" % APK)
    name = os.path.basename(APK)
    version = re.search(r"-(\d+\.\d+\.\d+)\.apk$", name).group(1)
    size = os.path.getsize(APK)
    with open(APK, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    size_label = "%d KB" % round(size / 1024.0)

    path = os.path.join(ROOT, "dist", "app.js")
    text = open(path, encoding="utf-8").read()
    text, n1 = re.subn(r'android:"[^"]*"', 'android:"download/%s"' % name, text, count=1)
    info = 'android:{version:"%s",size:"%s",sha256:"%s",date:"%s"}' % (
        version, size_label, digest, "2026-10-21")
    text, n2 = re.subn(r"android:\{[^}]*\}", info, text, count=1)
    if n1 != 1 or n2 != 1:
        raise SystemExit("app.js 结构与预期不符：downloadUrls=%d releaseInfo=%d" % (n1, n2))
    open(path, "w", encoding="utf-8", newline="").write(text)
    print("ok  %s  %d bytes  %s  %s" % (name, size, size_label, digest[:16] + "…"))


if __name__ == "__main__":
    main()
