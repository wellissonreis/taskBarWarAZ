#!/usr/bin/env python3
"""Gera o sprite ORIGINAL da torre Pipeline (canos + esteira de dados), reproduzivel e sem dependencias (zlib puro).

Saida: assets/source/anim/pipeline-{idle,attack,fx}.png. Pixel art 32x32 ampliado 4x (quadros 128x128; fx 16x16 -> 64x64),
virada para a esquerda, mesma paleta escura dos 13 sprites do design; acento = Palette.Menta (#5cf0b0), a cor do Pipeline no jogo.
Uso: python3 tools/sprites/gen_pipeline.py   (e atualiza o MANIFEST.json com o sha256).
"""
import hashlib, json, os, struct, zlib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "source", "anim")
S = 4

OL = (0x0a, 0x0f, 0x1a); D1 = (0x16, 0x1e, 0x2e); D2 = (0x23, 0x2c, 0x3d); D3 = (0x2e, 0x38, 0x4e)
M1 = (0x3a, 0x46, 0x60); M2 = (0x58, 0x68, 0x86); M3 = (0x82, 0x92, 0xb0)
G0 = (0x0f, 0x5f, 0x45); G1 = (0x2a, 0xb8, 0x82); G2 = (0x5c, 0xf0, 0xb0); G3 = (0xc8, 0xff, 0xe8)


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


def pipe_h(c, x0, x1, y0, y1, phase, glow):
    """Cano horizontal com janela de esteira: pacotes menta correm para a esquerda."""
    c.rect(x0, y0, x1, y1, OL)
    c.rect(x0, y0 + 1, x1, y0 + 1, M3)
    c.rect(x0, y0 + 2, x1, y1 - 2, D1)
    c.rect(x0, y1 - 1, x1, y1 - 1, M1)
    for x in range(x0, x1 + 1):
        if (x + phase) % 4 in (0, 1):
            c.set(x, y0 + 2, G2 if glow else G1)
            c.set(x, y0 + 3 if y1 - y0 > 4 else y0 + 2, G1 if glow else G0)
    for fx in (x0 + 1, x0 + 7):  # flanges
        if fx + 1 <= x1:
            c.rect(fx, y0 - 1, fx + 1, y1 + 1, OL)
            c.rect(fx, y0, fx, y1, M2)


def tower(dx=0, phase=0, glow=0, flash=0, dy=0):
    c = Canvas(32, 32)
    platform(c)
    # corpo
    bx = 10 + dx
    c.box(bx, 12 + dy, bx + 12, 24 + dy, M1, hi=M3, lo=D3)
    c.rect(bx + 1, 13 + dy, bx + 1, 23 + dy, M2)
    c.rect(bx + 12 - 1, 14 + dy, bx + 12 - 1, 23 + dy, D3)
    # visor da esteira (chevrons)
    c.rect(bx + 3, 17 + dy, bx + 9, 21 + dy, OL)
    c.rect(bx + 4, 18 + dy, bx + 8, 20 + dy, D1)
    for i in range(3):
        x = bx + 4 + ((i + phase) % 3) * 2 - (0)
        col = G3 if glow else G2
        c.set(x, 18 + dy, col); c.set(x + 1, 19 + dy, col); c.set(x, 20 + dy, col)
    # cano vertical no topo (entrada de dados) com flange
    c.rect(bx + 7, 6 + dy, bx + 11, 12 + dy, OL)
    c.rect(bx + 8, 7 + dy, bx + 10, 12 + dy, M1)
    c.rect(bx + 8, 7 + dy, bx + 8, 12 + dy, M3)
    c.rect(bx + 6, 5 + dy, bx + 12, 7 + dy, OL)
    c.rect(bx + 7, 6 + dy, bx + 11, 6 + dy, M2)
    c.set(bx + 9, 8 + dy + (phase % 3), G2)  # pacote caindo
    # cano de saida para a esquerda (focinho)
    pipe_h(c, 1 + dx, bx, 13 + dy, 17 + dy, phase, glow)
    c.rect(0 + dx, 12 + dy, 1 + dx, 18 + dy, OL)
    c.rect(1 + dx, 13 + dy, 1 + dx, 17 + dy, M2)
    c.rect(2 + dx, 14 + dy, 2 + dx, 16 + dy, G2 if glow else D1)
    # luz de estado
    c.set(bx + 11, 14 + dy, G3 if glow else G2)
    if flash:
        for (x, y) in [(0, 15), (1, 14), (1, 16), (0, 13), (0, 17)]:
            c.set(x, y, G3)
        c.set(1, 15, G3); c.set(2, 15, G3)
        for (x, y) in [(0, 12), (0, 18)]:
            c.set(x, y, G2)
    return c


def idle():
    return [tower(phase=i, glow=(i == 2)) for i in range(4)]


def attack():
    # 0-1 carrega, 2 dispara (FireFrame), 3-5 recua
    return [tower(phase=0, glow=1),
            tower(phase=1, glow=1, dx=0, dy=0),
            tower(phase=2, glow=1, flash=1, dx=1),
            tower(phase=3, glow=1, dx=1),
            tower(phase=0, glow=0, dx=1 if False else 0),
            tower(phase=1, glow=0)]


def fx():
    """Projetil: corrente de pacotes (quadrados menta) em fila que pulsa; 16x16 -> 64x64."""
    frames = []
    for i in range(4):
        c = Canvas(16, 16)
        for k in range(4):
            x = 1 + ((k * 4 + i * 1) % 14)
            y = 7 + (1 if (k + i) % 2 else 0)
            c.rect(x, y, x + 2, y + 2, OL)
            c.rect(x + 1 if False else x, y, x + 1, y + 1, G2)
            c.set(x + 1, y + 1, G1)
            c.set(x, y, G3 if (k + i) % 2 == 0 else G2)
        c.rect(0, 8 if False else 9, 15, 9, None)
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
    sheets = {"pipeline-idle.png": (idle(), "idle", 6.0, 0.667, 32), "pipeline-attack.png": (attack(), "attack", 12.0, 0.5, 32),
              "pipeline-fx.png": (fx(), "fx", 10.0, 0.4, 16)}
    mpath = os.path.join(OUT, "MANIFEST.json")
    man = json.load(open(mpath))
    man["entries"] = [e for e in man["entries"] if not e["file"].startswith("pipeline-")]
    for name, (frames, role, fps, dur, size) in sheets.items():
        data = png(frames[0].sheet(frames), S)
        open(os.path.join(OUT, name), "wb").write(data)
        fp = size * S
        man["entries"].append({"file": name, "design": "original", "role": role, "frames": len(frames), "durationSeconds": dur,
                               "fps": fps, "sheetPixels": [fp * len(frames), fp], "framePixels": [fp, fp],
                               "sha256": hashlib.sha256(data).hexdigest(), "generator": "tools/sprites/gen_pipeline.py"})
    man["entries"].sort(key=lambda e: e["file"])
    json.dump(man, open(mpath, "w"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    main()
