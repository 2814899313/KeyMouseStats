# -*- coding: utf-8 -*-
"""生成 KeyMouseStats 的应用图标 assets/app-icon.ico(2.0.6 起使用)。

素材是美术给的 mockup:深色背景上的一枚圆角方形徽章。这里自动找徽章边界,
裁成正方形,把圆角以外的区域做成透明,再导出多尺寸 ICO(Windows 按 DPI 取帧)。

用法(在仓库根目录):
    python tools/make_app_icon.py

输出:
    assets/app-icon.ico          多尺寸图标(16/20/24/32/40/48/64/96/128/256)
    assets/app-icon-preview.png  各尺寸与深/浅背景预览,只用于肉眼验收
"""
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets", "app-icon-source.png")
OUT_ICO = os.path.join(ROOT, "assets", "app-icon.ico")
OUT_PREVIEW = os.path.join(ROOT, "assets", "app-icon-preview.png")

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SS = 4  # 圆角蒙版的超采样倍数


def badge_box(im):
    """返回徽章正方形 (left, top, size)。"""
    edge = im.convert("L").filter(ImageFilter.FIND_EDGES)
    px = edge.load()
    w, h = edge.size
    cols, rows = {}, {}
    for y in range(h):
        xs = [x for x in range(w) if px[x, y] > 40]
        if len(xs) > 100:  # 直边所在的行
            cols[y] = (min(xs), max(xs))
    for x in range(w):
        ys = [y for y in range(h) if px[x, y] > 40]
        if len(ys) > 100:  # 直边所在的列
            rows[x] = (min(ys), max(ys))
    if not cols or not rows:
        raise SystemExit("未找到徽章边界")
    left = sorted(v[0] for v in cols.values())[len(cols) // 2]
    right = sorted(v[1] for v in cols.values())[len(cols) // 2]
    top = sorted(v[0] for v in rows.values())[len(rows) // 2]
    bottom = sorted(v[1] for v in rows.values())[len(rows) // 2]
    size = max(right - left + 1, bottom - top + 1)
    cx, cy = (left + right) / 2.0, (top + bottom) / 2.0
    return int(round(cx - size / 2.0)), int(round(cy - size / 2.0)), size


def master(im):
    left, top, size = badge_box(im)
    square = im.crop((left, top, left + size, top + size)).convert("RGBA")
    n = square.size[0] * SS
    big = square.resize((n, n), Image.LANCZOS)
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, n - 1, n - 1), radius=int(round(n * 0.235)), fill=255)
    big.putalpha(mask)
    return big


def frame(master_img, size):
    return master_img.resize((size, size), Image.LANCZOS)


def preview(frames):
    pad, gap = 16, 12
    strips = [f.resize((64, 64), Image.LANCZOS) for f in frames[:-1]]
    w = pad * 2 + 64 * len(strips) + gap * (len(strips) - 1)
    strip = Image.new("RGB", (w, 64 + pad * 2), (247, 248, 250))
    x = pad
    for s in strips:
        strip.paste(s, (x, pad), s)
        x += 64 + gap
    big = frames[-1]
    dark = Image.new("RGB", (288, 288), (12, 18, 32))
    dark.paste(big, (16, 16), big)
    light = Image.new("RGB", (288, 288), (240, 242, 246))
    light.paste(big, (16, 16), big)
    sheet = Image.new("RGB", (w, strip.height + 288 + 8), (255, 255, 255))
    sheet.paste(strip, (0, 0))
    sheet.paste(dark, (pad, strip.height + 8))
    sheet.paste(light, (pad + dark.width + 8, strip.height + 8))
    sheet.save(OUT_PREVIEW, "PNG")
    print("preview", OUT_PREVIEW, sheet.size)


def main():
    im = Image.open(SRC).convert("RGB")
    big = master(im)
    print("master", big.size)
    frames = [frame(big, s) for s in SIZES]
    frames[-1].save(OUT_ICO, format="ICO", append_images=frames[:-1],
                    sizes=[(s, s) for s in SIZES])
    print("ico", OUT_ICO, os.path.getsize(OUT_ICO), "bytes")
    preview(frames)


if __name__ == "__main__":
    main()
