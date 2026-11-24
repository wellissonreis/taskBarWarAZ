using System.Globalization;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

public sealed partial class TdUiController
{
    /// <summary>Linha de dicas das estatísticas do jogador (índice = <c>Arg</c> do item "jg-stat").</summary>
    private static readonly string[] JogoStatTips =
    [
        "Coroas: fases vencidas sem vazamentos",
        "Estrelas: até 3 por fase",
        "Fases vencidas",
        "Melhor recorde de pontuação em uma fase",
    ];

    /// <summary>
    /// Aba Jogo (artboard "Jogo"/"Menu (interativo)", modal 720×480, conteúdo a partir de y=100): o jogador em destaque (brasão, patente, escada, XP e
    /// estatísticas) e, embaixo, habilidades e ações compactas à esquerda e configurações em tiles à direita. Reaproveita os itens do BuildMenu
    /// (ações, opções, confirmações); só reposiciona e marca com SArg "jg-*" o que o renderer desenha diferente.
    /// </summary>
    private void AddJogoTab(TdUiItem[] src, List<TdUiItem> items)
    {
        const float lx = 16f, rx = 328f;
        TdUiItem Put(TdUiItem it, float x, float y, float w, float h) => it with { Rect = new DipRect(x, y, w, h) };
        var inv = CultureInfo.InvariantCulture;
        var rank = _host.Rank;
        int n = _host.StageCount, crowns = 0, stars = 0, won = 0, best = 0;
        for (int id = 1; id <= n; id++)
        {
            if (_host.StageCrown(id)) crowns++;
            stars += _host.StageStars(id);
            int score = _host.StageBestScore(id);
            if (score > 0) won++;
            best = Math.Max(best, score);
        }
        string rankName = TdRank.DisplayName(rank.Current);
        string[] stat = [crowns.ToString(inv), stars.ToString(inv), won.ToString(inv), best.ToString(inv)];
        string[] total = [$"/{n}", $"/{n * 3}", $"/{n}", ""];
        string[] label = ["Coroas", "Estrelas", "Fases vencidas", "Melhor recorde"];
        // Jogador (card 688×178): brasão 144, patente, escada de coroas, barra de XP e a grade 2×2 de estatísticas.
        foreach (var it in src)
        {
            if (it is not { Action: TdUiAction.None, Kind: TdUiKind.Bar }) continue;
            items.Add(Put(it, lx, 100, 688, 178) with { SArg = "rank-card", Arg = (int)rank.Current, Text = rankName, Tip = $"Brasão {rankName}", Sub = crowns.ToString(inv),
                Right = rank.Next is { } next ? $"{rank.Total} / {rank.NextThreshold} XP|faltam {Math.Max(0, rank.NextThreshold - rank.Total)} XP para {TdRank.DisplayName(next)}" : $"{rank.Total} XP|Patente máxima" });
            break;
        }
        for (int i = 0; i < 6; i++)
            items.Add(new(new DipRect(188 + i * 33, 195, 28, 26), TdUiKind.Label, "", Arg: i, SArg: "rank-step", Tip: $"Brasão {TdRank.DisplayName((TdRankLevel)i)} · NV {i + 1}"));
        for (int i = 0; i < 4; i++)
            items.Add(new(new DipRect(478 + i % 2 * 109, 114 + i / 2 * 78, 103, 72), TdUiKind.Label, stat[i], Arg: i, Right: total[i], Sub: label[i], Fill: (float)(int)rank.Current, SArg: "jg-stat", Tip: JogoStatTips[i]));

        items.Add(new(new DipRect(lx, 288, 300, 14), TdUiKind.Label, "HABILIDADES", SArg: "sec"));
        items.Add(new(new DipRect(rx, 288, 376, 14), TdUiKind.Label, "CONFIGURAÇÕES", SArg: "sec"));
        items.Add(new(new DipRect(lx, 352, 60, 14), TdUiKind.Label, "AÇÕES", SArg: "sec"));

        items.Add(new(new DipRect(rx, 308 + 3 * 35, 121, 33), TdUiKind.Label, "Velocidade", SArg: "jg-tile-speed"));
        items.Add(new(new DipRect(rx + 127, 308 + 3 * 35, 248, 33), TdUiKind.Button, "Todas as configurações", TdUiAction.SettingsOpen, Icon: TdIcon.Gear, SArg: "cfg-link"));
        int ab = 0;
        foreach (var it in src)
        {
            switch (it.Action)
            {
                case TdUiAction.ResumeAbility: items.Add(Put(it, lx + ab++ * 102, 308, 96, 36) with { Tip = it.Right }); break;
                case TdUiAction.Preview: items.Add(Put(it, lx, 372, 147, 32) with { Icon = TdIcon.FastForward, SArg = "menu-action" }); break;
                case TdUiAction.TowersOpen: items.Add(Put(it, lx + 153, 372, 147, 32) with { Icon = TdIcon.Target, SArg = "menu-action" }); break;
                case TdUiAction.RestartPrompt: items.Add(Put(it, lx, 410, 147, 32) with { Icon = TdIcon.Restore, SArg = "menu-action" }); break;
                case TdUiAction.Restart: items.Add(Put(it, lx, 410, 71, 32) with { SArg = "menu-action" }); break;
                case TdUiAction.RestartCancel: items.Add(Put(it, lx + 76, 410, 71, 32) with { SArg = "menu-action" }); break;
                case TdUiAction.None when it.Text == "Reiniciar? Torres ficam": items.Add(Put(it, lx + 64, 352, 150, 14) with { SArg = "jg-warn", Tone = TdTone.Warn }); break;
                case TdUiAction.Exit: items.Add(Put(it, lx + 153, 410, 147, 32) with { SArg = "menu-action" }); break;
                case TdUiAction.WipePrompt: items.Add(Put(it, 196, 350, 120, 18) with { FontSize = 10f }); break;
                case TdUiAction.WipeConfirm: items.Add(Put(it, 196, 350, 58, 18) with { FontSize = 10f }); break;
                case TdUiAction.WipeCancel: items.Add(Put(it, 258, 350, 58, 18) with { FontSize = 10f }); break;
                case TdUiAction.None when it.Text == "Apagar todo o progresso?": items.Add(Put(it, lx + 50, 352, 144, 14) with { SArg = "jg-warn", Tone = TdTone.Warn }); break;
                case TdUiAction.GameOption:
                {
                    int slot = it.Arg switch { 0 => 0, 1 => 1, 2 => 2, 4 => 3, 5 => 4, 6 => 5, 7 => 6, 3 => 7, _ => 8 };
                    items.Add(Put(it, rx + slot % 3 * 127, 308 + slot / 3 * 35, 121, 33) with { SArg = "jg-tile", Tip = it.Right });
                    break;
                }
                case TdUiAction.SetSpeed:
                    items.Add(Put(it, rx + 8 + (it.Arg - 1) * 33, 308 + 3 * 35 + 15, 30, 17));
                    break;
            }
        }
    }
}
