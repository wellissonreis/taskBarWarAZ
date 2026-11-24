using System.Text;
using SideWar.Core.Content.Td;

namespace SideWar.Core.Tests;

/// <summary>Valores fixos para testar mecânicas Azure sem acoplamento à calibração econômica.</summary>
internal static class TdAzureMechanicsContent
{
    private static readonly Lazy<TdContent> Value = new(() =>
    {
        var files = TdContentLoader.ReadEmbedded();
        files["td-towers.json"] = Encoding.UTF8.GetBytes(Towers);
        var result = TdContentLoader.Load(files);
        return result.Content ?? throw new InvalidOperationException(string.Join("; ", result.Errors));
    });

    public static TdContent Load() => Value.Value;

    private const string Towers = """
[
  {
    "id": "bolt", "category": "attack", "name": "Raio",
    "flavor": "Como uma Azure VM: capacidade bruta sob demanda.", "role": "Alvo único, longo alcance, polivalente", "kind": "attack", "targets": "both", "defaultTargeting": "first",
    "cost": 90, "range": 150, "damage": 24, "interval": 0.9, "minRank": 0,
    "stats": {}, "strong": ["flying", "drain", "durable"], "weak": ["swarm", "replicator"], "weaknessText": "Dano por tiro baixo contra enxames e blindados."
  },
  {
    "id": "frost", "category": "control", "name": "Gelo",
    "flavor": "Como um WAF: filtra e retarda o que chega.", "role": "Controle: lentidão e marca", "kind": "attack", "targets": "both", "defaultTargeting": "first",
    "cost": 90, "range": 100, "damage": 9, "interval": 0.9, "minRank": 0,
    "stats": {"slowFactor": 0.7}, "strong": ["fast", "flying"], "weak": ["regen", "armored", "boss"], "weaknessText": "Dano quase nulo: não mata sozinha; chefes resistem ao controle."
  },
  {
    "id": "cannon", "category": "attack", "name": "Canhão",
    "flavor": "Como a proteção contra DDoS: absorve o grosso do ataque.", "role": "Área e dano pesado, só solo", "kind": "attack", "targets": "ground", "defaultTargeting": "strong",
    "cost": 120, "range": 110, "damage": 100, "interval": 2.2, "minRank": 0,
    "stats": {"splashRadius": 40, "splashFactor": 0.5}, "strong": ["swarm", "armored", "basic"], "weak": ["flying", "fast", "durable", "stealth"], "weaknessText": "Sem voadores nem rápidos; fraco contra duráveis."
  },
  {
    "id": "balancer", "category": "control", "name": "Balanceador",
    "flavor": "Como um Load Balancer: segura e distribui a carga.", "role": "Retém inimigos de solo com nós bloqueadores", "kind": "blocker", "targets": "ground", "defaultTargeting": null,
    "cost": 100, "range": 90, "damage": 0, "interval": 0, "minRank": 1,
    "stats": {"nodeDamagePerSecond": 4, "nodeCooldownSeconds": 6, "nodes": 1, "nodeHolds": 1}, "strong": ["fast", "swarm", "basic"], "weak": ["flying"], "weaknessText": "Não segura voadores nem chefes por muito tempo."
  },
  {
    "id": "scaleset", "category": "support", "name": "Scale Set",
    "flavor": "Como VM Scale Sets: mais instâncias quando a demanda sobe.", "role": "Suporte: bônus às torres vizinhas (não ataca)", "kind": "support", "targets": "none", "defaultTargeting": null,
    "cost": 150, "range": 120, "damage": 0, "interval": 0, "minRank": 2,
    "stats": {}, "strong": ["durable"], "weak": ["drain"], "weaknessText": "Não ataca e perde o efeito quando desativado por drenagem."
  },
  {"id": "functions", "name": "Functions", "flavor": "Como Azure Functions: só roda quando é chamada, e cobra barato por isso.", "role": "Rajada quando um inimigo entra no alcance", "kind": "attack", "category": "attack", "targets": "both", "defaultTargeting": "first", "cost": 60, "range": 90, "damage": 22, "interval": 3.0, "minRank": 0, "stats": {"burstShots": 3, "burstGapTicks": 4, "rearmSeconds": 3.0}, "strong": ["fast", "flying", "basic"], "weak": ["shielded", "durable", "boss", "armored", "swarm"], "weaknessText": "Uma rajada por entrada; sofre contra blindados e enxames."},
  {"id": "cdn", "name": "CDN", "flavor": "Como a CDN: já respondeu antes de o pedido chegar na origem.", "role": "Desgaste de longo alcance", "kind": "attack", "category": "attack", "targets": "both", "defaultTargeting": "first", "cost": 140, "range": 260, "damage": 8, "interval": 1.4, "minRank": 3, "stats": {"minRange": 120}, "strong": ["basic", "flying", "swarm"], "weak": ["armored", "durable", "boss"], "weaknessText": "Dano baixo e zona cega próxima."},
  {"id": "policy", "name": "Policy", "flavor": "Como o Azure Policy: impõe a regra e derruba a blindagem.", "role": "Remove armadura na área", "kind": "control", "category": "control", "targets": "none", "defaultTargeting": null, "cost": 120, "range": 110, "damage": 0, "interval": 0, "minRank": 1, "stats": {"armorRemoveBp": 10000}, "strong": ["armored"], "weak": ["flying", "swarm", "fast"], "weaknessText": "Não causa dano e pouco ajuda contra inimigos sem armadura."},
  {"id": "cosmos", "name": "Cosmos", "flavor": "Como Azure Cosmos: serviço distribuído.", "role": "Cosmos", "kind": "attack", "category": "attack", "targets": "both", "defaultTargeting": "first", "cost": 130, "range": 100, "damage": 20, "interval": 1.6, "minRank": 2, "stats": {"chainJumps": 3, "chainRadius": 60, "chainFalloffBp": 8000}, "strong": ["replicator", "swarm", "basic"], "weak": ["armored", "boss", "durable"], "weaknessText": "Menos eficaz contra armored, boss, durable."},
  {"id": "sentinel", "name": "Sentinel", "flavor": "Como Azure Sentinel: serviço distribuído.", "role": "Sentinel", "kind": "control", "category": "control", "targets": "both", "defaultTargeting": "strong", "cost": 110, "range": 130, "damage": 0, "interval": 5, "minRank": 4, "stats": {"markBonusBp": 2500, "markSeconds": 4, "markMax": 1}, "strong": ["stealth", "boss", "durable", "armored"], "weak": ["swarm", "fast"], "weaknessText": "Menos eficaz contra swarm, fast."},
  {"id": "queue", "name": "Fila", "flavor": "Como Azure Fila: serviço distribuído.", "role": "Fila", "kind": "control", "category": "control", "targets": "both", "defaultTargeting": "strong", "cost": 120, "range": 110, "damage": 0, "interval": 6, "minRank": 4, "stats": {"pushbackUnits": 60, "bossPushbackFactorBp": 5000, "pushGraceSeconds": 3}, "strong": ["durable", "boss"], "weak": ["swarm", "fast"], "weaknessText": "Menos eficaz contra swarm, fast."},
  {"id": "pipeline", "name": "Pipeline", "flavor": "Como Azure Pipeline: serviço distribuído.", "role": "Pipeline", "kind": "attack", "category": "attack", "targets": "both", "defaultTargeting": "strong", "cost": 150, "range": 130, "damage": 14, "interval": 0.8, "minRank": 3, "stats": {"rampPerHitBp": 800, "rampCapBp": 12000, "rampKeepsBetweenTargets": 0}, "strong": ["shielded", "durable", "boss", "armored"], "weak": ["swarm", "fast", "regen"], "weaknessText": "Menos eficaz contra swarm, fast, regen."},
  {"id": "vault", "name": "Cofre", "flavor": "Como Key Vault: guarda o que vale.", "role": "Créditos por onda", "kind": "economy", "category": "economy", "targets": "none", "defaultTargeting": null, "cost": 160, "range": 1, "damage": 0, "interval": 0, "minRank": 5, "stats": {"creditsPerWave": 14, "noLeakBonus": 10, "maxPerPlayer": 2}, "strong": ["durable"], "weak": ["drain"], "weaknessText": "Não defende e não rende quando desativado."},
  {"id": "backup", "name": "Backup", "flavor": "Como Site Recovery: volta ao ar sozinho.", "role": "Reativa torres e recupera XP perdido", "kind": "support", "category": "support", "targets": "none", "defaultTargeting": null, "cost": 170, "range": 140, "damage": 0, "interval": 0, "minRank": 4, "stats": {"reviveEverySeconds": 6, "coreHealEverySeconds": 45, "coreHealCapPerStage": 3}, "strong": ["drain"], "weak": ["swarm"], "weaknessText": "Não ataca e depende de torres próximas."},
  {"id": "aks", "name": "AKS", "flavor": "Como AKS: sobe pods quando a carga aperta.", "role": "Invoca pods atacantes", "kind": "summoner", "category": "support", "targets": "none", "defaultTargeting": null, "cost": 180, "range": 80, "damage": 0, "interval": 0, "minRank": 5, "stats": {"pods": 3, "podDps": 8, "podLifeSeconds": 12, "triggerEnemies": 3, "interval": 20}, "strong": ["swarm", "basic"], "weak": ["flying", "boss", "fast"], "weaknessText": "Não retém inimigos e não atinge voadores."},
  {"id": "dns", "name": "DNS", "flavor": "Serviço Azure: DNS.", "role": "Ataque leve a solo e ar", "kind": "attack", "category": "attack", "targets": "both", "defaultTargeting": "first", "cost": 75, "range": 125, "damage": 14, "interval": 0.7, "minRank": 0, "stats": {}, "strong": ["basic", "fast"], "weak": ["armored", "boss"], "weaknessText": "Fraco contra armored, boss."},
  {"id": "frontdoor", "name": "Front Door", "flavor": "Serviço Azure: Front Door.", "role": "Retém inimigos de solo", "kind": "blocker", "category": "control", "targets": "ground", "defaultTargeting": null, "cost": 110, "range": 90, "damage": 0, "interval": 0, "minRank": 1, "stats": {"nodeDamagePerSecond": 3, "nodeCooldownSeconds": 6, "nodes": 1, "nodeHolds": 1}, "strong": ["fast", "basic"], "weak": ["flying"], "weaknessText": "Fraco contra flying."},
  {"id": "shield", "name": "Escudo", "flavor": "Serviço Azure: Escudo.", "role": "Bônus de dano às outras torres", "kind": "support", "category": "support", "targets": "none", "defaultTargeting": null, "cost": 135, "range": 110, "damage": 0, "interval": 0, "minRank": 2, "stats": {"neighborDamageBonus": 0.08}, "strong": ["durable"], "weak": ["drain"], "weaknessText": "Fraco contra drain."},
  {"id": "waf", "name": "WAF", "flavor": "Serviço Azure: WAF.", "role": "Dano e lentidão", "kind": "attack", "category": "control", "targets": "both", "defaultTargeting": "first", "cost": 125, "range": 100, "damage": 7, "interval": 1.2, "minRank": 2, "stats": {"slowFactor": 0.8}, "strong": ["fast"], "weak": ["armored", "boss"], "weaknessText": "Fraco contra armored, boss."},
  {"id": "eventgrid", "name": "Event Grid", "flavor": "Serviço Azure: Event Grid.", "role": "Ataque encadeado", "kind": "attack", "category": "attack", "targets": "both", "defaultTargeting": "first", "cost": 145, "range": 105, "damage": 16, "interval": 1.7, "minRank": 3, "stats": {"chainJumps": 2, "chainRadius": 60, "chainFalloffBp": 8000}, "strong": ["swarm", "replicator"], "weak": ["armored", "durable"], "weaknessText": "Fraco contra armored, durable."},
  {"id": "bastion", "name": "Bastion", "flavor": "Serviço Azure: Bastion.", "role": "Reativa uma torre drenada", "kind": "support", "category": "support", "targets": "none", "defaultTargeting": null, "cost": 175, "range": 120, "damage": 0, "interval": 0, "minRank": 4, "stats": {"reviveEverySeconds": 8}, "strong": ["drain"], "weak": ["swarm"], "weaknessText": "Fraco contra swarm."},
  {"id": "firewall", "name": "Firewall", "flavor": "Serviço Azure: Firewall.", "role": "Dano de área no solo", "kind": "attack", "category": "attack", "targets": "ground", "defaultTargeting": "first", "cost": 165, "range": 110, "damage": 80, "interval": 2.4, "minRank": 5, "stats": {"splashRadius": 35, "splashFactor": 0.5}, "strong": ["swarm", "armored"], "weak": ["flying", "fast"], "weaknessText": "Fraco contra flying, fast."}
]
""";
}
