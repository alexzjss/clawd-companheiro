# Desenha o ícone do Clawdesk: o Clawd num crachá, com cordão laranja, num quadrado escuro.
# Gera desktop/clawdesk.ico (16 a 256 px) e docs/icon.png. Uso: python desktop/make-icon.py
# (precisa do Pillow: pip install pillow)
import os
from PIL import Image, ImageDraw, ImageFilter

N, S = 64, 4          # a arte é uma grade de 64x64, ampliada 4x para 256 px
Z = N * S
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def C(h, a=255):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


class Grid:
    def __init__(self):
        self.im = Image.new('RGBA', (N, N), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.im)

    def r(self, x, y, w, h, c):
        if w > 0 and h > 0:
            self.d.rectangle([x, y, x + w - 1, y + h - 1], fill=C(c))

    def p(self, x, y, c):
        self.im.putpixel((x, y), C(c))


OR, ORL, ORD, ORK, INK = '#d97757', '#f2a283', '#b4573a', '#7a3420', '#2a1712'


def clawd(g, x, y, w=24, h=12):
    g.r(x, y, w, h, OR); g.r(x, y, w, 2, ORL); g.r(x, y + h - 2, w, 2, ORD)
    g.r(x - 1, y + 1, 1, h - 2, ORK); g.r(x + w, y + 1, 1, h - 2, ORK)
    g.r(x + 1, y - 1, w - 2, 1, ORK); g.r(x + 1, y + h, w - 2, 1, ORK)
    # braços
    g.r(x - 4, y + 5, 4, 3, OR); g.r(x - 4, y + 7, 4, 1, ORD); g.r(x + w, y + 5, 4, 3, OR); g.r(x + w, y + 7, 4, 1, ORD)
    g.r(x - 5, y + 5, 1, 3, ORK); g.r(x + w + 4, y + 5, 1, 3, ORK)
    # olhos com brilho e bochechas
    ex = x + w // 2
    g.r(ex - 6, y + 3, 3, 5, INK); g.r(ex + 3, y + 3, 3, 5, INK)
    g.p(ex - 6, y + 3, '#ffffff'); g.p(ex + 3, y + 3, '#ffffff')
    g.r(ex - 9, y + 8, 2, 1, '#ff8fa3'); g.r(ex + 7, y + 8, 2, 1, '#ff8fa3')
    # pernas
    for lx in (2, 6, w - 8, w - 4):
        g.r(x + lx, y + h + 1, 2, 3, ORK)


def art():
    g = Grid()
    x = 32
    # cordão e clipe
    g.r(x - 3, 0, 6, 9, OR); g.r(x - 3, 0, 1, 9, ORD); g.r(x + 2, 0, 1, 9, ORD)
    for yy in range(1, 9, 4):
        g.r(x - 2, yy, 4, 1, ORD)
    g.r(x - 5, 7, 10, 6, '#c9ced6'); g.r(x - 5, 7, 10, 1, '#eef1f5'); g.r(x - 5, 12, 10, 1, '#7d8794'); g.r(x - 2, 9, 4, 2, '#5d6672')
    # o crachá: cantos arredondados, faixa laranja, furo
    cx, cy, cw, ch, hh = 10, 12, 44, 48, 11
    g.r(cx + 2, cy, cw - 4, ch, '#f6f1e8'); g.r(cx, cy + 2, cw, ch - 4, '#f6f1e8'); g.r(cx + 1, cy + 1, cw - 2, ch - 2, '#f6f1e8')
    g.r(cx + 2, cy, cw - 4, hh, OR); g.r(cx + 1, cy + 1, cw - 2, hh - 1, OR); g.r(cx, cy + 2, cw, hh - 2, OR)
    g.r(cx + 2, cy + ch - 1, cw - 4, 1, '#d8d0c2'); g.r(cx + 1, cy + ch - 2, 1, 1, '#d8d0c2'); g.r(cx + cw - 2, cy + ch - 2, 1, 1, '#d8d0c2')
    g.r(cx + cw // 2 - 4, cy + 3, 8, 2, '#1b1922')
    clawd(g, 20, 30)
    # nome e cargo
    g.r(18, 49, 28, 2, '#cfc6b6'); g.r(22, 53, 20, 2, '#e3dccf')
    return g.im


def tile():
    big = art().resize((Z, Z), Image.NEAREST)
    mask = Image.new('L', (Z, Z), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, Z - 1, Z - 1], radius=58, fill=255)
    clear = Image.new('RGBA', (Z, Z), (0, 0, 0, 0))
    bg = Image.new('RGBA', (Z, Z))
    d = ImageDraw.Draw(bg)
    a, b = C('#2b2638'), C('#17141e')
    for y in range(Z):
        k = y / (Z - 1)
        d.line([(0, y), (Z, y)], fill=tuple(int(a[i] + (b[i] - a[i]) * k) for i in range(3)) + (255,))
    out = clear.copy()
    out.paste(bg, (0, 0), mask)
    # sombra do crachá
    alpha = big.split()[3].point(lambda v: 110 if v else 0)
    sh = Image.new('RGBA', (Z, Z), (0, 0, 0, 0))
    sh.putalpha(alpha)
    sh = sh.filter(ImageFilter.GaussianBlur(7))
    moved = clear.copy()
    moved.paste(sh, (0, 10))
    out.alpha_composite(Image.composite(moved, clear, mask))
    out.alpha_composite(Image.composite(big, clear, mask))
    edge = clear.copy()
    ImageDraw.Draw(edge).rounded_rectangle([1, 1, Z - 2, Z - 2], radius=57, outline=(255, 255, 255, 28), width=2)
    out.alpha_composite(Image.composite(edge, clear, mask))
    return out


if __name__ == '__main__':
    t = tile()
    os.makedirs(os.path.join(ROOT, 'docs'), exist_ok=True)
    t.save(os.path.join(ROOT, 'docs', 'icon.png'))
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [t.resize((s, s), Image.LANCZOS) for s in sizes]
    imgs[-1].save(os.path.join(ROOT, 'desktop', 'clawdesk.ico'), sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    print('ok: desktop/clawdesk.ico, docs/icon.png')
