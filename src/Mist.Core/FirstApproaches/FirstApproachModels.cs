using System.Collections.Immutable;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;

namespace Mist.Core.FirstApproaches;

public sealed record ApproachBands(decimal Reading,decimal Listening,decimal Writing);
public sealed record ApproachParagraph(string Id,string En,string Zh);
public sealed record ApproachMaterial(string Id,string Scene,string Spot,string Version,ImmutableArray<ApproachParagraph> Paragraphs);
public sealed record ApproachEnemy(string Id,string Template,int Slot,int Hp,int ArmorBp,int PressureThreshold,ImmutableArray<EnemyPattern> Pattern);
public sealed record ApproachDefinition(string Id,string Quest,string Scene,string Spot,string SourceMaterial,int EntryLevelFloor,int Grade,string Roster,string? ExclusiveRouteGroup,ImmutableArray<ApproachEnemy> Enemies);
public sealed record ApproachExtraSpot(string Scene,Spot Spot);
public sealed record FirstApproachPackage(string ContentVersion,bool PlayerProjection,string ReviewStatus,string HumanReview,ApproachBands TargetBands,string NumericProvenance,ImmutableArray<ApproachDefinition> Encounters,ImmutableArray<ApproachMaterial> Materials,ImmutableArray<ApproachExtraSpot> ExtraSpots)
{
    public static FirstApproachPackage Parse(string json){var p=StrictJson.Deserialize<FirstApproachPackage>(json);FirstApproachRules.Validate(p);return p;}
}
public sealed record SideGateMaintenance(bool Isolated=false,bool Braced=false,bool ZeroEnergy=false);
public sealed record FirstApproachResult(string Encounter,string Quest,Guid QuestInstance,Guid Instance,Guid Attempt,string Method,string SourceVersion,bool Completed);
public sealed record FirstApproachBattle(string Encounter,Guid QuestInstance,string SourceVersion,BattleSnapshot Battle,BattleSnapshot Entry,ActorState ReturnActor,bool ResultApplied=false);
public static class FirstApproachRules
{
    public const string Version="FIRST-APPROACHES-20261010-v1",Relay="ENC-R0-02",Side="ENC-R1-02",Outer="ENC-R1-03";
    public static void Validate(FirstApproachPackage p)
    {
        if(p.ContentVersion!=Version||!p.PlayerProjection||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||p.TargetBands!=new ApproachBands(6.5m,6.5m,6.5m)||p.NumericProvenance!="original_candidate_hp_damage_armor_with_frozen_template_thresholds"||!p.Encounters.Select(e=>e.Id).SequenceEqual(new[]{Relay,Side,Outer})||p.Materials.Length!=3||p.ExtraSpots.Length!=2)throw new InvalidDataException("Three registered approach contracts need exact version and honest pending review.");
        foreach(var e in p.Encounters)
        {
            bool relay=e.Id==Relay;string spot=e.Id switch{Relay=>"R0-S05.relay",Side=>"R1-S09.side_gate",_=>"R1-S09.outer_gate"};
            if(e.Quest!=(relay?"Q-P03":"Q-104")||e.Scene!=(relay?"R0-S05":"R1-S09")||e.Spot!=spot||e.EntryLevelFloor!=(relay?2:5)||e.Grade!=0||e.Roster!=(relay?"hero_only_prologue_specific":"hero_plus_two_legal_helpers")||e.ExclusiveRouteGroup!=(relay?null:"tower_base_access")||e.Enemies.IsDefaultOrEmpty||e.Enemies.Length>2||e.Enemies.Any(x=>x.Hp<1||x.ArmorBp is<0 or>4500||x.PressureThreshold!=(x.Template=="relay_drone"?3:x.Template=="braced_warden"?5:-1)||x.Pattern.IsDefaultOrEmpty)||!p.Materials.Any(m=>m.Id==e.SourceMaterial&&m.Version==Version&&m.Scene==e.Scene&&m.Paragraphs.Length>=2))throw new InvalidDataException("Approach source, template threshold, prior entry or alternative route changed.");
        }
        foreach(var material in p.Materials)if(material.Paragraphs.Any(p=>string.IsNullOrWhiteSpace(p.En)||string.IsNullOrWhiteSpace(p.Zh)))throw new InvalidDataException("Full bilingual source paragraphs required.");
        if(!p.ExtraSpots.Select(x=>x.Scene+"/"+x.Spot.Id).SequenceEqual(new[]{"R1-S09/R1-S09.side_gate","R1-S09/R1-S09.outer_gate"}))throw new InvalidDataException("Only two safe local work stands may extend the existing tower base.");
    }
    public static ImmutableDictionary<string,SceneGeometry> ExtendGeometry(FirstApproachPackage p,ImmutableDictionary<string,SceneGeometry> original)
    {
        Validate(p);var geometry=original.ToBuilder();foreach(var extra in p.ExtraSpots)
        {
            if(!geometry.TryGetValue(extra.Scene,out var scene))continue;var existing=scene.Spots.SingleOrDefault(x=>x.Id==extra.Spot.Id);if(existing is not null){if(existing!=extra.Spot)throw new InvalidDataException("An existing work stand cannot be relocated.");continue;}
            if(!new NavigationGrid(scene).CanStand(extra.Spot.Stand,true)||scene.Ports.Any(port=>new NavigationGrid(scene).FindPath(port.Stand,extra.Spot.Stand,true).IsEmpty))throw new InvalidDataException("Approach stand must be safely reachable without changing original ports or walls.");geometry[scene.Id]=scene with{Spots=scene.Spots.Add(extra.Spot)};
        }
        return geometry.ToImmutable();
    }
}
