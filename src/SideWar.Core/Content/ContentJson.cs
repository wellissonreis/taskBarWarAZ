using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideWar.Core.Content;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    ReadCommentHandling = JsonCommentHandling.Disallow)]
[JsonSerializable(typeof(RulesDef))]
[JsonSerializable(typeof(TowerDef[]))]
[JsonSerializable(typeof(EnemyDef[]))]
[JsonSerializable(typeof(WavesDef))]
[JsonSerializable(typeof(StageDef[]))]
[JsonSerializable(typeof(RegionDef[]))]
[JsonSerializable(typeof(MissionsDef))]
[JsonSerializable(typeof(EconomyDef))]
internal sealed partial class ContentJsonContext : JsonSerializerContext;
