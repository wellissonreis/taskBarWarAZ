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
        (1, TdBotProfile.Novato) => "+bolt@900 +bolt@600 +cannon@750 +bolt@450 +cannon@1000",
        (1, TdBotProfile.Medio) => "+bolt@900 +frost@800 +bolt@600 +cannon@450 +bolt@1000",
        (1, TdBotProfile.Otimizado) => "+bolt@900 +frost@800 +bolt@600 +cannon@450 +bolt@1000",
        (1, TdBotProfile.SemContra) => "+bolt@900 +bolt@600",

        // ---------------------------------------------------------------- fase 2 (blindados; balanceador)
        (2, TdBotProfile.Novato) => "+cannon@700 +bolt@900 +cannon@550 +bolt@450 +cannon@1000",
        (2, TdBotProfile.Medio) => "+bolt@900 +cannon@700 +frost@800 +bolt@550 +balancer@600 +bolt@1000",
        (2, TdBotProfile.Otimizado) => "+bolt@900 +cannon@700 +frost@800 +bolt@550 +balancer@600 +bolt@1000",
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
