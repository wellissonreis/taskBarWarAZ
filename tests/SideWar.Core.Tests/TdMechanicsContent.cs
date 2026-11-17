using System.Text;
using SideWar.Core.Content.Td;

namespace SideWar.Core.Tests;

/// <summary>
/// Conteúdo TD congelado (valores do TD.3b, antes do balanceamento da TD.10) para os testes de MECÂNICA do Core TD (Match/Combat/Placement).
/// Assim o tuning de <c>td-*.json</c> (HP, custos, ondas) não quebra testes que verificam regras; a curva de dificuldade é coberta por
/// <c>TdSanityBotTests</c> sobre o conteúdo embarcado de verdade.
/// </summary>
internal static class TdMechanicsContent
{
    private static readonly Lazy<TdContent> Value = new(Build);

    public static TdContent Load() => Value.Value;

    private static TdContent Build()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["td-abilities.json"] = Encoding.UTF8.GetBytes(Abilities + "\n"),
            ["td-enemies.json"] = Encoding.UTF8.GetBytes(Enemies + "\n"),
            ["td-meta.json"] = Encoding.UTF8.GetBytes(Meta + "\n"),
            ["td-runes.json"] = TdContentLoader.ReadEmbedded()["td-runes.json"], // regras e efeitos das runas atuais (TD.21)
            ["td-stages.json"] = Encoding.UTF8.GetBytes(Stages + "\n"),
            ["td-towers.json"] = Encoding.UTF8.GetBytes(Towers + "\n"),
            ["td-waves.json"] = Encoding.UTF8.GetBytes(Waves + "\n"),
        };
        var result = TdContentLoader.Load(files);
        var content = result.Content ?? throw new InvalidOperationException("Conteúdo congelado inválido: " + string.Join("; ", result.Errors));
        // Fontes de crédito congeladas (valores da TD.22): a recalibração da TD.29a de td-runes.json não mexe nos testes de mecânica.
        return content with { Runes = content.Runes with { CreditSources = new TdCreditSourcesDef(80, 60, 5, 40, 2, 40, 3, 120) } };
    }

    private const string Abilities = """
[
  { "id": "scale",    "name": "Escalar",            "effect": "+50% de cadência em todas as torres por 8 s.", "cooldownSeconds": 60, "durationSeconds": 8, "unlockStage": 1, "unlockMetaNode": null, "usesPerStage": null, "stats": { "cadenceBonus": 0.5 } },
  { "id": "firewall", "name": "Firewall global",    "effect": "Lentidão de 50% (20% em chefes) em todos os inimigos por 6 s.", "cooldownSeconds": 75, "durationSeconds": 6, "unlockStage": 3, "unlockMetaNode": null, "usesPerStage": null, "stats": { "slow": 0.5, "bossSlow": 0.2 } },
  { "id": "snapshot", "name": "Restaurar snapshot", "effect": "Recupera 3 de integridade; 1 uso por fase.", "cooldownSeconds": 0, "durationSeconds": 0, "unlockStage": null, "unlockMetaNode": "operation.snapshot", "usesPerStage": 1, "stats": { "integrity": 3 } }
]
""";

    private const string Enemies = """
{
  "enemies": [
    { "id": "grunt",   "name": "Requisição",      "flavor": "O tráfego básico de qualquer serviço.", "hp": 40,  "speed": 40, "armor": 0, "gold": 6,  "coreDamage": 1, "introStage": 1, "traits": ["basic"],   "counters": ["bolt", "cannon"], "params": {} },
    { "id": "runner",  "name": "Ping",            "flavor": "Rápido demais para defesas lentas.", "hp": 22,  "speed": 78, "armor": 0, "gold": 6,  "coreDamage": 1, "introStage": 1, "traits": ["fast"],    "counters": ["balancer", "frost", "bolt"], "params": {} },
    { "id": "swarm",   "name": "Rajada",          "flavor": "Dezenas de pacotes minúsculos de uma vez.", "hp": 14,  "speed": 46, "armor": 0, "gold": 3,  "coreDamage": 1, "introStage": 3, "traits": ["swarm"],   "counters": ["cannon", "balancer", "bolt"], "params": {} },
    { "id": "armored", "name": "Pacote blindado", "flavor": "Dano fraco por tiro some na armadura.", "hp": 110, "speed": 32, "armor": 6, "gold": 12, "coreDamage": 2, "introStage": 2, "traits": ["armored"], "counters": ["cannon", "bolt", "frost"], "params": {} },
    { "id": "tank",    "name": "Tráfego pesado",  "flavor": "Testa o DPS total da defesa.", "hp": 380, "speed": 24, "armor": 2, "gold": 20, "coreDamage": 3, "introStage": 3, "traits": ["durable"], "counters": ["bolt", "cannon", "scaleset"], "params": {} },
    { "id": "flyer",   "name": "Bot aéreo",       "flavor": "Ignora balanceador e canhão.", "hp": 60,  "speed": 55, "armor": 0, "gold": 10, "coreDamage": 1, "introStage": 4, "traits": ["flying"],  "counters": ["bolt", "frost"], "params": {} },
    { "id": "regen",   "name": "Verme",           "flavor": "Regenera quando fica sem receber dano.", "hp": 120, "speed": 36, "armor": 0, "gold": 14, "coreDamage": 2, "introStage": 5, "traits": ["regen"],   "counters": ["bolt", "frost", "cannon"], "params": { "regenPctPerSecond": 0.04, "regenDelaySeconds": 2 } },
    { "id": "leech",   "name": "Parasita",        "flavor": "Drena torres: desativa a que estiver ao alcance.", "hp": 90,  "speed": 38, "armor": 0, "gold": 14, "coreDamage": 2, "introStage": 5, "traits": ["drain"],   "counters": ["bolt"], "params": { "drainRangeUnits": 80, "disableSeconds": 3 } }
  ],
  "bosses": [
    {
      "id": "ransomware", "name": "Ransomware", "flavor": "Sequestra o que está por perto até o resgate.", "stage": 3,
      "hp": 1500, "speed": 20, "armor": 3, "gold": 150, "coreDamage": 5, "paralysisImmune": true, "slowCap": 0.2,
      "mechanic": "A cada 10 s criptografa a torre mais próxima por 4 s; recebe 30% menos dano de torres bloqueadas.",
      "traits": ["boss", "durable", "drain"], "counters": ["balancer", "frost", "bolt"],
      "params": { "encryptIntervalSeconds": 10, "encryptSeconds": 4, "encryptRangeUnits": 120, "blockedTowerDamageReduction": 0.3 }
    },
    {
      "id": "botnet", "name": "Botnet (Hidra DDoS)", "flavor": "Corte uma cabeça e nascem seis.", "stage": 5,
      "hp": 2200, "speed": 20, "armor": 2, "gold": 200, "coreDamage": 6, "paralysisImmune": true, "slowCap": 0.2,
      "mechanic": "Ao cair para 66% e 33% do HP divide-se em 6 enxames.",
      "traits": ["boss", "swarm"], "counters": ["cannon", "balancer"],
      "params": { "splitAtHpFraction1": 0.66, "splitAtHpFraction2": 0.33, "splitCount": 6 }
    },
    {
      "id": "zeroday", "name": "Zero-day", "flavor": "Ninguém tem patch para isto.", "stage": 6,
      "hp": 3000, "speed": 20, "armor": 4, "gold": 300, "coreDamage": 8, "paralysisImmune": true, "slowCap": 0.0,
      "mechanic": "Imune a lentidão e paralisia; regenera 2%/s; fica invulnerável por 3 s ao entrar em cada nova metade do caminho.",
      "traits": ["boss", "regen"], "counters": ["cannon", "scaleset", "bolt"],
      "params": { "regenPctPerSecond": 0.02, "regenDelaySeconds": 0, "invulnerableSeconds": 3, "invulnerableAtPathFraction1": 0.5, "invulnerableAtPathFraction2": 0.75 }
    }
  ]
}
""";

    private const string Meta = """
{
  "sealsPerStar": 1,
  "sealsPerChallenge": 1,
  "branches": [
    { "id": "infrastructure", "name": "Infraestrutura", "nodes": [
      { "id": "infrastructure.credits",  "name": "Orçamento inicial", "cost": 1, "effects": [ { "kind": "startingCredits", "value": 10, "target": null } ] },
      { "id": "infrastructure.sell75",   "name": "Revenda 75%",       "cost": 2, "effects": [ { "kind": "sellRefund", "value": 0.75, "target": null } ] },
      { "id": "infrastructure.slots1",   "name": "Mais uma posição",  "cost": 3, "effects": [ { "kind": "maxTowers", "value": 1, "target": null } ] },
      { "id": "infrastructure.slots2",   "name": "Revenda 80% e posição extra", "cost": 4, "effects": [ { "kind": "maxTowers", "value": 1, "target": null }, { "kind": "sellRefund", "value": 0.8, "target": null } ] }
    ] },
    { "id": "defense", "name": "Defesa", "nodes": [
      { "id": "defense.damage1",    "name": "Dano +3%",        "cost": 1, "effects": [ { "kind": "damagePercent", "value": 3, "target": null } ] },
      { "id": "defense.range",      "name": "Alcance +5%",     "cost": 2, "effects": [ { "kind": "rangePercent", "value": 5, "target": null } ] },
      { "id": "defense.integrity1", "name": "Integridade +1",  "cost": 3, "effects": [ { "kind": "integrity", "value": 1, "target": null } ] },
      { "id": "defense.integrity2", "name": "Integridade +1 e dano +3%", "cost": 4, "effects": [ { "kind": "integrity", "value": 1, "target": null }, { "kind": "damagePercent", "value": 3, "target": null } ] }
    ] },
    { "id": "operation", "name": "Operação", "nodes": [
      { "id": "operation.cooldown1", "name": "Recarga -10%",         "cost": 1, "effects": [ { "kind": "abilityCooldownPercent", "value": 10, "target": null } ] },
      { "id": "operation.snapshot",  "name": "Restaurar snapshot",   "cost": 2, "effects": [ { "kind": "unlockAbility", "value": 1, "target": "snapshot" } ] },
      { "id": "operation.cooldown2", "name": "Recarga -10%",         "cost": 3, "effects": [ { "kind": "abilityCooldownPercent", "value": 10, "target": null } ] },
      { "id": "operation.autostart", "name": "Auto-início +25%",     "cost": 4, "effects": [ { "kind": "autoStartBonusPercent", "value": 25, "target": null } ] }
    ] }
  ]
}
""";

    private const string Stages = """
{
  "rules": {
    "coreIntegrity": 20,
    "sellRefund": 0.7,
    "towerCostTable": [100, 125, 156, 195, 244, 305, 381, 477, 596, 745, 931, 1164, 1455, 1819, 2274, 2842, 3553, 4441, 5551, 6939, 8674, 10842, 13553, 16941, 21176, 26470, 33087, 41359, 51699, 64623, 80779, 100974, 126218, 157772, 197215, 246519, 308149, 385186, 481482, 601853, 752316, 940395, 1175494, 1469368, 1836710, 2295887, 2869859, 3587324],
    "towerCostTableLow": [100, 120, 144, 173, 207, 249, 299, 358, 430, 516, 619, 743, 892, 1070, 1284, 1541, 1849, 2219, 2662, 3195, 3834, 4601, 5521, 6625, 7950, 9540, 11448, 13737, 16484, 19781, 23738, 28485, 34182, 41019, 49222, 59067, 70880, 85056, 102067, 122481, 146977, 176373, 211647, 253977, 304772, 365726, 438871, 526646],
    "waveBonusBase": 20,
    "waveBonusPerWave": 4,
    "hpWaveGrowth": 1.08,
    "hpStageStep": 0.1,
    "spawnJitter": 0.1,
    "maxEnemiesOnField": 44,
    "callNext": { "bonusBase": 0.6, "bonusPerWave": 0.05, "bonusCapFactor": 2.0, "abilityCooldownReduction": 0.25 },
    "autoStart": { "defaultMode": "off", "defaultCountdownSeconds": 15, "minCountdownSeconds": 5, "maxCountdownSeconds": 60 },
    "stars": { "twoStarMaxLoss": 6, "threeStarMaxLoss": 2 },
    "bonusQuestion": { "timerSeconds": 10, "creditsBase": 15, "creditsPerWave": 3, "cooldownReductionSeconds": 10 },
    "fragments": { "perWave": 1, "perQuestion": 1, "perBoss": 2 },
    "challenges": [
      { "id": "frugal",   "name": "Frugal",   "description": "Concluir com no máximo 4 torres.", "maxTowers": 4, "noAbilities": false, "noCallNext": false, "startingCreditsFactor": 1.0 },
      { "id": "purist",   "name": "Purista",  "description": "Concluir sem usar habilidades.", "maxTowers": null, "noAbilities": true, "noCallNext": false, "startingCreditsFactor": 1.0 },
      { "id": "marathon", "name": "Maratona", "description": "Concluir sem chamar ondas antes e com 30% menos créditos iniciais.", "maxTowers": null, "noAbilities": false, "noCallNext": true, "startingCreditsFactor": 0.7 }
    ]
  },
  "stages": [
    { "id": 1, "title": "Brazil South",    "region": "brazilsouth",   "concept": "Tutorial: construir e iniciar a onda", "waves": 10, "startingCredits": 150, "challenge": "frugal" },
    { "id": 2, "title": "East US",         "region": "eastus",        "concept": "Blindados e mira", "waves": 12, "startingCredits": 180, "challenge": "purist" },
    { "id": 3, "title": "West Europe",     "region": "westeurope",    "concept": "Controle, habilidade e o primeiro chefe", "waves": 12, "startingCredits": 200, "challenge": "marathon" },
    { "id": 4, "title": "Japan East",      "region": "japaneast",     "concept": "Voadores e suporte (respiro)", "waves": 14, "startingCredits": 240, "challenge": "frugal" },
    { "id": 5, "title": "UK South",        "region": "uksouth",       "concept": "Regeneração e drenagem", "waves": 14, "startingCredits": 260, "challenge": "purist" },
    { "id": 6, "title": "Australia East",  "region": "australiaeast", "concept": "Prova final", "waves": 15, "startingCredits": 300, "challenge": "marathon" }
  ]
}
""";

    private const string Towers = """
[
  {"id": "bolt", "name": "Raio", "flavor": "Como uma Azure VM: capacidade bruta sob demanda.", "role": "Alvo único, longo alcance, polivalente", "kind": "attack", "targets": "both", "defaultTargeting": "first", "cost": 70, "range": 150, "damage": 14, "interval": 0.6, "unlockStage": 1, "stats": {}, "strong": ["flying", "drain", "durable"], "weak": ["swarm"], "weaknessText": "Dano por tiro baixo contra enxames e blindados."},
  {"id": "frost", "name": "Gelo", "flavor": "Como um WAF: filtra e retarda o que chega.", "role": "Controle: lentidão e marca", "kind": "attack", "targets": "both", "defaultTargeting": "first", "cost": 90, "range": 100, "damage": 5, "interval": 0.9, "unlockStage": 1, "stats": {"slowFactor": 0.7}, "strong": ["fast", "flying"], "weak": ["regen", "armored", "boss"], "weaknessText": "Dano quase nulo: não mata sozinha; chefes resistem ao controle."},
  {"id": "cannon", "name": "Canhão", "flavor": "Como a proteção contra DDoS: absorve o grosso do ataque.", "role": "Área e dano pesado, só solo", "kind": "attack", "targets": "ground", "defaultTargeting": "strong", "cost": 120, "range": 110, "damage": 45, "interval": 1.9, "unlockStage": 1, "stats": {"splashRadius": 40, "splashFactor": 0.5}, "strong": ["swarm", "armored", "basic"], "weak": ["flying", "fast", "durable"], "weaknessText": "Sem voadores nem rápidos; fraco contra duráveis."},
  {"id": "balancer", "name": "Balanceador", "flavor": "Como um Load Balancer: segura e distribui a carga.", "role": "Retém inimigos de solo com nós bloqueadores", "kind": "blocker", "targets": "ground", "defaultTargeting": null, "cost": 100, "range": 90, "damage": 0, "interval": 0, "unlockStage": 2, "stats": {"nodeDamagePerSecond": 4, "nodeCooldownSeconds": 6, "nodes": 1, "nodeHolds": 1}, "strong": ["fast", "swarm", "basic"], "weak": ["flying"], "weaknessText": "Não segura voadores nem chefes por muito tempo."},
  {"id": "scaleset", "name": "Scale Set", "flavor": "Como VM Scale Sets: mais instâncias quando a demanda sobe.", "role": "Suporte: bônus às torres vizinhas (não ataca)", "kind": "support", "targets": "none", "defaultTargeting": null, "cost": 150, "range": 120, "damage": 0, "interval": 0, "unlockStage": 4, "stats": {}, "strong": ["durable"], "weak": ["drain"], "weaknessText": "Não ataca e perde o efeito quando desativado por drenagem."}
]
""";

    private const string Waves = """
{
  "stages": [
    {
      "stage": 1,
      "waves": [
        {"number": 1, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}]},
        {"number": 2, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}]},
        {"number": 3, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 3, "offset": 8.1, "interval": 0.8}]},
        {"number": 4, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 4, "offset": 10.3, "interval": 0.8}]},
        {"number": 5, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 5, "offset": 0.0, "interval": 1.1}]},
        {"number": 6, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 5, "offset": 10.3, "interval": 0.8}]},
        {"number": 7, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 10, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 4, "offset": 12.5, "interval": 0.8}]},
        {"number": 8, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 8, "offset": 0.0, "interval": 0.8}, {"enemy": "grunt", "count": 6, "offset": 7.9, "interval": 1.1}]},
        {"number": 9, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 12, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 6, "offset": 14.7, "interval": 0.8}]},
        {"number": 10, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 14, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 8, "offset": 16.9, "interval": 0.8}]}
      ]
    },
    {
      "stage": 2,
      "waves": [
        {"number": 1, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}]},
        {"number": 2, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "armored", "count": 2, "offset": 8.1, "interval": 1.6}]},
        {"number": 3, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 4, "offset": 10.3, "interval": 0.8}]},
        {"number": 4, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "armored", "count": 4, "offset": 0.0, "interval": 1.6}, {"enemy": "grunt", "count": 6, "offset": 7.9, "interval": 1.1}]},
        {"number": 5, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 3, "offset": 8.1, "interval": 0.8}]},
        {"number": 6, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 6, "offset": 8.1, "interval": 0.8}, {"enemy": "armored", "count": 3, "offset": 14.4, "interval": 1.6}]},
        {"number": 7, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "armored", "count": 5, "offset": 10.3, "interval": 1.6}]},
        {"number": 8, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 8, "offset": 0.0, "interval": 0.8}, {"enemy": "armored", "count": 4, "offset": 7.9, "interval": 1.6}]},
        {"number": 9, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 10, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 4, "offset": 12.5, "interval": 0.8}, {"enemy": "armored", "count": 6, "offset": 17.2, "interval": 1.6}]},
        {"number": 10, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "armored", "count": 2, "offset": 10.3, "interval": 1.6}]},
        {"number": 11, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 8, "offset": 8.1, "interval": 0.8}, {"enemy": "armored", "count": 8, "offset": 16.0, "interval": 1.6}]},
        {"number": 12, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 8, "offset": 10.3, "interval": 0.8}, {"enemy": "armored", "count": 10, "offset": 18.2, "interval": 1.6}]}
      ]
    },
    {
      "stage": 3,
      "waves": [
        {"number": 1, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 3, "offset": 10.3, "interval": 0.8}]},
        {"number": 2, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 12, "offset": 0.0, "interval": 0.35}]},
        {"number": 3, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "swarm", "count": 10, "offset": 10.3, "interval": 0.35}]},
        {"number": 4, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 5, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 3, "offset": 7.0, "interval": 0.8}]},
        {"number": 5, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "tank", "count": 2, "offset": 10.3, "interval": 2.5}]},
        {"number": 6, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 4, "offset": 0.0, "interval": 0.8}, {"enemy": "swarm", "count": 16, "offset": 4.7, "interval": 0.35}]},
        {"number": 7, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 8, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 5, "offset": 4.3, "interval": 1.6}, {"enemy": "tank", "count": 2, "offset": 13.8, "interval": 2.5}]},
        {"number": 8, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "swarm", "count": 6, "offset": 8.1, "interval": 0.35}]},
        {"number": 9, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 14, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 4, "offset": 6.4, "interval": 1.6}, {"enemy": "tank", "count": 3, "offset": 14.3, "interval": 2.5}]},
        {"number": 10, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 4, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 6, "offset": 5.9, "interval": 0.8}, {"enemy": "swarm", "count": 20, "offset": 12.2, "interval": 0.35}]},
        {"number": 11, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 10, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 6, "offset": 5.0, "interval": 1.6}, {"enemy": "tank", "count": 4, "offset": 16.1, "interval": 2.5}]},
        {"number": 12, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 10, "offset": 0.0, "interval": 1.1}, {"enemy": "tank", "count": 2, "offset": 12.5, "interval": 2.5}, {"enemy": "ransomware", "count": 1, "offset": 19.0, "interval": 1.0}, {"enemy": "swarm", "count": 10, "offset": 21.5, "interval": 0.35}]}
      ]
    },
    {
      "stage": 4,
      "waves": [
        {"number": 1, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 4, "offset": 0.0, "interval": 1.1}, {"enemy": "flyer", "count": 4, "offset": 5.9, "interval": 1.2}]},
        {"number": 2, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 4, "offset": 0.0, "interval": 1.1}, {"enemy": "flyer", "count": 6, "offset": 5.9, "interval": 1.2}]},
        {"number": 3, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 3, "offset": 10.3, "interval": 0.8}, {"enemy": "flyer", "count": 4, "offset": 14.2, "interval": 1.2}]},
        {"number": 4, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "flyer", "count": 4, "offset": 0.0, "interval": 1.2}]},
        {"number": 5, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "armored", "count": 3, "offset": 0.0, "interval": 1.6}, {"enemy": "flyer", "count": 8, "offset": 6.3, "interval": 1.2}]},
        {"number": 6, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 6, "offset": 0.0, "interval": 0.8}, {"enemy": "flyer", "count": 6, "offset": 6.3, "interval": 1.2}]},
        {"number": 7, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "tank", "count": 2, "offset": 8.1, "interval": 2.5}, {"enemy": "flyer", "count": 8, "offset": 14.6, "interval": 1.2}]},
        {"number": 8, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 4, "offset": 0.0, "interval": 1.1}, {"enemy": "flyer", "count": 5, "offset": 5.9, "interval": 1.2}]},
        {"number": 9, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 8, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 4, "offset": 4.3, "interval": 1.6}, {"enemy": "flyer", "count": 10, "offset": 12.2, "interval": 1.2}]},
        {"number": 10, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 8, "offset": 0.0, "interval": 0.8}, {"enemy": "tank", "count": 2, "offset": 7.9, "interval": 2.5}, {"enemy": "flyer", "count": 8, "offset": 14.4, "interval": 1.2}]},
        {"number": 11, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 14, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 4, "offset": 6.4, "interval": 1.6}, {"enemy": "flyer", "count": 8, "offset": 14.3, "interval": 1.2}]},
        {"number": 12, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "flyer", "count": 6, "offset": 8.1, "interval": 1.2}]},
        {"number": 13, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 6, "offset": 0.0, "interval": 0.8}, {"enemy": "tank", "count": 3, "offset": 6.3, "interval": 2.5}, {"enemy": "flyer", "count": 12, "offset": 15.3, "interval": 1.2}]},
        {"number": 14, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 4, "offset": 0.0, "interval": 1.1}, {"enemy": "armored", "count": 6, "offset": 5.9, "interval": 1.6}, {"enemy": "tank", "count": 3, "offset": 17.0, "interval": 2.5}, {"enemy": "flyer", "count": 14, "offset": 26.0, "interval": 1.2}]}
      ]
    },
    {
      "stage": 5,
      "waves": [
        {"number": 1, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 4, "offset": 10.3, "interval": 0.8}]},
        {"number": 2, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "regen", "count": 3, "offset": 8.1, "interval": 1.8}]},
        {"number": 3, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 4, "offset": 0.0, "interval": 0.8}, {"enemy": "swarm", "count": 8, "offset": 4.7, "interval": 0.35}, {"enemy": "regen", "count": 4, "offset": 9.0, "interval": 1.8}]},
        {"number": 4, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "regen", "count": 2, "offset": 8.1, "interval": 1.8}]},
        {"number": 5, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "leech", "count": 2, "offset": 10.3, "interval": 1.8}]},
        {"number": 6, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "flyer", "count": 4, "offset": 0.0, "interval": 1.2}, {"enemy": "regen", "count": 5, "offset": 6.3, "interval": 1.8}, {"enemy": "leech", "count": 2, "offset": 16.8, "interval": 1.8}]},
        {"number": 7, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 6, "offset": 0.0, "interval": 0.8}, {"enemy": "armored", "count": 6, "offset": 6.3, "interval": 1.6}, {"enemy": "regen", "count": 4, "offset": 17.4, "interval": 1.8}]},
        {"number": 8, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 10, "offset": 0.0, "interval": 0.35}, {"enemy": "tank", "count": 3, "offset": 5.0, "interval": 2.5}, {"enemy": "leech", "count": 4, "offset": 14.0, "interval": 1.8}]},
        {"number": 9, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 6, "offset": 0.0, "interval": 1.1}, {"enemy": "regen", "count": 3, "offset": 8.1, "interval": 1.8}]},
        {"number": 10, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "armored", "count": 5, "offset": 0.0, "interval": 1.6}, {"enemy": "flyer", "count": 6, "offset": 9.5, "interval": 1.2}, {"enemy": "regen", "count": 6, "offset": 18.2, "interval": 1.8}, {"enemy": "leech", "count": 3, "offset": 30.5, "interval": 1.8}]},
        {"number": 11, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 18, "offset": 0.0, "interval": 0.35}, {"enemy": "regen", "count": 5, "offset": 7.8, "interval": 1.8}, {"enemy": "leech", "count": 3, "offset": 18.3, "interval": 1.8}]},
        {"number": 12, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 6, "offset": 0.0, "interval": 0.8}, {"enemy": "tank", "count": 4, "offset": 6.3, "interval": 2.5}, {"enemy": "flyer", "count": 8, "offset": 17.8, "interval": 1.2}, {"enemy": "regen", "count": 6, "offset": 28.9, "interval": 1.8}]},
        {"number": 13, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 10, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 8, "offset": 5.0, "interval": 1.6}, {"enemy": "regen", "count": 6, "offset": 19.3, "interval": 1.8}, {"enemy": "leech", "count": 5, "offset": 31.6, "interval": 1.8}]},
        {"number": 14, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 8, "offset": 0.0, "interval": 0.35}, {"enemy": "flyer", "count": 6, "offset": 4.3, "interval": 1.2}, {"enemy": "regen", "count": 6, "offset": 13.0, "interval": 1.8}, {"enemy": "leech", "count": 4, "offset": 25.3, "interval": 1.8}, {"enemy": "botnet", "count": 1, "offset": 34.0, "interval": 1.0}]}
      ]
    },
    {
      "stage": 6,
      "waves": [
        {"number": 1, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 6, "offset": 10.3, "interval": 0.8}, {"enemy": "flyer", "count": 4, "offset": 16.6, "interval": 1.2}]},
        {"number": 2, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 12, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 6, "offset": 5.7, "interval": 1.6}, {"enemy": "regen", "count": 3, "offset": 16.8, "interval": 1.8}]},
        {"number": 3, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "tank", "count": 3, "offset": 0.0, "interval": 2.5}, {"enemy": "leech", "count": 3, "offset": 9.0, "interval": 1.8}, {"enemy": "flyer", "count": 8, "offset": 15.9, "interval": 1.2}]},
        {"number": 4, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 10, "offset": 0.0, "interval": 0.8}, {"enemy": "armored", "count": 6, "offset": 9.5, "interval": 1.6}, {"enemy": "regen", "count": 5, "offset": 20.6, "interval": 1.8}]},
        {"number": 5, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "flyer", "count": 4, "offset": 10.3, "interval": 1.2}]},
        {"number": 6, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 16, "offset": 0.0, "interval": 0.35}, {"enemy": "tank", "count": 3, "offset": 7.1, "interval": 2.5}, {"enemy": "leech", "count": 3, "offset": 16.1, "interval": 1.8}]},
        {"number": 7, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "armored", "count": 6, "offset": 0.0, "interval": 1.6}, {"enemy": "regen", "count": 5, "offset": 11.1, "interval": 1.8}, {"enemy": "flyer", "count": 10, "offset": 21.6, "interval": 1.2}]},
        {"number": 8, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 8, "offset": 0.0, "interval": 0.8}, {"enemy": "tank", "count": 5, "offset": 7.9, "interval": 2.5}, {"enemy": "regen", "count": 6, "offset": 21.9, "interval": 1.8}, {"enemy": "leech", "count": 5, "offset": 34.2, "interval": 1.8}]},
        {"number": 9, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 16, "offset": 0.0, "interval": 0.35}, {"enemy": "armored", "count": 6, "offset": 7.1, "interval": 1.6}, {"enemy": "flyer", "count": 6, "offset": 18.2, "interval": 1.2}]},
        {"number": 10, "rest": true, "bonusFactor": 1.5, "groups": [{"enemy": "grunt", "count": 8, "offset": 0.0, "interval": 1.1}, {"enemy": "runner", "count": 6, "offset": 10.3, "interval": 0.8}, {"enemy": "regen", "count": 3, "offset": 16.6, "interval": 1.8}]},
        {"number": 11, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "armored", "count": 6, "offset": 0.0, "interval": 1.6}, {"enemy": "tank", "count": 4, "offset": 11.1, "interval": 2.5}, {"enemy": "leech", "count": 4, "offset": 22.6, "interval": 1.8}, {"enemy": "flyer", "count": 8, "offset": 31.3, "interval": 1.2}]},
        {"number": 12, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "runner", "count": 6, "offset": 0.0, "interval": 0.8}, {"enemy": "swarm", "count": 14, "offset": 6.3, "interval": 0.35}, {"enemy": "regen", "count": 8, "offset": 12.7, "interval": 1.8}]},
        {"number": 13, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "armored", "count": 8, "offset": 0.0, "interval": 1.6}, {"enemy": "tank", "count": 4, "offset": 14.3, "interval": 2.5}, {"enemy": "leech", "count": 5, "offset": 25.8, "interval": 1.8}, {"enemy": "flyer", "count": 8, "offset": 36.3, "interval": 1.2}]},
        {"number": 14, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "swarm", "count": 14, "offset": 0.0, "interval": 0.35}, {"enemy": "tank", "count": 4, "offset": 6.4, "interval": 2.5}, {"enemy": "regen", "count": 6, "offset": 17.9, "interval": 1.8}, {"enemy": "leech", "count": 4, "offset": 30.2, "interval": 1.8}]},
        {"number": 15, "rest": false, "bonusFactor": 1.0, "groups": [{"enemy": "armored", "count": 6, "offset": 0.0, "interval": 1.6}, {"enemy": "tank", "count": 3, "offset": 11.1, "interval": 2.5}, {"enemy": "regen", "count": 6, "offset": 20.1, "interval": 1.8}, {"enemy": "flyer", "count": 6, "offset": 32.4, "interval": 1.2}, {"enemy": "zeroday", "count": 1, "offset": 41.1, "interval": 1.0}]}
      ]
    }
  ]
}
""";

}
