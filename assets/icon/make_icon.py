"""
打印机共享修复工具 —— 应用图标生成脚本

设计思路：
  · 圆角方形底板（Windows Fluent 风格），蓝色渐变；
  · 中间是一台白色打印机，出纸口吐出一张浅蓝色纸张，纸上有对勾（代表“已修复”）；
  · 纸张上有一个深蓝色对勾，代表“问题已修复”。

输出：
  assets/icon/app-icon-256.png          预览图（透明背景）
  assets/icon/app-icon-1024.png         母版（透明背景）
  src/PrinterShareFixer.App/Assets/app.ico   多尺寸图标（16/24/32/48/64/128/256）
  src/PrinterShareFixer.App/Assets/app-icon.png  界面标题栏使用的小图（256×256）

用法：
  python make_icon.py            生成图标
  python make_icon.py preview    额外在终端用字符画打印 16/24/32 尺寸，便于检查小图标是否清晰

每个尺寸都按 8 倍超采样单独绘制后再缩小，保证 16×16 也足够锐利。
"""

from __future__ import annotations

import io
import os
import struct
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ICO_PATH = os.path.join(REPO_ROOT, "src", "PrinterShareFixer.App", "Assets", "app.ico")
PREVIEW_PATH = os.path.join(HERE, "app-icon-256.png")
MASTER_PATH = os.path.join(HERE, "app-icon-1024.png")
APP_PNG_PATH = os.path.join(REPO_ROOT, "src", "PrinterShareFixer.App", "Assets", "app-icon.png")

ICON_SIZES = [16, 24, 32, 48, 64, 128, 256]
SUPERSAMPLE = 8

# 配色
GRADIENT_TOP = (43, 135, 218)      # #2B87DA
GRADIENT_BOTTOM = (11, 78, 151)    # #0B4E97
BODY_WHITE = (255, 255, 255)
PAPER_BLUE = (203, 226, 248)       # #CBE2F8
SLOT_BLUE = (201, 220, 240)        # #C9DCF0
ACCENT_CYAN = (168, 216, 255)      # #A8D8FF
CHECK_BLUE = (11, 78, 151)


def _rounded(draw: ImageDraw.ImageDraw, box, radius, fill):
    draw.rounded_rectangle(box, radius=radius, fill=fill)


def _vertical_gradient(size: tuple[int, int], top, bottom) -> Image.Image:
    width, height = size
    gradient = Image.new("RGBA", (1, height))
    for y in range(height):
        t = y / max(height - 1, 1)
        gradient.putpixel(
            (0, y),
            (
                round(top[0] + (bottom[0] - top[0]) * t),
                round(top[1] + (bottom[1] - top[1]) * t),
                round(top[2] + (bottom[2] - top[2]) * t),
                255,
            ),
        )
    return gradient.resize((width, height))


