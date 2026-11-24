#!/usr/bin/env python3
"""Gera as folhas originais de vault; use python3 tools/sprites/gen_vault.py."""
import hashlib, json, os
from gen_eventgrid import Canvas, png, line, OL, D1, D2, D3, M1, M2, M3, ROOT, OUT, S

def rgb(s): return tuple(bytes.fromhex(s))
G1, G2, G3 = map(rgb, ('96621c', 'f5b335', 'fff0b0'))

def tower(phase=0, glow=False, flash=False):
    c = Canvas(32, 32)
    c.ellipse(16, 26, 14, 5, OL); c.ellipse(16, 25, 13, 4, D3)
    c.ellipse(16, 26, 12, 4, D2); c.rect(6, 24, 26, 24, M1)
    c.rect(13, 29, 19, 29, G1)
    c.box(7, 6, 25, 24, D2, M3, D1)
    c.box(10, 9, 22, 22, M1, M3, D2)
    c.ellipse(16, 15, 5, 5, OL); c.ellipse(16, 15, 4, 4, G1)
    c.ellipse(16, 15, 2, 2, D2)
    c.rect(16, 11, 16, 19, G2); c.rect(12, 15, 20, 15, G2)
    c.set(16, 15, G3 if glow else G2)
    c.rect(8, 10, 9, 12, M3); c.rect(8, 19, 9, 21, M3)
    if flash:
        for x, y in ((3, 7), (5, 5), (6, 9), (2, 10)): c.set(x, y, G3)
    return c

def fx():
    frames = []
    for i in range(4):
        c = Canvas(16, 16)
        c.ellipse(8, 8, 4+i%2, 4+i%2, G1)
        c.rect(7, 3, 9, 13, G2); c.rect(3, 7, 13, 9, G2)
        c.ellipse(8, 8, 1, 1, G3)
        for x,y in ((2,2),(13,2),(2,13),(13,13)):
            if (x+y+i)%2: c.set(x,y,G2)
        frames.append(c)
    return frames

def main():
    sheets = {"idle": ([tower(i, i == 2) for i in range(4)], 6.0, .667, 32),
              "attack": ([tower(i, i < 4, i == 2) for i in range(6)], 12.0, .5, 32),
              "fx": (fx(), 10.0, .4, 16)}
    path = os.path.join(OUT, "MANIFEST.json")
    with open(path) as f: manifest = json.load(f)
    manifest["entries"] = [e for e in manifest["entries"] if not e["file"].startswith("vault-")]
    for role, (frames, fps, duration, size) in sheets.items():
        name = "vault-" + role + ".png"
        data = png(frames[0].sheet(frames), S)
        with open(os.path.join(OUT, name), "wb") as f: f.write(data)
        pixels = size * S
        manifest["entries"].append({"file": name, "design": "original", "role": role, "frames": len(frames),
            "durationSeconds": duration, "fps": fps, "sheetPixels": [pixels * len(frames), pixels],
            "framePixels": [pixels, pixels], "sha256": hashlib.sha256(data).hexdigest(),
            "generator": "tools/sprites/gen_vault.py"})
    manifest["entries"].sort(key=lambda e: e["file"])
    with open(path, "w") as f: json.dump(manifest, f, indent=1, ensure_ascii=False)

if __name__ == "__main__": main()
