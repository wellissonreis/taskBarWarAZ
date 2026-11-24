#!/usr/bin/env python3
"""Gera src/SideWar.Core/Content/td-waves.json (fases longas, INFINITO) a partir das ondas autorais curtas.

As ondas autorais (tools/content/td-waves-authored.json: 10 a 15 ondas por fase) são MOLDES ordenados por dificuldade. Cada fase
longa percorre os moldes devagar (interpolação linear de quantidades entre moldes vizinhos), com respiro a cada 5 ondas, pico a
cada 10 e a última onda autoral (chefe) no fim. Determinístico: sem aleatoriedade. Uso: python3 tools/content/gen_td_waves.py
"""
import json, math, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
AUTHORED = ROOT / "tools/content/td-waves-authored.json"
OUT = ROOT / "src/SideWar.Core/Content/td-waves.json"
STAGES = ROOT / "src/SideWar.Core/Content/td-stages.json"

# ondas por fase (alvo: ~25-40 min de jogo normal por fase; calibrado com o probe do SanityBot)
LENGTHS = {1: 60, 2: 65, 3: 80, 4: 85, 5: 70, 6: 62}
HEAD = {1: 7}  # ondas autorais mantidas ao pé da letra no começo (início calmo da fase 1, RITMO-INICIO)
REST_EVERY = 5
PEAK_EVERY = 10
MAX_ENEMIES = 32
MIN_ENEMIES = 3


def span(g):
    return g["offset"] + g["interval"] * (g["count"] - 1)


def counts(t):
    return {g["enemy"]: g for g in t["groups"]}


def gaps(t):
    """Folga de cada grupo do molde em relação ao fim do grupo anterior (a 1ª folga é o deslocamento inicial)."""
    out, prev_end = {}, None
    for g in t["groups"]:
        out[g["enemy"]] = g["offset"] if prev_end is None else g["offset"] - prev_end
        prev_end = span(g)
    return out


def blend(t0, t1, f, scale):
    """Mistura dois moldes: quantidade interpolada por inimigo; deslocamentos recalculados em sequência com a folga do molde de origem."""
    c0, c1 = counts(t0), counts(t1)
    g0, g1 = gaps(t0), gaps(t1)
    order = [g["enemy"] for g in t0["groups"]] + [g["enemy"] for g in t1["groups"] if g["enemy"] not in c0]
    groups = []
    for e in order:
        a = c0[e]["count"] if e in c0 else 0
        b = c1[e]["count"] if e in c1 else 0
        n = (a * (1 - f) + b * f) * scale
        src = c0.get(e) or c1[e]
        gap = g0[e] if e in c0 else g1[e]
        groups.append({"enemy": e, "count": n, "interval": src["interval"], "gap": gap})
    return groups


def build(stage, templates, n_total):
    head = HEAD.get(stage, 0)
    body = templates[max(head - 1, 0):-1]
    boss = templates[-1]
    m = len(body)
    waves = [json.loads(json.dumps(t)) for t in templates[:head]]
    for k in range(head + 1, n_total):
        u = (k - head - 1) / (n_total - head - 2) * (m - 1)
        i = min(int(math.floor(u)), m - 2) if m > 1 else 0
        f = u - i if m > 1 else 0.0
        t0 = body[i]
        t1 = body[i + 1] if m > 1 else body[i]
        rest = k % REST_EVERY == 0
        peak = (k % PEAK_EVERY == 0) and not rest
        scale = 0.5 if rest else (1.25 if peak else 1.0)
        bonus = round(t0["bonusFactor"] * (1 - f) + t1["bonusFactor"] * f, 2)
        groups = blend(t0, t1, f, scale)
        # inteiros, mínimo 1 por grupo mantido; total em [MIN, MAX]
        for g in groups:
            g["count"] = int(math.floor(g["count"] + 0.5))
        groups = [g for g in groups if g["count"] >= 1]
        if not groups:
            groups = [{"enemy": body[0]["groups"][0]["enemy"], "count": MIN_ENEMIES, "interval": body[0]["groups"][0]["interval"], "gap": 0.0}]
        total = sum(g["count"] for g in groups)
        while total > MAX_ENEMIES:
            big = max(groups, key=lambda g: g["count"]); big["count"] -= 1; total -= 1
        while total < MIN_ENEMIES:
            groups[0]["count"] += 1; total += 1
        cursor = None
        for g in groups:
            g["offset"] = round(max(0.0, g["gap"] if cursor is None else cursor + g["gap"]), 2)
            cursor = g["offset"] + g["interval"] * (g["count"] - 1)
        out_groups = [{"enemy": g["enemy"], "count": g["count"], "offset": g["offset"], "interval": g["interval"]} for g in groups]
        if rest:
            bonus = max(bonus, 1.5)
        waves.append({"number": k, "rest": rest, "bonusFactor": bonus if not rest else max(1.5, bonus), "groups": out_groups})
    last = json.loads(json.dumps(boss))
    last["number"] = n_total
    last["rest"] = False
    waves.append(last)
    return waves


def main():
    auth = json.loads(AUTHORED.read_text())
    out = {"stages": []}
    for s in auth["stages"]:
        sid = s["stage"]
        n = LENGTHS[sid]
        out["stages"].append({"stage": sid, "waves": build(sid, s["waves"], n)})
    # JSON compacto: uma onda por linha
    lines = ['{', '  "stages": [']
    for si, s in enumerate(out["stages"]):
        lines.append('    {')
        lines.append(f'      "stage": {s["stage"]},')
        lines.append('      "waves": [')
        for wi, w in enumerate(s["waves"]):
            comma = "," if wi < len(s["waves"]) - 1 else ""
            lines.append("        " + json.dumps(w, ensure_ascii=False) + comma)
        lines.append('      ]')
        lines.append('    }' + ("," if si < len(out["stages"]) - 1 else ""))
    lines += ['  ]', '}']
    OUT.write_text("\n".join(lines) + "\n")
    st = json.loads(STAGES.read_text())
    for s in st["stages"]:
        s["waves"] = LENGTHS[s["id"]]
    # td-stages.json é editado à mão: só confere
    for s in st["stages"]:
        assert s["waves"] == LENGTHS[s["id"]]
    print({k: v for k, v in LENGTHS.items()})


if __name__ == "__main__":
    main()
