namespace SideWar.Core.Td;

/// <summary>
/// Roteiros do SanityBot por fase e perfil (DSL em <see cref="TdSanityBot"/>). X lógico preferido: o inimigo entra em 1200 e anda
/// até 0; o layout típico libera ~62-325, ~400-737 e ~812-1125 (1920 DIP). Torre N = N-ésima construção do roteiro.
/// </summary>
internal static class TdBotPlans
{
    public static TdSanityBot.Step[] For(int stage, TdBotProfile profile) => Parse(Script(stage, profile));

    private static string Script(int stage, TdBotProfile p) => (stage, p) switch
    {
        // ---------------------------------------------------------------- fase 1 (Raio, Gelo, Canhão)
        (1, TdBotProfile.Novato) => "+bolt@900 +bolt@600 +cannon@750 u1 u2 u3 u1 u2 u3 +bolt@450 u4 u4 +cannon@1000 u5 u5",
        (1, TdBotProfile.Medio) => "+bolt@900 +frost@800 +bolt@600 u1 u2 u3 u1 u3 u2 r1B r3B +cannon@450 u4 u4 +bolt@1000 u5 u5",
        (1, TdBotProfile.Otimizado) => "+bolt@900 +frost@800 +bolt@600 u1 u2 u3 u1 u3 u2 r1B r3B r2B +cannon@450 u4 u4 +bolt@1000 u5 u5 c1 c3 c2",
        (1, TdBotProfile.SemContra) => "+bolt@900 +bolt@600",

        // ---------------------------------------------------------------- fase 2 (blindados; balanceador)
        (2, TdBotProfile.Novato) => "+cannon@700 +bolt@900 +cannon@550 +bolt@450 +cannon@1000 u1 u2 u3 u4 u5 u1 u2 u3 u4 u5",
        (2, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +frost@800 +bolt@550 u1 u2 u1 u2 r2B r1A u4 u4 +balancer@600 u5 u5 +bolt@1000 u6 u6",
        (2, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +frost@800 +bolt@550 u1 u2 u1 u2 r2B r1A u4 u4 r4B +balancer@600 u3 u3 r3A +bolt@1000 c2 c1 u6 u6",
        (2, TdBotProfile.SemContra) => "+bolt@900 +bolt@700 +bolt@550 u1 u2 u3 u1 u2 u3 +bolt@450 u4 u4 +bolt@1000 u5 u5 +bolt@300 u6 u6",

        // ---------------------------------------------------------------- fase 3 (enxames, tanques, Ransomware)
        (3, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 u1 u2 u3 u1 u2 u3 +cannon@450 u4 u4 +bolt@1000 u5 u5 +bolt@300 u6 u6",
        (3, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +cannon@800 +frost@550 u1 u2 u3 +bolt@450 +bolt@1000 u1 u2 u3 u5 u6 r2A r3A r1A u5 u6",
        (3, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +cannon@800 +frost@550 u1 u2 u3 +bolt@450 +bolt@1000 u1 u2 u3 u5 u6 r2A r3A r1A u5 u6 u4 u4 r4B r5B r6A c2 c3 c1",
        (3, TdBotProfile.SemContra) => "+bolt@900 +bolt@700 +bolt@550 u1 u2 u3 u1 u2 u3 +bolt@450 u4 u4 +bolt@1000 u5 u5 +bolt@300 u6 u6",

        // ---------------------------------------------------------------- fase 4 (voadores; Scale Set)
        (4, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 u1 u2 u3 u1 u2 u3 +cannon@450 u4 u4 +bolt@1000 u5 u5 +bolt@300 u6 u6",
        (4, TdBotProfile.Medio) => "+bolt@900 +frost@800 +cannon@700 +bolt@550 u1 u1 u3 u3 r3A r1B u4 u4 r4B u2 u2 +scaleset@650 u5 u5 +bolt@1000 u6 u6",
        (4, TdBotProfile.Otimizado) => "+bolt@900 +frost@800 +cannon@700 +bolt@550 u1 u1 u3 u3 r3A r1B u4 u4 r4B u2 u2 +scaleset@650 u5 u5 +bolt@1000 u6 u6 r6B r5A c1 c3",
        (4, TdBotProfile.SemContra) => "+cannon@900 +balancer@700 +cannon@550 u1 u2 u3 u1 u2 u3 +cannon@450 u4 u4 +balancer@1000 u5 u5",

        // ---------------------------------------------------------------- fase 5 (regeneração, drenagem, Botnet)
        (5, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 u1 u2 u3 u1 u2 u3 +cannon@450 u4 u4 +bolt@1000 u5 u5 +bolt@300 u6 u6",
        (5, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 u1 u2 u3 u4 +frost@1000 +cannon@450 +bolt@350 u1 u2 u3 u4 r1A r2A u6 u6 u7 u7",
        (5, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 u1 u2 u3 u4 +frost@1000 +cannon@450 +bolt@350 u1 u2 u3 u4 r1A r2A r3B r4A u6 u6 r6A u7 u7 r7A u5 u5 r5B c1 c2 c3",
        (5, TdBotProfile.SemContra) => "+frost@900 +frost@700 +cannon@550 u1 u2 u3 u1 u2 u3 +frost@450 u4 u4 +cannon@1000 u5 u5",

        // ---------------------------------------------------------------- fase 6 (prova final)
        (6, TdBotProfile.Novato) => "+bolt@900 +cannon@700 +bolt@550 u1 u2 u3 u1 u2 u3 +cannon@450 u4 u4 +bolt@1000 u5 u5 +bolt@300 u6 u6",
        (6, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 u1 u2 u3 u4 +frost@1000 +cannon@450 +bolt@350 u1 u2 u3 u4 r1A r2A u6 u6 u7 u7",
        (6, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +bolt@800 +bolt@550 u1 u2 u3 u4 +frost@1000 +cannon@450 +bolt@350 +scaleset@600 u1 u2 u3 u4 r1A r2A r3B r4A u6 u6 r6A u7 u7 r7A u8 u8 r8B u5 u5 r5B c1 c2 c3",
        (6, TdBotProfile.SemContra) => "+bolt@900 +frost@700 +bolt@550 u1 u2 u3 u1 u3 +frost@450 u4 u4 +bolt@1000 u5 u5",

        _ => "",
    };

    private static TdSanityBot.Step[] Parse(string script)
    {
        var list = new List<TdSanityBot.Step>();
        foreach (var tok in script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (tok[0])
            {
                case '+':
                {
                    int at = tok.IndexOf('@');
                    list.Add(new('b', tok[1..at], int.Parse(tok[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture), 0, ""));
                    break;
                }
                case 'u': list.Add(new('u', "", 0, int.Parse(tok[1..], System.Globalization.CultureInfo.InvariantCulture), "")); break;
                case 'c': list.Add(new('c', "", 0, int.Parse(tok[1..], System.Globalization.CultureInfo.InvariantCulture), "")); break;
                case 'r': list.Add(new('r', "", 0, int.Parse(tok[1..^1], System.Globalization.CultureInfo.InvariantCulture), tok[^1..])); break;
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
