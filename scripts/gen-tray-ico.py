#!/usr/bin/env python3
"""Generates the tray icon (PNG-in-ICO, 16/20/24/32/48) from the project art (assets/source/design/tower-bolt.png).

Usage: gen-tray-ico.py <out.ico> [source.png]. Pure Python (no PIL); deterministic. Area-average downscale on
premultiplied alpha, centered on a transparent square canvas.
"""
import os, struct, sys, zlib

DEFAULT_SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "source", "design", "tower-bolt.png")

def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos, idat, w = 8, b"", None
    while pos < len(data):
        n, t = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        if t == b"IHDR":
            w, h, depth, ctype, _, _, interlace = struct.unpack(">IIBBBBB", body)
            assert depth == 8 and ctype == 6 and interlace == 0, "need 8-bit RGBA non-interlaced PNG"
        elif t == b"IDAT": idat += body
        pos += 12 + n
    raw, stride, bpp = zlib.decompress(idat), w * 4, 4
    rows, prev = [], bytearray(stride)
    for y in range(h):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b = prev[i]
            c = prev[i - bpp] if i >= bpp else 0
            if f == 1: line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + ((a + b) >> 1)) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line); prev = line
    return w, h, rows

def resize(src, sw, sh, size):
    """Fit src inside a size x size transparent canvas (area average, premultiplied alpha)."""
    scale = size / max(sw, sh)
    dw, dh = max(1, round(sw * scale)), max(1, round(sh * scale))
    ox, oy = (size - dw) // 2, (size - dh) // 2
    out = [[(0, 0, 0, 0)] * size for _ in range(size)]
    for y in range(dh):
        y0, y1 = y * sh / dh, (y + 1) * sh / dh
        for x in range(dw):
            x0, x1 = x * sw / dw, (x + 1) * sw / dw
            r = g = b = a = wt = 0.0
            for sy in range(int(y0), min(sh, int(y1 + 0.999))):
                wy = min(y1, sy + 1) - max(y0, sy)
                for sx in range(int(x0), min(sw, int(x1 + 0.999))):
                    wgt = wy * (min(x1, sx + 1) - max(x0, sx))
                    p = src[sy][sx * 4:sx * 4 + 4]
                    al = p[3] / 255
                    r += p[0] * al * wgt; g += p[1] * al * wgt; b += p[2] * al * wgt; a += al * wgt; wt += wgt
            if a > 0:
                out[oy + y][ox + x] = (round(r / a), round(g / a), round(b / a), round(255 * a / wt))
    return out

def png(pixels, size):
    raw = b"".join(b"\x00" + b"".join(bytes(px) for px in row) for row in pixels)
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")

sw, sh, rows = read_png(sys.argv[2] if len(sys.argv) > 2 else DEFAULT_SRC)
sizes = [16, 20, 24, 32, 48]
images = [png(resize(rows, sw, sh, s), s) for s in sizes]
out = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, img in zip(sizes, images):
    out += struct.pack("<BBBBHHII", s, s, 0, 0, 1, 32, len(img), offset)
    offset += len(img)
out += b"".join(images)
open(sys.argv[1], "wb").write(out)
