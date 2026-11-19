using System.Globalization;
using SideWar.Core.Content.Td;
using SideWar.Core.Td;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var c = TdContentLoader.LoadEmbedded();
Console.WriteLine("scenario,profile,seed,won,phase,seconds,integrity_lost,credits,towers,diversity_kill_types,hash,cleared");
foreach (var p in new[]{TdBotProfile.Novato, TdBotProfile.Otimizado})
foreach (ulong seed in Enumerable.Range(100,20).Select(x => (ulong)x)) {
 for (int stage=1;stage<=6;stage++) {
  var r=TdSanityBot.Run(c,stage,p,seed);
  var repeat=TdSanityBot.Run(c,stage,p,seed);
  if(r.StateHash!=repeat.StateHash) throw new InvalidOperationException($"Divergencia {stage} {p} {seed}");
  Console.WriteLine($"fase{stage},{p},{seed},{r.Won},{r.Phase},{r.CombatSeconds},{r.IntegrityLost},{r.CreditsLeft},{r.TowersBuilt},{r.KillsByTowerType.Count(k=>k.Key != "zona" && k.Value>0)},{r.StateHash},");
 }
 var run=TdSanityBot.RunContinuous(c,1,p,seed,2);
 var again=TdSanityBot.RunContinuous(c,1,p,seed,2);
 if(run.StateHash!=again.StateHash) throw new InvalidOperationException($"Divergencia continua {p} {seed}");
 Console.WriteLine($"ate_fase3,{p},{seed},{run.StagesCleared>=2},{run.Phase},{run.CombatSeconds},{run.Stages.Sum(x=>x.IntegrityLost)},{run.CreditsLeft},{run.TowersBuilt},,{run.StateHash},{run.StagesCleared}");
}
