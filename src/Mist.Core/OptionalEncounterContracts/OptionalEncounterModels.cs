using System.Collections.Immutable;
using Mist.Core.Assets;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;

namespace Mist.Core.OptionalEncounterContracts;

public sealed record OptionalContractParagraph(string Id,string En,string Zh);
public sealed record OptionalContractEnemy(string Id,string Name,string NameZh,int Slot,int Hp,int ArmorBp,int PressureThreshold,int Initiative,ImmutableArray<EnemyPattern> Pattern);
public sealed record OptionalContractDefinition(string Id,string Region,string Scene,string Spot,string Material,string Title,string TitleZh,string Target,string InspectionAction,string LocalCustodian,ImmutableArray<string> PermissionActions,ImmutableArray<OptionalContractEnemy> Enemies);
public sealed record OptionalContractMaterial(string Id,string Scene,string Spot,string ContentVersion,string AccessClass,ImmutableArray<OptionalContractParagraph> Paragraphs);
public sealed record OptionalContractStand(string Scene,Spot Spot);
public sealed record OptionalContractBands(decimal Reading,decimal Listening,decimal Writing);
public sealed record OptionalContractPackage(string ContentVersion,bool PlayerProjection,string Scope,string ReviewStatus,string HumanReview,string NumericReview,OptionalContractBands TargetBands,ImmutableArray<OptionalContractDefinition> Contracts,ImmutableArray<OptionalContractMaterial> Materials,ImmutableArray<OptionalContractStand> ExtraSpots)
{
    public static OptionalContractPackage Parse(string json){var p=StrictJson.Deserialize<OptionalContractPackage>(json);OptionalContractRules.Validate(p);return p;}
}
public sealed record OptionalContractSource(string Material,string Version,string EnglishFingerprint,string ActualSpot);
public sealed record OptionalContractPermit(string Encounter,Guid Instance,string SourceVersion,string EnglishFingerprint,string Target,string Purpose,string RegionEntryReceipt,bool Allowed);
public sealed record OptionalContractBattle(string Encounter,Guid ContractInstance,string SourceVersion,string Target,BattleSnapshot Battle,BattleSnapshot Entry,ActorState ReturnActor,bool ResultApplied=false);
public sealed record OptionalContractResult(string Encounter,Guid ContractInstance,Guid BattleInstance,Guid Attempt,string SourceVersion,string SourceFingerprint,string Target,string InspectionAction,string PermissionReceipt,string RegionEntryReceipt,bool Controlled,bool Inspected,int Experience=0,long Marks=0,int Parts=0);

