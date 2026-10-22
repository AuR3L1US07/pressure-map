"""生成 PressureMap 手机版图标（纯标准库，无第三方依赖）。

用法： python scripts/make-icons.py
输出： www/icons/icon-192.png, icon-512.png, icon-maskable-512.png, apple-touch-icon.png
以及 ios/PressureMap/Assets.xcassets/AppIcon.appiconset/AppIcon-1024.png
"""
import math
import os
import struct
import sys
import zlib

SS = 3  # 每像素超采样倍数

BG_A = (0x8b, 0x78, 0xe6)
BG_B = (0x65, 0x52, 0xcc)
WHITE = (255, 255, 255)


def round_rect(x, y, x0, y0, w, h, r):
    """点是否落在圆角矩形内。"""
    cx = min(max(x, x0 + r), x0 + w - r)
    cy = min(max(y, y0 + r), y0 + h - r)
    if x0 <= x <= x0 + w and y0 <= y <= y0 + h:
        if (x < x0 + r or x > x0 + w - r) and (y < y0 + r or y > y0 + h - r):
            return (x - cx) ** 2 + (y - cy) ** 2 <= r * r
        return True
    return False


def ring(x, y, cx, cy, r, half):
    return abs(math.hypot(x - cx, y - cy) - r) <= half


def angle_deg(x, y, cx, cy):
    """从正上方顺时针的角度，范围 0..360。"""
    a = math.degrees(math.atan2(x - cx, cy - y))
    return a % 360


def sample(x, y, scale, offset):
    """返回 (r,g,b,a)，坐标在 512 设计空间。"""
    # 背景
    if round_rect(x, y, 0, 0, 512, 512, 112):
        t = (x + y) / 1024.0
        col = tuple(int(BG_A[i] + (BG_B[i] - BG_A[i]) * t) for i in range(3))
        a = 255
    else:
        return (0, 0, 0, 0)

    layers = []
    if ring(x, y, 256, 238, 112, 13):
        layers.append(0.28)
    # 高亮弧：从 0° 到 124°（对应 SVG 里的 92/174 端点）
    if ring(x, y, 256, 238, 112, 13):
        ad = angle_deg(x, y, 256, 238)
        if 0 <= ad <= 124:
            layers.append(1.0)
    for (bx, by, bw, bh) in ((196, 286, 26, 46), (243, 256, 26, 76), (290, 222, 26, 110)):
        if round_rect(x, y, bx, by, bw, bh, 9):
            layers.append(1.0)

    for alpha in layers:
        if alpha >= 1.0:
            col = WHITE
        else:
            col = tuple(int(col[i] + (WHITE[i] - col[i]) * alpha) for i in range(3))
    return (col[0], col[1], col[2], a)


def render(size, radius_ratio=0.219, content_scale=1.0):
    """radius_ratio: 圆角半径 / 边长，用于 iOS（无圆角）或 PWA。"""
    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            r = g = b = a = 0
            for sy in range(SS):
                for sx in range(SS):
                    # 映射到 512 设计空间；content_scale < 1 表示留白的 maskable 版
                    ux = (px + (sx + 0.5) / SS) / size
                    uy = (py + (sy + 0.5) / SS) / size
                    dx = (ux - 0.5) / content_scale + 0.5
                    dy = (uy - 0.5) / content_scale + 0.5
                    if not (0 <= dx <= 1 and 0 <= dy <= 1):
                        continue
                    pr, pg, pb, pa = sample(dx * 512, dy * 512, 1.0, 0)
                    r += pr
                    g += pg
                    b += pb
                    a += pa
            n = SS * SS
            row += bytes((r // n, g // n, b // n, a // n))
        rows.append(bytes(row))
    return rows


def write_png(path, size, rows, radius_ratio=0.0):
    raw = b"".join(b"\x00" + r for r in rows)
    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) +
           chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as fh:
        fh.write(png)
    return len(png)


def rounded_mask(rows, size, proportion):
    """把方形图案裁成圆角（PWA / Android 用的普通图标）。"""
    r = size * proportion
    out = []
    for y in range(size):
        row = bytearray(rows[y])
        for x in range(size):
            i = x * 4 + 3
            # 到最近圆角圆心的距离
            cx = min(max(x + 0.5, r), size - r)
            cy = min(max(y + 0.5, r), size - r)
            inside_box = r <= x + 0.5 <= size - r or r <= y + 0.5 <= size - r
            if not inside_box and math.hypot(x + 0.5 - cx, y + 0.5 - cy) > r:
                row[i] = 0
        out.append(bytes(row))
    return out


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    www_icons = os.path.join(root, "www", "icons")
    ios_icons = os.path.join(root, "ios", "PressureMap", "Assets.xcassets", "AppIcon.appiconset")

    jobs = [
        (192, os.path.join(www_icons, "icon-192.png"), 0.219, 1.0),
        (512, os.path.join(www_icons, "icon-512.png"), 0.219, 1.0),
        (180, os.path.join(www_icons, "apple-touch-icon.png"), 0.0, 1.0),
        (512, os.path.join(www_icons, "icon-maskable-512.png"), 0.0, 0.78),
        (1024, os.path.join(ios_icons, "AppIcon-1024.png"), 0.0, 1.0),
    ]
    total = 0
    for size, path, radius, scale in jobs:
        rows = render(size, radius, scale)
        if radius > 0:
            rows = rounded_mask(rows, size, radius)
        total += write_png(path, size, rows)
        print("wrote %s (%d px)" % (os.path.relpath(path, root), size))
    print("total %.1f KB" % (total / 1024.0))


if __name__ == "__main__":
    sys.exit(main())
