#!/usr/bin/env python3
"""Importa os APNGs do artboard (requer Pillow). Ataque reutiliza quadros do APNG.

O artboard não fornece folhas de ataque nem de efeito; os efeitos originais permanecem.
"""
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageSequence

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets/source/design6-towers"
OUT = ROOT / "assets/source/anim"
BLOBS = {
    "queue": "1940746e20e0624b93af1e3f83c08f15",
    "backup": "9235a2af94535bc6bf30d8dbc3c6f7a5",
    "aks": "12f030feca55adf43b15e83e7b9bd97d",
    "bastion": "3b037f26f57d033b7ff7371012283614",
    "vault": "af0d554eab41836601649c0a64347dde",
}


def main():
    manifest_path = OUT / "MANIFEST.json"
    manifest = json.loads(manifest_path.read_text())
    entries = {entry["file"]: entry for entry in manifest["entries"]}
    for name, blob in BLOBS.items():
        with Image.open(SOURCE / (name + ".png")) as source:
            frames = [frame.convert("RGBA").resize((128, 128), Image.Resampling.NEAREST)
                      for frame in ImageSequence.Iterator(source)]
        if len(frames) != 4:
            raise ValueError(f"{name}: esperado APNG de quatro quadros")
        for role, order in (("idle", (0, 1, 2, 3)), ("attack", (0, 1, 2, 3, 2, 1))):
            sheet = Image.new("RGBA", (128 * len(order), 128))
            for i, index in enumerate(order):
                sheet.paste(frames[index], (128 * i, 0))
            file = name + "-" + role + ".png"
            sheet.save(OUT / file)
            entry = entries[file]
            entry.update(design="Torres — todas as 18 (animadas)", blob=blob,
                         sha256=hashlib.sha256((OUT / file).read_bytes()).hexdigest(),
                         generator="tools/sprites/import_design6_towers.py")
    manifest_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False))


if __name__ == "__main__":
    main()