public static class OptionalContractRules
{
    public const string Version="OPTIONAL-ENCOUNTERS-20261010-v1",Scope="twelve_independent_local_safety_contracts_candidate",Purpose="control_and_inspect_declared_local_spare";
    public static readonly ImmutableArray<string> Regions=["R1","R2","R3","R4","R5","R6"];
    public static readonly ImmutableDictionary<string,string> Arenas=new Dictionary<string,string>{{"R1","R1-S09"},{"R2","R2-S06"},{"R3","R3-O02"},{"R4","R4-S07"},{"R5","R5-S04"},{"R6","R6-S06"}}.ToImmutableDictionary();
    public static readonly ImmutableArray<string> Ids=Regions.SelectMany(r=>new[]{"ENC-"+r+"-O01","ENC-"+r+"-O02"}).ToImmutableArray();
    public static string Fingerprint(OptionalContractMaterial m)=>AssetText.Hash(string.Join("\n\n",m.Paragraphs.Select(p=>p.En)));
    public static void Validate(OptionalContractPackage p)
    {
        if(p.ContentVersion!=Version||!p.PlayerProjection||p.Scope!=Scope||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||p.NumericReview!="original_candidate_not_calibrated"||p.TargetBands!=new OptionalContractBands(6.5m,6.5m,6.5m)||!p.Contracts.Select(c=>c.Id).SequenceEqual(Ids)||p.Materials.Length!=12||p.Materials.Select(m=>m.Id).Distinct().Count()!=12||p.ExtraSpots.Length!=12||p.Contracts.Select(c=>c.Target).Distinct().Count()!=12||p.Contracts.Select(c=>c.InspectionAction).Distinct().Count()!=12)throw new InvalidDataException("Twelve original optional slots require independent targets and honest pending review.");
        var expected=Regions.SelectMany(r=>new[]{Arenas[r]+".optional_contract_one",Arenas[r]+".optional_contract_two"}).ToArray();
        if(!p.ExtraSpots.Select(s=>s.Spot.Id).SequenceEqual(expected))throw new InvalidDataException("Optional stands cannot replace canonical scene entrances.");
        foreach(var c in p.Contracts)
        {
            if(!Arenas.TryGetValue(c.Region,out string? arena)||c.Scene!=arena||!expected.Contains(c.Spot)||!c.Spot.StartsWith(c.Scene+".",StringComparison.Ordinal)||c.PermissionActions.IsDefault||!c.PermissionActions.SequenceEqual(new[]{"read_declared_contract","control_declared_threats","inspect_declared_target"})||string.IsNullOrWhiteSpace(c.Target)||string.IsNullOrWhiteSpace(c.LocalCustodian)||c.Enemies.IsDefaultOrEmpty||c.Enemies.Length>4||c.Enemies.Select(e=>e.Id).Distinct().Count()!=c.Enemies.Length||c.Enemies.Select(e=>e.Slot).Distinct().Count()!=c.Enemies.Length||c.Enemies.Any(e=>e.Slot is<0 or>3||e.Hp<1||e.ArmorBp is<0 or>4500||e.PressureThreshold is<1 or>8||e.Pattern.IsDefaultOrEmpty||e.Pattern.Any(t=>t.Damage<0||t.Reinforcement is not null)))throw new InvalidDataException("Each contract requires a finite public threat configuration and its own target.");
            var m=p.Materials.SingleOrDefault(m=>m.Id==c.Material);if(m is null||m.Scene!=c.Scene||m.Spot!=c.Spot||m.ContentVersion!=Version||m.AccessClass!="acquired_local_contract"||m.Paragraphs.Length<3||m.Paragraphs.Any(p=>string.IsNullOrWhiteSpace(p.En)||string.IsNullOrWhiteSpace(p.Zh))||m.Paragraphs.Select(p=>p.Id).Distinct().Count()!=m.Paragraphs.Length)throw new InvalidDataException("Full bilingual source, actual stand and immutable version required.");
            string body=string.Join("\n\n",m.Paragraphs.Select(p=>p.En));if(!body.Contains(c.Id,StringComparison.Ordinal)||!body.Contains(c.Target,StringComparison.Ordinal)||!body.Contains(c.InspectionAction,StringComparison.Ordinal))throw new InvalidDataException("The player's source must name its actual contract, target and inspection.");
            if(!p.ExtraSpots.Any(s=>s.Scene==c.Scene&&s.Spot.Id==c.Spot))throw new InvalidDataException("Contract work stand missing.");
        }
    }
    public static ImmutableDictionary<string,SceneGeometry> ExtendGeometry(OptionalContractPackage p,ImmutableDictionary<string,SceneGeometry> original)
    {
        Validate(p);var result=original.ToBuilder();foreach(var extra in p.ExtraSpots)
        {
            if(!result.TryGetValue(extra.Scene,out var scene))continue;
            var old=scene.Spots.SingleOrDefault(s=>s.Id==extra.Spot.Id);if(old is not null){if(old!=extra.Spot)throw new InvalidDataException("Existing optional work stand was relocated.");continue;}
            var nav=new NavigationGrid(scene);if(!nav.CanStand(extra.Spot.Stand,true)||!scene.Spots.Any(s=>nav.CanStand(s.Stand,true)&&!nav.FindPath(s.Stand,extra.Spot.Stand,true).IsEmpty))throw new InvalidDataException("Optional stand must connect safely to an original work stand.");
            result[scene.Id]=scene with{Spots=scene.Spots.Add(extra.Spot)};
        }
        return result.ToImmutable();
    }
}
