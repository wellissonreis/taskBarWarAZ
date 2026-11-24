using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

public sealed partial class TdUiController
{
    private void AddBestiary(List<TdUiItem> items)
    {
        var entries = TdBestiary.Entries;
        _selectedEnemy = Math.Clamp(_selectedEnemy, 0, entries.Length - 1);
        int frame = (int)Math.Floor(_now() * 6) % 4;
        int knownBase = entries.Count(e => !e.Boss && _host.EncounterCount(e.Id) > 0);
        int knownBoss = entries.Count(e => e.Boss && _host.EncounterCount(e.Id) > 0);
        items.Add(new(new DipRect(16, 100, 404, 18), TdUiKind.Label, "INIMIGOS", Right: $"{knownBase}/6", SArg: "best-heading"));
        items.Add(new(new DipRect(16, 205, 404, 18), TdUiKind.Label, "CHEFÕES", Right: $"{knownBoss}/8", SArg: "best-heading"));
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            bool known = _host.EncounterCount(e.Id) > 0;
            int n = e.Boss ? i - 6 : i;
            float x = e.Boss ? 16 + n % 4 * 102 : 16 + n * 68;
            float y = e.Boss ? 227 + n / 4 * 84 : 120;
            float w = e.Boss ? 96 : 62;
            items.Add(new(new DipRect(x, y, w, 76), TdUiKind.Button, known ? e.Name : "???",
                TdUiAction.BestiarySelect, i, e.Art, Right: frame.ToString(System.Globalization.CultureInfo.InvariantCulture), Selected: i == _selectedEnemy, Fill: known ? 1 : 0));
        }
        items.Add(new(new DipRect(16, 402, 404, 34), TdUiKind.Label,
            "Passe o mouse para ver as variações. Novos inimigos aparecem ao enfrentá-los.", SArg: "best-hint"));

        var selected = entries[_selectedEnemy];
        int count = _host.EncounterCount(selected.Id);
        items.Add(new(new DipRect(432, 100, 272, 336), TdUiKind.Label, "", SArg: "best-panel"));
        if (count == 0)
        {
            int stage = _match().Content.Waves.Stages.Where(s => s.Waves.Any(w => w.Groups.Any(g => g.Enemy == selected.Id)))
                .Select(s => s.Stage).DefaultIfEmpty(selected.Stage).Min();
            items.Add(new(new DipRect(460, 200, 216, 115), TdUiKind.Label, $"Aparece a partir da fase {stage}", Icon: TdIcon.Lock, SArg: "best-locked"));
        }
        else
        {
            items.Add(new(new DipRect(444, 112, 80, 80), TdUiKind.Label, "", Arg: frame, SArg: "best-image:" + selected.Art));
            items.Add(new(new DipRect(532, 119, 160, 24), TdUiKind.Label, selected.Name, SArg: "best-title"));
            items.Add(new(new DipRect(532, 148, 160, 18), TdUiKind.Label, selected.Boss ? "CHEFÃO" : "INIMIGO", SArg: "best-tag"));
            items.Add(new(new DipRect(532, 171, 160, 18), TdUiKind.Label, $"{count} encontrados", SArg: "best-meta"));
            items.Add(new(new DipRect(444, 203, 246, 52), TdUiKind.Label, selected.Description, SArg: "best-description"));
            items.Add(new(new DipRect(444, 262, 246, 18), TdUiKind.Label, "Vida", Arg: selected.Life, SArg: "best-stat"));
            items.Add(new(new DipRect(444, 282, 246, 18), TdUiKind.Label, "Velocidade", Arg: selected.Speed, SArg: "best-stat"));
            items.Add(new(new DipRect(444, 302, 246, 18), TdUiKind.Label, "Armadura", Arg: selected.Armor, SArg: "best-stat"));
            items.Add(new(new DipRect(444, 338, 120, 16), TdUiKind.Label, "FRACO CONTRA", SArg: "best-weak"));
            items.Add(new(new DipRect(570, 338, 120, 16), TdUiKind.Label, "RESISTE A", SArg: "best-resist"));
            AddBestiaryCounters(items, selected.Weak, 444);
            AddBestiaryCounters(items, selected.Resist, 570);
        }

        var hovered = entries.FirstOrDefault(e => e.Art == _hoverEnemy);
        if (hovered is null) return;
        int hoverIndex = Array.IndexOf(entries, hovered);
        int vindex = hovered.Boss ? hoverIndex - 6 : hoverIndex;
        float hx = hovered.Boss ? 16 + vindex % 4 * 102 : 16 + vindex * 68;
        float hy = hovered.Boss ? 227 + vindex / 4 * 84 : 120;
        float px = Math.Clamp(hx, 16, 272), py = hy > 200 ? hy - 76 : hy + 77;
        items.Add(new(new DipRect(px, py, 144, 74), TdUiKind.Label, "VARIAÇÕES", SArg: "best-popup"));
        for (int i = 0; i < hovered.Variants.Length; i++)
        {
            var v = hovered.Variants[i];
            bool known = _host.EncounterCount(v.Id) > 0;
            items.Add(new(new DipRect(px + 7 + i * 48, py + 22, 43, 43), TdUiKind.Label, known ? v.Name : "???",
                Arg: frame, SArg: "best-variant:" + v.Art, Fill: known ? 1 : 0));
        }
    }

    private static void AddBestiaryCounters(List<TdUiItem> items, string names, float x)
    {
        int column = 0;
        foreach (var name in names.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            string id = name switch
            {
                "Raio" => "bolt", "Canhão" => "cannon", "Gelo" => "frost", "Fila" => "queue",
                "Escudo" => "shield", "Front Door" => "frontdoor", "Scale Set" => "scaleset", "Balanceador" => "balancer",
                "Functions" => "functions", "CDN" => "cdn", "Cosmos" => "cosmos", "WAF" => "waf",
                "Sentinel" => "sentinel", "Policy" => "policy", "AKS" => "aks", _ => "",
            };
            if (id.Length == 0) continue;
            items.Add(new(new DipRect(x + column++ * 34, 357, 30, 30), TdUiKind.Label, name, SArg: "best-tower:" + id));
        }
    }
}
