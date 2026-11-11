#!/usr/bin/env python3
"""Generates the placeholder tray icon (PNG-in-ICO, 16/20/24/32/48). Deterministic; real art replaces it later."""
import struct, sys, zlib

def png(size):
    rows = []
    c = (size - 1) / 2
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            d = max(abs(x - c), abs(y - c))
            if d > size * 0.47: px = (0, 0, 0, 0)
            elif d > size * 0.38: px = (0x4F, 0xC3, 0xF7, 255)       # accent border
            elif (x + y) % max(2, size // 8) < max(1, size // 16) and y > size * 0.3: px = (0x4F, 0xC3, 0xF7, 255)
            else: px = (0x14, 0x1C, 0x2B, 255)                        # dark body
            row += bytes(px)
        rows.append(bytes(row))
    raw = b"".join(rows)
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")

sizes = [16, 20, 24, 32, 48]
images = [png(s) for s in sizes]
out = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, img in zip(sizes, images):
    out += struct.pack("<BBBBHHII", s, s, 0, 0, 1, 32, len(img), offset)
    offset += len(img)
out += b"".join(images)
open(sys.argv[1], "wb").write(out)
