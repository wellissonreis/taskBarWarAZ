namespace SideWar.Core.Content;

/// <summary>Constantes gerais de combate (rules.json, SDD-12 RF-12.3).</summary>
public sealed record RulesDef(
    int MaxEnemiesOnField, double SlotRangeDip, double SplashRadius, double SplashFactor,
    double ChilledBonus, double CannonVsRunner, double FrostSlowFactor, double FrostSlowFloor,
    int BoltSuperHitEvery, double BoltSuperHitMultiplier);

/// <summary>Torre (towers.json, SDD-27 RF-27.4). <c>UnlockStage</c> é o id (1-based) da fase que a desbloqueia.</summary>
public sealed record TowerDef(
    string Id, string Name, string AzureTheme, long Cost, int RangeSlots, double Damage, double Interval,
    int? UnlockStage, double? DamageBonusPerBolt, string[] Strong, string[] Weak, string Description);

/// <summary>Inimigo (enemies.json, SDD-27 RF-27.6). <c>Speed</c> em DIP/s.</summary>
public sealed record EnemyDef(
    string Id, string Name, double Hp, double Gold, double Xp, double Armor, int CoreDamage, double Speed, string Sprite);

public sealed record WaveCountDef(int Base, int PerWaves, int Cap);

public sealed record WaveIntervalDef(double Start, double Slope, double Floor);

public sealed record WaveMixDef(string Kind, double Start, double Slope, double Cap);

/// <summary>Ondas (waves.json, SDD-27 RF-27.7): quantidade, intervalo, perfil de mistura e chefe.</summary>
public sealed record WavesDef(
    WaveCountDef Count, WaveIntervalDef Interval, WaveMixDef[] Mix,
    int BossEvery, double BossRegularFactor, int BossRegularMin);

/// <summary>Fase (stages.json, SDD-27 RF-27.9). Títulos das fases 08 a 10 são provisórios (D-14).</summary>
public sealed record StageDef(int Id, string Title, bool TitleProvisional, int Waves, string StageTopic);

/// <summary>Região (regions.json, SDD-27 RF-27.10). <c>Unlock</c>: always | firstMigration | reserves.</summary>
public sealed record RegionDef(
    string Id, string Name, double HpMultiplier, double ReservesMultiplier, string Unlock, int? UnlockReserves);

/// <summary>Recompensa de missão. <c>Kind</c>: chestRare | incomeMinutes | reserves | xp.</summary>
public sealed record MissionRewardDef(string Kind, long Amount);

public sealed record MissionDef(string Id, string Title, long Target, MissionRewardDef Reward);

public sealed record MissionsDef(int DailyCount, MissionDef[] Missions);

public sealed record CoreDef(int BaseHp, int HpPerLevel);

public sealed record PrestigeDef(int MinWave, int Divisor);

public sealed record ChestDef(double IncomeMinutes, long Xp, long Reserves);

public sealed record ChestsDef(ChestDef Common, ChestDef Rare);

/// <summary>Economia (economy.json, SDD-27 RF-27.14).</summary>
public sealed record EconomyDef(
    long StartingCredits, int MaxTowerLevel, double BuildCostFactor, double UpgradeCostFactor,
    double UpgradeCostGrowth, double DamageGrowth, double HpGrowth, double GoldGrowth, double XpGrowth,
    double StageHpStep, int XpPerLevel, CoreDef Core, int FallWaves, double WaveRestSeconds,
    int RateWindowSeconds, double ReservesBonus, PrestigeDef Prestige, ChestsDef Chests);

/// <summary>Conteúdo completo, validado e imutável por convenção; <c>ContentHash</c> = SHA-256 hex (minúsculas).</summary>
public sealed record GameContent(
    RulesDef Rules, TowerDef[] Towers, EnemyDef[] Enemies, WavesDef Waves, StageDef[] Stages,
    RegionDef[] Regions, MissionsDef Missions, EconomyDef Economy, Events.QuestionBank Questions, string ContentHash);
