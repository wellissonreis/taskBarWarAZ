namespace SideWar.Core.Content.Td;

// Conteúdo TD v2 (SDD-28 RF-28.4 a RF-28.13), lado a lado com o conteúdo idle (SDD-12/27): namespace e arquivos
// `td-*.json` novos, hash próprio. Distâncias em unidades lógicas (mundo de 1.200 unidades), tempos em segundos.

/// <summary>Torre (td-towers.json). <c>Kind</c>: attack | blocker | support. <c>Targets</c>: ground | both | none.</summary>
public sealed record TdTowerDef(
    string Id, string Name, string Flavor, string Role, string Kind, string Targets, string? DefaultTargeting,
    long Cost, double Range, double Damage, double Interval, int UnlockStage, Dictionary<string, double> Stats,
    string[] Strong, string[] Weak, string WeaknessText, string Category = "attack");

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

/// <summary>Pergunta-bônus do Preparo (RF-28.23): tempo da pergunta (s) e recompensa por acerto (créditos = base + porOnda × onda; redução de recarga em s).</summary>
public sealed record TdBonusQuestionDef(int TimerSeconds, int CreditsBase, int CreditsPerWave, int CooldownReductionSeconds);

/// <summary>Fragmentos de runa (TD.29a): ganhos por onda vencida, pergunta certa e chefe derrotado.</summary>
public sealed record TdFragmentsDef(int PerWave, int PerQuestion, int PerBoss);

/// <summary>Regras de partida (RF-28.1 a RF-28.4, RF-28.10, RF-28.12).</summary>
public sealed record TdMatchRulesDef(
    int CoreIntegrity, double SellRefund, int[] TowerCostTable, int[] TowerCostTableLow, int WaveBonusBase, int WaveBonusPerWave, double HpWaveGrowth, double HpStageStep,
    double SpawnJitter, int MaxEnemiesOnField, TdCallNextDef CallNext, TdAutoStartDef AutoStart, TdStarsDef Stars,
    TdChallengeDef[] Challenges, TdBonusQuestionDef BonusQuestion, TdFragmentsDef Fragments);

public sealed record TdStageDef(
    int Id, string Title, string Region, string Concept, int Waves, long StartingCredits, string Challenge);

public sealed record TdStagesDef(TdMatchRulesDef Rules, TdStageDef[] Stages);

/// <summary>Habilidade ativa (RF-28.9). Desbloqueio por fase (<c>UnlockStage</c>) ou por nó da árvore (<c>UnlockMetaNode</c>).</summary>
public sealed record TdAbilityDef(
    string Id, string Name, string Effect, double CooldownSeconds, double DurationSeconds, int? UnlockStage,
    string? UnlockMetaNode, int? UsesPerStage, Dictionary<string, double> Stats);

/// <summary>Efeito de nó. <c>Kind</c>: startingCredits | sellRefund | maxTowers | damagePercent | rangePercent | xpShield | abilityCooldownPercent | unlockAbility | autoStartBonusPercent.</summary>
public sealed record TdMetaEffectDef(string Kind, double Value, string? Target);

public sealed record TdMetaNodeDef(string Id, string Name, int Cost, TdMetaEffectDef[] Effects);

public sealed record TdMetaBranchDef(string Id, string Name, TdMetaNodeDef[] Nodes);

/// <summary>Selos e árvore de melhorias permanentes (RF-28.13): 3 ramos de 4 nós, custos 1 a 4.</summary>
public sealed record TdMetaDef(int SealsPerStar, int SealsPerChallenge, TdMetaBranchDef[] Branches);

// ---------------------------------------------------------------- runas (SDD-29 §4)

/// <summary>Tipo de runa da teia (TD.29a): <c>Effect</c> é o identificador do efeito e <c>Value</c> o valor por runa despertada (bp, créditos ou décimos de segundo).</summary>
public sealed record TdRuneTypeDef(string Id, string Name, string Effect, int Value, string Text);

/// <summary>Nó fixo da teia: anel (0 a 3), setor (0 a 7), tipo (id de <see cref="TdRuneTypeDef"/>) e posição normalizada (0 a 1) para a UI.</summary>
public sealed record TdRuneNodeDef(string Id, int Ring, int Sector, string Type, double X, double Y);

/// <summary>Fontes de crédito da §6 (moeda única), consumidas pelo Core desde a TD.21; calibração na TD.22.</summary>
public sealed record TdCreditSourcesDef(
    int StartingCreditsBonus, int WaveBonusBase, int WaveBonusPerWave, int NoLeakBase, int NoLeakPerWave,
    int BonusQuestionBase, int BonusQuestionPerWave, int BossCredits);