def render(size: int) -> Image.Image:
    """按目标尺寸绘制图标（内部使用超采样保证边缘平滑）。"""
    scale = size * SUPERSAMPLE
    image = Image.new("RGBA", (scale, scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def px(value: float) -> float:
        return value * scale

    # 1. 圆角方形底板 + 蓝色渐变
    margin = 0.055
    radius = 0.205
    board = [px(margin), px(margin), px(1 - margin), px(1 - margin)]
    mask = Image.new("L", (scale, scale), 0)
    ImageDraw.Draw(mask).rounded_rectangle(board, radius=px(radius), fill=255)
    image.paste(_vertical_gradient((scale, scale), GRADIENT_TOP, GRADIENT_BOTTOM), (0, 0), mask)

    # 顶部高光，让底板有轻微体积感
    sheen = Image.new("RGBA", (1, scale))
    for y in range(scale):
        t = y / max(scale - 1, 1)
        sheen.putpixel((0, y), (255, 255, 255, round(30 * max(0.0, 1 - t / 0.55))))
    sheen = sheen.resize((scale, scale))
    sheen_masked = Image.new("RGBA", (scale, scale), (0, 0, 0, 0))
    sheen_masked.paste(sheen, (0, 0), mask)
    image.alpha_composite(sheen_masked)

    # 2. 正在打印的纸张（浅蓝）+ 对勾
    _rounded(draw, [px(0.295), px(0.215), px(0.705), px(0.505)], px(0.038), PAPER_BLUE)
    check_width = round(px(0.066))
    draw.line(
        [(px(0.395), px(0.350)), (px(0.465), px(0.425)), (px(0.615), px(0.270))],
        fill=CHECK_BLUE,
        width=check_width,
        joint="curve",
    )
    for point in ((0.395, 0.350), (0.465, 0.425), (0.615, 0.270)):
        draw.ellipse(
            [
                px(point[0] - 0.033),
                px(point[1] - 0.033),
                px(point[0] + 0.033),
                px(point[1] + 0.033),
            ],
            fill=CHECK_BLUE,
        )

    # 3. 打印机机身
    _rounded(draw, [px(0.195), px(0.465), px(0.805), px(0.79)], px(0.072), BODY_WHITE)
    # 出纸口
    _rounded(draw, [px(0.285), px(0.495), px(0.715), px(0.542)], px(0.024), SLOT_BLUE)
    # 出纸托盘
    _rounded(draw, [px(0.295), px(0.700), px(0.705), px(0.750)], px(0.024), SLOT_BLUE)
    # 状态指示灯
    draw.ellipse([px(0.240), px(0.630), px(0.296), px(0.686)], fill=ACCENT_CYAN)

    return image.resize((size, size), Image.LANCZOS)


def build_ico(images: list[Image.Image], path: str) -> None:
    """写出多尺寸 ICO：小尺寸用 32 位 BMP 条目，256 用 PNG 条目（兼容性最好）。"""
    entries = []
    blobs = []
    offset = 6 + 16 * len(images)

    for image in images:
        width, height = image.size
        rgba = image.convert("RGBA")

        if width >= 256:
            buffer = io.BytesIO()
            rgba.save(buffer, format="PNG", optimize=True)
            blob = buffer.getvalue()
        else:
            pixels = rgba.tobytes()  # RGBA，自上而下
            xor_rows = bytearray()
            for y in range(height - 1, -1, -1):  # BMP 为自下而上
                row = pixels[y * width * 4 : (y + 1) * width * 4]
                for x in range(width):
                    r, g, b, a = row[x * 4 : x * 4 + 4]
                    xor_rows += bytes((b, g, r, a))

            xor = bytes(xor_rows)
            and_mask_row = ((width + 31) // 32) * 4
            and_mask = b"\x00" * (and_mask_row * height)
            header = struct.pack(
                "<IiiHHIIiiII",
                40,          # biSize
                width,       # biWidth
                height * 2,  # biHeight（含掩码）
                1,           # biPlanes
                32,          # biBitCount
                0,           # biCompression
                len(xor) + len(and_mask),
                0, 0, 0, 0,
            )
            blob = header + xor + and_mask

        blobs.append(blob)
        entries.append((width, height, len(blob), offset))
        offset += len(blob)

    with open(path, "wb") as handle:
        handle.write(struct.pack("<HHH", 0, 1, len(images)))
        for width, height, length, data_offset in entries:
            handle.write(
                struct.pack(
                    "<BBBBHHII",
                    width % 256,
                    height % 256,
                    0,
                    0,
                    1,
                    32,
                    length,
                    data_offset,
                )
            )
        for blob in blobs:
            handle.write(blob)


def ascii_preview(image: Image.Image, label: str) -> None:
    """在终端用字符画显示图标，便于在没有看图工具时检查形状。"""
    rgba = image.convert("RGBA")
    width, height = rgba.size
    ramp = " .:-=+*#%@"
    print(f"--- {label} ({width}x{height}) ---")
    for y in range(height):
        line = []
        for x in range(width):
            r, g, b, a = rgba.getpixel((x, y))
            if a < 96:
                line.append(" ")
                continue
            luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
            alpha = a / 255
            value = luminance * alpha
            line.append(ramp[min(len(ramp) - 1, int(value * len(ramp)))])
        print("".join(line))
    print()


def main() -> int:
    os.makedirs(os.path.dirname(ICO_PATH), exist_ok=True)

    images = [render(size) for size in ICON_SIZES]
    by_size = dict(zip(ICON_SIZES, images))

    by_size[256].save(PREVIEW_PATH)
    by_size[256].save(APP_PNG_PATH)
    render(1024).save(MASTER_PATH)
    build_ico(images, ICO_PATH)

    print(f"PNG 预览 : {PREVIEW_PATH}")
    print(f"PNG 母版 : {MASTER_PATH}")
    print(f"界面用图 : {APP_PNG_PATH}")
    print(f"ICO 图标 : {ICO_PATH}  ({os.path.getsize(ICO_PATH)} 字节，含 {'/'.join(str(s) for s in ICON_SIZES)})")

    if len(sys.argv) > 1 and sys.argv[1] == "preview":
        for size in (16, 24, 32):
            ascii_preview(by_size[size], f"icon {size}px")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
