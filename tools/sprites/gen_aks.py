#!/usr/bin/env python3
"""Gera as folhas originais de aks; use python3 tools/sprites/gen_aks.py."""
import hashlib, json, os
from gen_eventgrid import Canvas, png, line, OL, D1, D2, D3, M1, M2, M3, ROOT, OUT, S

def rgb(s): return tuple(bytes.fromhex(s))
G1, G2, G3 = map(rgb, ('337c9a', '7fe7ff', 'e1faff'))

def tower(phase=0, glow=False, flash=False):
    c = Canvas(32, 32)
    c.ellipse(16, 26, 14, 5, OL); c.ellipse(16, 25, 13, 4, D3)
    c.ellipse(16, 26, 12, 4, D2); c.rect(6, 24, 26, 24, M1)
    c.rect(13, 29, 19, 29, G1)
    c.box(10, 9, 22, 24, D2, M2, D1)
    c.ellipse(16, 10, 7, 6, OL); c.ellipse(16, 10, 6, 5, D3)
    c.ellipse(16, 10, 2, 2, G3 if glow else G2)
    for x,y in ((9,6),(23,6),(16,3),(9,15),(23,15)):
        line(c,16,10,x,y,G1); c.box(x-2,y-2,x+2,y+2,M1,M3,D1); c.set(x,y,G2)
    for x in (12,16,20): c.box(x-1,20,x+1,22,G1,G2,D1)
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
    manifest["entries"] = [e for e in manifest["entries"] if not e["file"].startswith("aks-")]
    for role, (frames, fps, duration, size) in sheets.items():
        name = "aks-" + role + ".png"
        data = png(frames[0].sheet(frames), S)
        with open(os.path.join(OUT, name), "wb") as f: f.write(data)
        pixels = size * S
        manifest["entries"].append({"file": name, "design": "original", "role": role, "frames": len(frames),
            "durationSeconds": duration, "fps": fps, "sheetPixels": [pixels * len(frames), pixels],
            "framePixels": [pixels, pixels], "sha256": hashlib.sha256(data).hexdigest(),
            "generator": "tools/sprites/gen_aks.py"})
    manifest["entries"].sort(key=lambda e: e["file"])
    with open(path, "w") as f: json.dump(manifest, f, indent=1, ensure_ascii=False)

if __name__ == "__main__": main()