/// <summary>Teia de runas (td-runes.json): 32 nós fixos (4 anéis × 8 setores), 8 tipos e as fontes de crédito da §6.</summary>
public sealed record TdRunesDef(TdCreditSourcesDef CreditSources, TdRuneTypeDef[] Types, TdRuneNodeDef[] Nodes);

// ---------------------------------------------------------------- fusão de torres (SDD-37 §3)

/// <summary>
/// Par nomeado ORDENADO base+módulo (SDD-37 RF-37.1/§3). <c>Effect</c>: hitDamage | hitSlow | hitSplash | hitChain | hitCorrode | nodeSlow | periodicDamage | periodicSlow.
/// <c>Params</c> (todos positivos): damagePct, jumps, armorPct, seconds, intervalSeconds, rangeFromModule (1 = alcance do módulo; 0 = da base).
/// </summary>
public sealed record TdFusionPairDef(string Base, string Module, string Name, string Effect, Dictionary<string, double> Params);

/// <summary>
/// Fusão (td-fusion.json): fase mínima, ordem de resolução documentada (base, módulo, aura), papéis por torre (attack, control, support, economy),
/// módulo genérico por papel (RF-37.5) e pares nomeados.
/// </summary>
public sealed record TdFusionDef(
    int MinStage, string[] ResolveOrder, Dictionary<string, string[]> Roles,
    Dictionary<string, Dictionary<string, double>> Generic, TdFusionPairDef[] Pairs, TdFusionNamesDef? Names = null);

/// <summary>Nome próprio de um par base+módulo que NÃO tem efeito nomeado (continua no efeito genérico por papel): só troca o rótulo.</summary>
public sealed record TdFusionNameOverride(string Base, string Module, string Name);

/// <summary>
/// Nomes de fusão num único lugar (td-fusion.json, "names"). Todo par sem nome próprio vira "{Substantivo da base} {adjetivo do módulo}" concordando em gênero:
/// <c>Gender</c> (id -> "m" | "f"), <c>Adjective</c> (id -> [masculino, feminino] ou [invariável]), <c>Same</c> (base = módulo, usa <c>{noun}</c>) e <c>Overrides</c>.
/// Só muda o rótulo: nenhum efeito nem número depende daqui.
/// </summary>
public sealed record TdFusionNamesDef(
    Dictionary<string, string> Gender, Dictionary<string, string[]> Adjective, string Same, TdFusionNameOverride[] Overrides);

// ---------------------------------------------------------------- mutações de inimigos (SDD-37 §4)

/// <summary>
/// Traço de mutação (RF-37.7). Fatores sobre os valores escalados da base: HP, velocidade, armadura (soma) e escudo inicial (fração do HP máximo escalado).
/// <c>Excludes</c>: traços mutuamente exclusivos (simétrico). Disponível a partir da fase absoluta <c>MinStage</c>.
/// </summary>
public sealed record TdMutationTraitDef(
    string Id, string Name, string Label, int MinStage, double HpMult, double SpeedMult, double ArmorAdd, double ShieldPct, bool Flying, string[] Excludes);

/// <summary>
/// Degrau da progressão (RF-37.8): vale com fase absoluta &gt;= <c>MinStage</c> e <c>MinWaves</c> ondas já iniciadas antes da onda atual.
/// <c>MaxMutatedPct</c> = fração dos não chefes (piso); <c>MaxDoubles</c> = mutados com dois traços (-1 = usar <c>DoublePct</c> dos mutados, piso).
/// </summary>
public sealed record TdMutationTierDef(int MinStage, int MinWaves, double MaxMutatedPct, int MaxDoubles, double DoublePct);

/// <summary>Mutações (td-mutations.json): catálogo de traços, degraus por fase/onda, recompensa e limites de velocidade.</summary>
public sealed record TdMutationsDef(
    double RewardPerTrait, double RewardCap, double SpeedMin, double SpeedMax, int DoubleGapWaves, int DoubleMinMutated, int MinEligibleForOne,
    TdMutationTraitDef[] Traits, TdMutationTierDef[] Tiers);

/// <summary>Conteúdo TD completo e validado; <c>ContentHash</c> = SHA-256 hex dos 9 arquivos <c>td-*.json</c> (com <c>td-runes.json</c>, <c>td-fusion.json</c> e <c>td-mutations.json</c>).</summary>
public sealed record TdContent(
    TdTowerDef[] Towers, TdEnemiesDef Enemies, TdWavesDef Waves, TdStagesDef Stages, TdAbilityDef[] Abilities,
    TdMetaDef Meta, TdRunesDef Runes, string ContentHash, TdFusionDef Fusion, TdMutationsDef Mutations);
