namespace SideWar.Core.Td;

/// <summary>
/// Roteiros do SanityBot por fase e perfil (DSL em <see cref="TdSanityBot"/>). X lógico preferido: o inimigo entra em 1200 e anda
/// até 0; o layout típico libera ~62-325, ~400-737 e ~812-1125 (1920 DIP). Torre N = N-ésima construção do roteiro.
/// </summary>
internal static class TdBotPlans
{
    public static TdSanityBot.Step[] For(int stage, TdBotProfile profile) => Parse(Script(stage, profile) + Runes(stage, profile));

    private static string Runes(int stage, TdBotProfile profile)
    {
        if (profile == TdBotProfile.SemContra) return "";
        string sector = stage is 2 or 3 ? "cannon" : "bolt";
        if (profile == TdBotProfile.Novato) return " rune bolt.s1 rune cannon.s1";
        string army = " +frost@850 +cannon@950 +bolt@650 +frost@500 +cannon@420 +bolt@1050 +frost@300 +bolt@200";
        return (profile == TdBotProfile.Otimizado && stage != 4 ? " rune " + sector + ".n2 rune frost.n1" : "") + " runeSector " + sector + " 4" + army + (profile == TdBotProfile.Otimizado && stage == 1 ? " sell cannon" : "") + " runeSector " + sector + " 9"
            + (profile == TdBotProfile.Otimizado ? " rune cannon.n2 rune border.bolt-frost" : "");
    }

    private static string Script(int stage, TdBotProfile p) => (stage, p) switch
    {
        // ---------------------------------------------------------------- fase 1 (Raio, Gelo, Canhão)
        (1, TdBotProfile.Novato) => "+bolt@900 +bolt@600 +cannon@750 +bolt@450 +cannon@1000",
        (1, TdBotProfile.Medio) => "+bolt@900 +frost@800 +bolt@600 +cannon@450 +bolt@1000",
        (1, TdBotProfile.Otimizado) => "+bolt@900 +frost@800 +bolt@600 +cannon@450 +bolt@1000",
        (1, TdBotProfile.SemContra) => "+bolt@900 +bolt@600",

        // ---------------------------------------------------------------- fase 2 (blindados; balanceador)
        (2, TdBotProfile.Novato) => "+cannon@700 +bolt@900 +cannon@550 +bolt@450 +cannon@1000",
        (2, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +frost@800 +bolt@550 +balancer@600 +bolt@1000",
        (2, TdBotProfile.Otimizado) => "+cannon@700 +bolt@900 +cannon@550 +frost@800 +cannon@1000 +bolt@450",
        (2, TdBotProfile.SemContra) => "+bolt@900 +bolt@700 +bolt@550 +bolt@450 +bolt@1000 +bolt@300",

        // ---------------------------------------------------------------- fase 3 (enxames, tanques, Ransomware)
        (3, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 +cannon@450 +bolt@1000 +bolt@300",
        (3, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +cannon@800 +frost@550 +bolt@450 +bolt@1000",
        (3, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +cannon@800 +frost@550 +bolt@450 +bolt@1000",
        (3, TdBotProfile.SemContra) => "+bolt@900 +bolt@700 +bolt@550 +bolt@450 +bolt@1000 +bolt@300",

        // ---------------------------------------------------------------- fase 4 (voadores; Scale Set)
        (4, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 +cannon@450 +bolt@1000 +bolt@300",
        (4, TdBotProfile.Medio) => "+bolt@900 +frost@800 +cannon@700 +bolt@550 +scaleset@650 +bolt@1000",
        (4, TdBotProfile.Otimizado) => "+bolt@900 +frost@800 +cannon@700 +bolt@550 +scaleset@650 +bolt@1000",
        (4, TdBotProfile.SemContra) => "+cannon@900 +balancer@700 +cannon@550 +cannon@450 +balancer@1000",

        // ---------------------------------------------------------------- fase 5 (regeneração, drenagem, Botnet)
        (5, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 +cannon@450 +bolt@1000 +bolt@300",
        (5, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 +frost@1000 +cannon@450 +bolt@350",
        (5, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 +frost@1000 +cannon@450 +bolt@350",
        (5, TdBotProfile.SemContra) => "+frost@900 +frost@700 +cannon@550 +frost@450 +cannon@1000",

        // ---------------------------------------------------------------- fase 6 (prova final)
        (6, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 +cannon@450 +bolt@1000 +bolt@300",
        (6, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 +frost@1000 +cannon@450 +bolt@350",
        (6, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 +frost@1000 +cannon@450 +bolt@350 +scaleset@600",
        (6, TdBotProfile.SemContra) => "+bolt@900 +frost@700 +bolt@550 +frost@450 +bolt@1000",

        _ => "",
    };

    internal static TdSanityBot.Step[] Parse(string script)
    {
        var list = new List<TdSanityBot.Step>();
        var tokens = script.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            var tok = tokens[i];
            if (tok == "rune") { list.Add(new('r', "", 0, 0, tokens[++i])); continue; }
            if (tok == "runeSector") { string sector = tokens[++i]; int count = int.Parse(tokens[++i], System.Globalization.CultureInfo.InvariantCulture); list.Add(new('s', sector, 0, count, "")); continue; }
            if (tok == "sell") { list.Add(new('v', tokens[++i], 0, 0, "")); continue; }
            switch (tok[0])
            {
                case '+':
                {
                    int at = tok.IndexOf('@');
                    list.Add(new('b', tok[1..at], int.Parse(tok[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture), 0, ""));
                    break;
                }
                case 't':
                {
                    int eq = tok.IndexOf('=');
                    list.Add(new('t', "", 0, int.Parse(tok[1..eq], System.Globalization.CultureInfo.InvariantCulture), tok[(eq + 1)..]));
                    break;
                }
                default: throw new ArgumentException($"Passo inválido no roteiro: {tok}");
            }
        }
        return [.. list];
    }
}
