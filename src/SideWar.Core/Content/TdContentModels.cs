namespace SideWar.Core.Content.Td;

// Conteúdo TD v2 (SDD-28 RF-28.4 a RF-28.13), lado a lado com o conteúdo idle (SDD-12/27): namespace e arquivos
// `td-*.json` novos, hash próprio. Distâncias em unidades lógicas (mundo de 1.200 unidades), tempos em segundos.

// LEGADO (SDD-29): TdTowerLevelDef, TdCapstoneDef, TdBranchDef, TdTowerDef.Levels/Branches e TdStageDef.MaxTowers
// serão removidos na TD.20/TD.21; seguem aqui só porque Runtime/Rendering ainda os consomem.

/// <summary>Nível linear 1 a 3 (RF-28.5): <c>CostFactor</c> é múltiplo do custo base da torre.</summary>
public sealed record TdTowerLevelDef(int Level, double CostFactor, double DamageMultiplier);

/// <summary>Capstone (nível 5) do ramo escolhido; herda a fraqueza do ramo.</summary>
public sealed record TdCapstoneDef(string Name, double CostFactor, string Effect, Dictionary<string, double> Stats);

/// <summary>Ramo A/B do nível 4. <c>Weakness</c> (texto) e <c>WeakAgainst</c> (traços) são obrigatórios.</summary>
public sealed record TdBranchDef(
    string Id, string Name, double CostFactor, string Gain, string Weakness, string[] WeakAgainst,
    Dictionary<string, double> Stats, TdCapstoneDef Capstone);

/// <summary>Torre (td-towers.json). <c>Kind</c>: attack | blocker | support. <c>Targets</c>: ground | both | none.</summary>
public sealed record TdTowerDef(
    string Id, string Name, string Flavor, string Role, string Kind, string Targets, string? DefaultTargeting,
    long Cost, double Range, double Damage, double Interval, int UnlockStage, Dictionary<string, double> Stats,
    TdTowerLevelDef[] Levels, string[] Strong, string[] Weak, string WeaknessText, TdBranchDef[] Branches);

/// <summary>Inimigo comum (RF-28.7). <c>Counters</c>: torres ("bolt") ou ramos ("bolt.A") que o contra-atacam.</summary>
public sealed record TdEnemyDef(
    string Id, string Name, string Flavor, double Hp, double Speed, double Armor, long Gold, int CoreDamage,
    int IntroStage, string[] Traits, string[] Counters, Dictionary<string, double> Params);

/// <summary>Chefe (RF-28.8): imune a paralisia; <c>SlowCap</c> é a lentidão máxima (0 = imune).</summary>
public sealed record TdBossDef(
    string Id, string Name, string Flavor, int Stage, double Hp, double Speed, double Armor, long Gold, int CoreDamage,
    bool ParalysisImmune, double SlowCap, string Mechanic, string[] Traits, string[] Counters,
    Dictionary<string, double> Params);

public sealed record TdEnemiesDef(TdEnemyDef[] Enemies, TdBossDef[] Bosses);

/// <summary>Grupo de spawn: <c>Count</c> inimigos de <c>Enemy</c> (inimigo ou chefe), um a cada <c>Interval</c> s a partir de <c>Offset</c>.</summary>
public sealed record TdWaveGroupDef(string Enemy, int Count, double Offset, double Interval);

/// <summary>Onda autoral. <c>BonusFactor</c> multiplica o bônus de onda (respiro paga mais).</summary>
public sealed record TdWaveDef(int Number, bool Rest, double BonusFactor, TdWaveGroupDef[] Groups);

public sealed record TdStageWavesDef(int Stage, TdWaveDef[] Waves);

public sealed record TdWavesDef(TdStageWavesDef[] Stages);

public sealed record TdCallNextDef(double BonusBase, double BonusPerWave, double BonusCapFactor, double AbilityCooldownReduction);

public sealed record TdAutoStartDef(string DefaultMode, double DefaultCountdownSeconds, double MinCountdownSeconds, double MaxCountdownSeconds);

public sealed record TdStarsDef(int TwoStarMaxLoss, int ThreeStarMaxLoss);

public sealed record TdChallengeDef(
    string Id, string Name, string Description, int? MaxTowers, bool NoAbilities, bool NoCallNext, double StartingCreditsFactor);

/// <summary>Caixa-bomba (RF-28.19): chance por onda elegível, janela de queda, contagem, recompensa e janela de presença do host.</summary>
public sealed record TdBombCrateDef(
    double Chance, int FirstEligibleWaveStage1, int MinDropSeconds, double DropMaxSpawnFraction, int CountdownSeconds,
    int EdgeMargin, int CooldownReductionSeconds, int PresenceWindowSeconds);

