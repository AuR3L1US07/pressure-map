"""打包官网新版本：dist/ + 手机版应用 app/ + Android 安装包 download/

产出 deploy/website-20261021-mobile-v2c.tar.gz，供 deploy/update-mobile-v1.sh 在服务器上解包。

用法： python deploy/pack-mobile-v2c.py
"""

import hashlib
import os
import shutil
import tarfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOBILE = os.path.join(os.path.dirname(ROOT), "pressure-map-mobile")
SITE = os.path.join(MOBILE, "site")
APK = os.path.join(MOBILE, "dist", "PressureMap-android-2.1.0.apk")
APK_NAME = "PressureMap-android-2.1.0.apk"
RELEASE = "20261021-mobile-v2c"
STAGE = os.path.join(ROOT, "deploy", "stage-" + RELEASE)
ARCHIVE = os.path.join(ROOT, "deploy", "website-" + RELEASE + ".tar.gz")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    if not os.path.isdir(SITE):
        raise SystemExit("缺少 %s，先执行 bash pressure-map-mobile/scripts/build-site.sh" % SITE)
    if not os.path.exists(APK):
        raise SystemExit("缺少 %s" % APK)

    if os.path.isdir(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)

    # 1) 官网本体
    for name in ("index.html", "style.css", "app.js", "assets"):
        src = os.path.join(ROOT, "dist", name)
        dst = os.path.join(STAGE, name)
        if os.path.isdir(src):
            shutil.copytree(src, dst)
        else:
            shutil.copy2(src, dst)

    # 2) 手机版应用，挂在 /app/
    app_dir = os.path.join(STAGE, "app")
    shutil.copytree(SITE, app_dir)
    shutil.rmtree(os.path.join(app_dir, "download"), ignore_errors=True)

    # 站内不重复放安装包，改成引用根目录那一个
    index_path = os.path.join(app_dir, "index.html")
    html = open(index_path, encoding="utf-8").read()
    before = 'href="download/PressureMap-android.apk"'
    after = 'href="../download/%s"' % APK_NAME
    if before not in html:
        raise SystemExit("app/index.html 里没找到下载链接，检查 build-site.sh 产物")
    html = html.replace(before, after)
    open(index_path, "w", encoding="utf-8", newline="").write(html)

    # 3) 安装包放站点根目录
    os.makedirs(os.path.join(STAGE, "download"))
    shutil.copy2(APK, os.path.join(STAGE, "download", APK_NAME))

    # 4) 校验清单（相对路径，按行 sha256sum -c 可用）
    files = []
    for base, _dirs, names in os.walk(STAGE):
        for n in names:
            full = os.path.join(base, n)
            files.append(os.path.relpath(full, STAGE).replace(os.sep, "/"))
    files.sort()
    apk_hash = sha256(os.path.join(STAGE, "download", APK_NAME))
    lines = ["%s  %s" % (sha256(os.path.join(STAGE, f)), f) for f in files]
    with open(os.path.join(STAGE, "manifest.sha256"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")

    # dist 的清单同步更新，保持本地自洽
    dist = os.path.join(ROOT, "dist")
    dlines = []
    for base, _dirs, names in os.walk(dist):
        for n in names:
            if n == "manifest.sha256":
                continue
            full = os.path.join(base, n)
            rel = os.path.relpath(full, dist).replace(os.sep, "/")
            dlines.append("%s  %s" % (sha256(full), rel))
    dlines.sort(key=lambda s: s.split("  ", 1)[1])
    with open(os.path.join(dist, "manifest.sha256"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(dlines) + "\n")

    # 5) 打包
    if os.path.exists(ARCHIVE):
        os.remove(ARCHIVE)
    with tarfile.open(ARCHIVE, "w:gz") as tar:
        for name in sorted(os.listdir(STAGE)):
            tar.add(os.path.join(STAGE, name), arcname=name)

    size = os.path.getsize(ARCHIVE)
    print("stage   -> %s (%d 个文件)" % (STAGE, len(files) + 1))
    print("archive -> %s (%.1f KB)" % (ARCHIVE, size / 1024.0))
    print("apk sha256 %s" % apk_hash)
    digest = {}
    for line in lines:
        h, p = line.split("  ", 1)
        digest[p] = h
    for key in ("index.html", "app/index.html", "download/" + APK_NAME):
        print("  %-42s %s" % (key, digest.get(key, "缺失")[:16] + "…"))


if __name__ == "__main__":
    main()
