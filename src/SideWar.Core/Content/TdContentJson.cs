using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideWar.Core.Content.Td;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    ReadCommentHandling = JsonCommentHandling.Disallow)]
[JsonSerializable(typeof(TdTowerDef[]))]
[JsonSerializable(typeof(TdEnemiesDef))]
[JsonSerializable(typeof(TdWavesDef))]
[JsonSerializable(typeof(TdStagesDef))]
[JsonSerializable(typeof(TdAbilityDef[]))]
[JsonSerializable(typeof(TdMetaDef))]
[JsonSerializable(typeof(TdRunesDef))]
internal sealed partial class TdContentJsonContext : JsonSerializerContext;
