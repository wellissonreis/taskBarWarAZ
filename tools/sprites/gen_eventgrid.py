#!/usr/bin/env python3
"""Gera o sprite ORIGINAL da torre Event Grid (hub com nos e raios encadeados), reproduzivel e sem dependencias (zlib puro).

Saida: assets/source/anim/eventgrid-{idle,attack,fx}.png. Pixel art 32x32 ampliado 4x (quadros 128x128; fx 16x16 -> 64x64),
virada para a esquerda, mesma paleta escura dos 13 sprites do design; acento = Palette.Td.TintViolet (#9a6cff), a cor do Event Grid no jogo.
Uso: python3 tools/sprites/gen_eventgrid.py   (e atualiza o MANIFEST.json com o sha256).
"""
import hashlib, json, os, struct, zlib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "source", "anim")
S = 4

OL = (0x0a, 0x0f, 0x1a); D1 = (0x16, 0x1e, 0x2e); D2 = (0x23, 0x2c, 0x3d); D3 = (0x2e, 0x38, 0x4e)
M1 = (0x3a, 0x46, 0x60); M2 = (0x58, 0x68, 0x86); M3 = (0x82, 0x92, 0xb0)
G0 = (0x4a, 0x2f, 0x96); G1 = (0x74, 0x4e, 0xd4); G2 = (0x9a, 0x6c, 0xff); G3 = (0xe4, 0xd4, 0xff)


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.p = [[None] * w for _ in range(h)]

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h and c is not None:
            self.p[y][x] = c

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def box(self, x0, y0, x1, y1, fill, hi=None, lo=None):
        """Retangulo com contorno, luz em cima/esquerda e sombra embaixo/direita."""
        self.rect(x0, y0, x1, y1, OL)
        self.rect(x0 + 1, y0 + 1, x1 - 1, y1 - 1, fill)
        if hi: self.rect(x0 + 1, y0 + 1, x1 - 1, y0 + 1, hi)
        if lo: self.rect(x0 + 1, y1 - 1, x1 - 1, y1 - 1, lo)

    def ellipse(self, cx, cy, rx, ry, fill):
        for y in range(cy - ry, cy + ry + 1):
            for x in range(cx - rx, cx + rx + 1):
                if ((x - cx) / (rx + .5)) ** 2 + ((y - cy) / (ry + .5)) ** 2 <= 1:
                    self.set(x, y, fill)

    def sheet(self, frames):
        return [[f.p[y][x] for f in frames for x in range(f.w)] for y in range(self.h)]


def platform(c):
    c.ellipse(16, 26, 14, 5, OL)
    c.ellipse(16, 25, 13, 4, D3)
    c.ellipse(16, 26, 12, 4, D2)
    c.rect(5, 24, 27, 24, M1)
    c.rect(8, 23, 24, 23, M2)
    c.rect(13, 29, 19, 29, G1)
    for x in (6, 26): c.set(x, 26, M1)


def line(c, x0, y0, x1, y1, col):
    n = max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(n + 1):
        c.set(round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n), col)


def node(c, x, y, glow):
    c.rect(x - 1, y - 1, x + 1, y + 1, OL)
    c.set(x, y, G3 if glow else G2)


def tower(phase=0, glow=0, flash=0, dx=0):
    c = Canvas(32, 32)
    platform(c)
    # base/pedestal
    c.box(11, 20, 21, 25, M1, hi=M3, lo=D3)
    c.rect(12, 21, 12, 24, M2)
    # haste central
    c.rect(14, 12, 18, 20, OL)
    c.rect(15, 12, 17, 20, M1)
    c.rect(15, 12, 15, 20, M3)
    # hub (esfera violeta) com anel
    cx, cy = 16 + dx, 9
    c.ellipse(cx, cy, 5, 5, OL)
    c.ellipse(cx, cy, 4, 4, D2)
    c.ellipse(cx, cy, 3, 3, G0 if not glow else G1)
    c.ellipse(cx, cy, 1, 1, G3 if glow else G2)
    c.set(cx - 3, cy - 3, M3); c.set(cx - 2, cy - 4, M3)
    # nos satelites ligados por raios (a corrente), o da esquerda e o focinho
    nodes = [(4, 4), (27, 6), (6, 17), (26, 15)]
    for i, (x, y) in enumerate(nodes):
        on = (i + phase) % 4 == 0
        line(c, cx, cy, x, y, G2 if (glow or on) else G0)
        node(c, x + (dx if i == 0 else 0), y, glow or on)
    if flash:
        for (x, y) in [(1, 4), (2, 3), (2, 5), (3, 2), (3, 6)]:
            c.set(x, y, G3)
        c.set(0, 4, G2)
    return c


def idle():
    return [tower(phase=i, glow=(i == 2)) for i in range(4)]


def attack():
    # 0-1 carrega, 2 dispara (FireFrame), 3-5 recua
    return [tower(phase=0, glow=1), tower(phase=1, glow=1), tower(phase=2, glow=1, flash=1, dx=1),
            tower(phase=3, glow=1, dx=1), tower(phase=0, glow=0), tower(phase=1, glow=0)]


def fx():
    """Projetil: raio em zigue-zague violeta que pulsa; 16x16 -> 64x64."""
    frames = []
    for i in range(4):
        c = Canvas(16, 16)
        pts = [(1, 8), (4, 5 + (i % 2) * 2), (7, 10 - (i % 2) * 2), (10, 6 + ((i + 1) % 2) * 2), (14, 8)]
        for a, b in zip(pts, pts[1:]):
            line(c, a[0], a[1] - 1, b[0], b[1] - 1, OL); line(c, a[0], a[1] + 1, b[0], b[1] + 1, OL)
        for a, b in zip(pts, pts[1:]):
            line(c, a[0], a[1], b[0], b[1], G2)
        for k, (x, y) in enumerate(pts):
            c.set(x, y, G3 if (k + i) % 2 == 0 else G2)
        frames.append(c)
    return frames


def png(rows, scale):
    h = len(rows); w = len(rows[0])
    raw = bytearray()
    for y in range(h * scale):
        raw.append(0)
        r = rows[y // scale]
        for x in range(w * scale):
            c = r[x // scale]
            raw += bytes((c[0], c[1], c[2], 255)) if c else b"\0\0\0\0"
    def chunk(t, d):
        b = t + d
        return struct.pack(">I", len(d)) + b + struct.pack(">I", zlib.crc32(b) & 0xffffffff)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w * scale, h * scale, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def main():
    sheets = {"eventgrid-idle.png": (idle(), "idle", 6.0, 0.667, 32), "eventgrid-attack.png": (attack(), "attack", 12.0, 0.5, 32),
              "eventgrid-fx.png": (fx(), "fx", 10.0, 0.4, 16)}
    mpath = os.path.join(OUT, "MANIFEST.json")
    man = json.load(open(mpath))
    man["entries"] = [e for e in man["entries"] if not e["file"].startswith("eventgrid-")]
    for name, (frames, role, fps, dur, size) in sheets.items():
        data = png(frames[0].sheet(frames), S)
        open(os.path.join(OUT, name), "wb").write(data)
        fp = size * S
        man["entries"].append({"file": name, "design": "original", "role": role, "frames": len(frames), "durationSeconds": dur,
                               "fps": fps, "sheetPixels": [fp * len(frames), fp], "framePixels": [fp, fp],
                               "sha256": hashlib.sha256(data).hexdigest(), "generator": "tools/sprites/gen_eventgrid.py"})
    man["entries"].sort(key=lambda e: e["file"])
    json.dump(man, open(mpath, "w"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    main()
