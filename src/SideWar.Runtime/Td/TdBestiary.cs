namespace SideWar.Runtime.Td;

/// <summary>Dados do artboard e ids de simulação para as descobertas.</summary>
public sealed record TdBestiaryVariant(string Art, string Id, string Name);
public sealed record TdBestiaryEntry(string Id, string Art, string Name, string Description, int Stage, bool Boss,
    int Life, int Speed, int Armor, string Weak, string Resist, TdBestiaryVariant[] Variants);
public static class TdBestiary
{
    public static readonly TdBestiaryEntry[] Entries =
    [
        new("swarm", "enxame", "Enxame", "Pequeno e em grupo; morre fácil, mas vem em quantidade.", 1, false, 1, 3, 0, "CDN, Cosmos", "Raio, Functions", [new("enxame_elite", "swarm:elite", "Enxame elite")]),
        new("runner", "rapido", "Ping", "Aranha-robô veloz; atravessa a faixa antes das torres lentas mirarem.", 1, false, 1, 5, 0, "Gelo, Fila", "Canhão", [new("rapido_elite", "runner:elite", "Ping elite")]),
        new("armored", "blindado", "Blindado", "Esteira com placas de metal; ignora tiros fracos.", 2, false, 4, 1, 5, "Canhão", "Raio, Functions, WAF", [new("blindado_elite", "armored:elite", "Blindado elite")]),
        new("flyer", "voador", "Drone", "Voa por cima das torres terrestres.", 2, false, 2, 3, 1, "Raio, Cosmos", "Canhão, Front Door, AKS", [new("voador_elite", "flyer:elite", "Drone elite")]),
        new("leech", "dreno", "Dreno", "Verme sugador que desativa torres de suporte.", 5, false, 2, 2, 1, "Raio", "Escudo, Scale Set", [new("dreno_elite", "leech:elite", "Dreno elite")]),
        new("tank", "duravel", "Bruto", "Golem pesado com muita vida.", 3, false, 5, 1, 3, "Sentinel", "Canhão, Cosmos", [new("duravel_elite", "tank:elite", "Bruto elite")]),
        new("ransomware", "ransomware", "Ransomware", "Mecha bípede com cadeado no peito; sequestra créditos se chegar ao núcleo.", 3, true, 5, 2, 4, "Sentinel, Canhão", "Gelo", [new("ransomware_enf", "ransomware:enraged", "Enfurecido")]),
        new("botnet", "titan", "Titã DDoS", "Mecha de 6 patas com baterias de mísseis.", 5, true, 5, 1, 5, "Sentinel", "Functions, Raio", [new("titan_enf", "botnet:enraged", "Enfurecido")]),
        new("chefe_botnet", "botnet", "Rei Botnet", "Caranguejo-mecha com antenas de botnet.", 1, true, 5, 2, 3, "CDN", "Raio", [new("botnet_enf", "chefe_botnet:enraged", "Enfurecido"), new("aliado_bot", "aliado_botnet", "Bot zumbi")]),
        new("chefe_kraken", "kraken", "Kraken Phishing", "Lula-mecha flutuante com tentáculos de anzol.", 2, true, 5, 2, 2, "Policy", "Canhão", [new("kraken_enf", "chefe_kraken:enraged", "Enfurecido"), new("aliado_isca", "aliado_kraken", "Isca")]),
        new("chefe_golem", "golem", "Golem Rootkit", "Golem de pedra e metal enraizado no sistema.", 3, true, 5, 1, 5, "Sentinel", "Raio, Functions", [new("golem_enf", "chefe_golem:enraged", "Enfurecido"), new("aliado_raiz", "aliado_golem", "Raiz")]),
        new("chefe_ronin", "ronin", "Ronin Zero-Day", "Samurai-mecha que ataca sem aviso.", 4, true, 4, 4, 3, "Gelo", "Canhão", [new("ronin_enf", "chefe_ronin:enraged", "Enfurecido"), new("aliado_shuriken", "aliado_ronin", "Shuriken")]),
        new("chefe_trojan", "trojan", "Colosso Troiano", "Cavalo-mecha que solta soldados.", 5, true, 5, 1, 4, "WAF", "Gelo", [new("trojan_enf", "chefe_trojan:enraged", "Enfurecido"), new("aliado_soldado", "aliado_trojan", "Soldado")]),
        new("chefe_hydra", "hydra", "Hidra DDoS", "Serpente de três cabeças elétricas.", 6, true, 5, 3, 2, "CDN, Balanceador", "Fila", [new("hydra_enf", "chefe_hydra:enraged", "Enfurecido"), new("aliado_pacote", "aliado_hydra", "Pacote")]),
    ];
}
