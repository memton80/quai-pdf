"""Génère l'icône de Quai PDF (même fond que Quai, feuille au coin corné) en .ico et .png.

Usage : python tools/make_icon.py src/QuaiPdf.App/Assets/QuaiPdf.ico src/QuaiPdf.App/Assets/AppIcon.png
"""
import sys
from PIL import Image, ImageDraw


def render(size: int) -> Image.Image:
    scale = 4
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    # Fond : carré arrondi, dégradé vertical bleu nuit -> bleu canard (celui de Quai).
    top, bottom = (15, 76, 129), (11, 132, 148)
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        t = y / max(1, s - 1)
        gd.line([(0, y), (s, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    # Feuille blanche au coin supérieur droit corné.
    white = (255, 255, 255, 255)
    x0, y0, x1, y1 = s * 0.27, s * 0.17, s * 0.73, s * 0.83
    fold = s * 0.15
    d.polygon([(x0, y0), (x1 - fold, y0), (x1, y0 + fold), (x1, y1), (x0, y1)], fill=white)
    d.polygon([(x1 - fold, y0), (x1 - fold, y0 + fold), (x1, y0 + fold)], fill=(190, 214, 228, 255))
    # Lignes de texte.
    w = max(1, int(s * 0.045))
    ink = (15, 76, 129, 255)
    for i, right in enumerate((0.62, 0.62, 0.52)):
        y = s * (0.50 + i * 0.09)
        d.rounded_rectangle([s * 0.36, y, s * right, y + w], radius=w / 2, fill=ink)
    return img.resize((size, size), Image.LANCZOS)


out_ico, out_png = sys.argv[1], sys.argv[2]
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
big = render(256)
big.save(out_ico, format="ICO", sizes=[(n, n) for n in sizes], append_images=[render(n) for n in sizes[:-1]])
render(64).save(out_png, format="PNG")