/// <summary>Pergunta-bônus do Preparo (RF-28.23): tempo da pergunta (s) e recompensa por acerto (créditos = base + porOnda × onda; redução de recarga em s).</summary>
public sealed record TdBonusQuestionDef(int TimerSeconds, int CreditsBase, int CreditsPerWave, int CooldownReductionSeconds);

/// <summary>Regras de partida (RF-28.1 a RF-28.4, RF-28.10, RF-28.12).</summary>
public sealed record TdMatchRulesDef(
    int CoreIntegrity, double SellRefund, int[] TowerCostTable, int[] TowerCostTableLow, int WaveBonusBase, int WaveBonusPerWave, double HpWaveGrowth, double HpStageStep,
    double SpawnJitter, int MaxEnemiesOnField, TdCallNextDef CallNext, TdAutoStartDef AutoStart, TdStarsDef Stars,
    TdChallengeDef[] Challenges, TdBombCrateDef BombCrate, TdBonusQuestionDef BonusQuestion);

public sealed record TdStageDef(
    int Id, string Title, string Region, string Concept, int Waves, long StartingCredits, int MaxTowers, string Challenge);

public sealed record TdStagesDef(TdMatchRulesDef Rules, TdStageDef[] Stages);

/// <summary>Habilidade ativa (RF-28.9). Desbloqueio por fase (<c>UnlockStage</c>) ou por nó da árvore (<c>UnlockMetaNode</c>).</summary>
public sealed record TdAbilityDef(
    string Id, string Name, string Effect, double CooldownSeconds, double DurationSeconds, int? UnlockStage,
    string? UnlockMetaNode, int? UsesPerStage, Dictionary<string, double> Stats);

/// <summary>Efeito de nó. <c>Kind</c>: startingCredits | sellRefund | maxTowers | damagePercent | rangePercent | integrity | abilityCooldownPercent | unlockAbility | autoStartBonusPercent.</summary>
public sealed record TdMetaEffectDef(string Kind, double Value, string? Target);

public sealed record TdMetaNodeDef(string Id, string Name, int Cost, TdMetaEffectDef[] Effects);

public sealed record TdMetaBranchDef(string Id, string Name, TdMetaNodeDef[] Nodes);

/// <summary>Selos e árvore de melhorias permanentes (RF-28.13): 3 ramos de 4 nós, custos 1 a 4.</summary>
public sealed record TdMetaDef(int SealsPerStar, int SealsPerChallenge, TdMetaBranchDef[] Branches);

// ---------------------------------------------------------------- runas (SDD-29 §4)

/// <summary>Efeito de runa em pontos-base inteiros (ou contagem/segundos). <c>Target</c>: torre afetada; nulo = a torre do setor (ou global no setor Geral).</summary>
public sealed record TdRuneEffectDef(string Kind, int Value, string? Target);

/// <summary>Nó da página. <c>Type</c>: center | small | notable | keystone | border. <c>Links</c>: arestas (não direcionadas) declaradas neste nó.</summary>
public sealed record TdRuneNodeDef(
    string Id, string Sector, string Type, string Name, string Icon, string Text, int Cost, string[] Links, TdRuneEffectDef[] Effects);

public sealed record TdRuneSectorDef(string Id, string Name, string? Tower);

/// <summary>Custos em créditos por tipo de nó (fixos, sem arbitragem; SDD-29 §4.2).</summary>
public sealed record TdRuneCostsDef(int Small, int Notable, int Border, int Keystone);

/// <summary>Valores-alvo das fontes de crédito da §6 (moeda única). Ainda não consumidos pelo Core: a TD.20/21/22 liga e calibra.</summary>
public sealed record TdCreditSourcesDef(
    int StartingCreditsBonus, int WaveBonusBase, int WaveBonusPerWave, int NoLeakBase, int NoLeakPerWave,
    int CrateBase, int CratePerWave, int BonusQuestionBase, int BonusQuestionPerWave, int BossCredits);

/// <summary>Página de runas (td-runes.json): grafo radial de 61 nós, 6 setores.</summary>
public sealed record TdRunesDef(
    string CenterId, TdRuneCostsDef Costs, int SellRefundCapBp, TdRuneSectorDef[] Sectors, TdCreditSourcesDef CreditSources,
    TdRuneNodeDef[] Nodes);

/// <summary>Conteúdo TD completo e validado; <c>ContentHash</c> = SHA-256 hex dos 7 arquivos <c>td-*.json</c> (com <c>td-runes.json</c>).</summary>
public sealed record TdContent(
    TdTowerDef[] Towers, TdEnemiesDef Enemies, TdWavesDef Waves, TdStagesDef Stages, TdAbilityDef[] Abilities,
    TdMetaDef Meta, TdRunesDef Runes, string ContentHash);
