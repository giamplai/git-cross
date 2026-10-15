#!/usr/bin/env python3
"""Generate the app icon (PNG sizes, .ico, .icns) into src/GitCross.App/Assets. Deterministic: rerun after tweaking colors."""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "GitCross.App", "Assets")
os.makedirs(OUT, exist_ok=True)
S = 1024  # draw large, downsample for smooth edges


def render() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # rounded-square background with a vertical blue gradient
    grad = Image.new("RGBA", (S, S))
    top, bottom = (84, 132, 250), (46, 84, 200)
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=220, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    white, mint = (255, 255, 255, 255), (120, 235, 170, 255)
    w = 46
    x_main, x_side = 400, 650

    # main line, side branch that forks off and merges back
    d.line([(x_main, 250), (x_main, 780)], fill=white, width=w)
    d.line([(x_main, 400), (x_main, 400), (x_side, 480), (x_side, 600), (x_main, 680)], fill=mint, width=w, joint="curve")

    def node(cx, cy, color, r=74):
        d.ellipse([cx - r - 14, cy - r - 14, cx + r + 14, cy + r + 14], fill=(46, 84, 200, 255))  # ring cut-out
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color)

    node(x_main, 250, white)
    node(x_main, 780, white)
    node(x_side, 540, mint)
    node(x_main, 515, white, r=60)
    return img


if __name__ == "__main__":
    big = render()
    sizes = [16, 24, 32, 48, 64, 128, 256, 512]
    for s in sizes:
        big.resize((s, s), Image.LANCZOS).save(os.path.join(OUT, f"icon-{s}.png"))
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "icon.png"))
    big.save(os.path.join(OUT, "icon.ico"), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    big.resize((1024, 1024), Image.LANCZOS).save(os.path.join(OUT, "icon.icns"))
    print("icons written to", os.path.abspath(OUT))
