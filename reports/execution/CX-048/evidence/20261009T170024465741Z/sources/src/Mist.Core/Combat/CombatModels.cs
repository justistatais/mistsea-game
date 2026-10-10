using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Inventory;
using Mist.Core.Progression;
using Mist.Core.Skills;

namespace Mist.Core.Combat;

public enum CombatOutcome { Active,Controlled,Defeat,Withdrawn }
public enum TargetLock { Unit,Column,All }
public enum CombatSide { Ally,Enemy }
public enum ActionKind { Damage,Shield,CleanseShield,Stance,Empower,Trap,StepShield,StepAllyShield }
public sealed record CombatActionDefinition(string Id,string Name,int Ap,int Focus,int Cooldown,ActionKind Kind,string? Power,int Coefficient,int Shield,int Pressure,int PressureIfExposed,bool ExposeAfter,int MaximumTargets,int GuardBp,int WeakenBp,int BonusBp);
public sealed record EnemyPattern(string Name,int Damage,TargetLock Lock,bool Attack,bool Interruptible,string? Reinforcement=null);
public sealed record BoundExpiry(string Source,int PersonalTurn,bool AtEnd=false);
public sealed record GuardEffect(string Id,int Amount,BoundExpiry Expiry);
public sealed record DamageOverTime(string Id,int Raw,int RemainingSlots);
public sealed record EnemyIntent(string Source,string Name,int RawDamage,TargetLock Lock,string[] Units,int[] Slots,bool Attack,bool Interruptible,string? Reinforcement=null);
public sealed record PendingReinforcement(string Source,string Template,int EligibleRound,int Deferrals=0);
public sealed record BattleTrap(string Id,string Owner,string Target,int Coefficient,int BasePressure,int PaidFocus,long Sequence,string? WardTarget=null);
public sealed record CarriedSupply(string Instance,string Definition,bool Used=false);
public sealed record BattleRequest(CommandId Command,string Action,string Actor,string[] Targets,int? Destination=null,string? Device=null,string? PressureTarget=null,string? WardTarget=null);
public sealed record BattleEvent(int Round,string Kind,string Source,string? Target,int Value,string Detail);
public sealed class BattleUnit
{
    public string Id {get;set;}="";public string Name {get;set;}="";public CombatSide Side {get;set;}public int Slot {get;set;}public int Level {get;set;}=1;
    public int MaxHp {get;set;}public int Hp {get;set;}public int MaxFocus {get;set;}public int Focus {get;set;}
    public int AttackUnits {get;set;}public int TechUnits {get;set;}public int ArmorBp {get;set;}public int GuardBase {get;set;}public int ExposureBonus {get;set;}public int Strength {get;set;}=5;public int Initiative {get;set;}=10;
    public int DamageBonus {get;set;}public int FlatPressure {get;set;}public WeaponScaling Scaling {get;set;}public string Weapon {get;set;}="";public string Armor {get;set;}="";
    public string[] Learned {get;set;}=[];public string[] Active {get;set;}=[];public string[] Relics {get;set;}=[];public Dictionary<int,string> Variants {get;set;}=new();
    public int Ap {get;set;}public int PersonalTurn {get;set;}public int EligibleRound {get;set;}=1;public int JoinRound {get;set;}=1;
    public int Shield {get;set;}public BoundExpiry? ShieldExpiry {get;set;}public List<GuardEffect> Guards {get;set;}=[];
    public int NextGuard {get;set;}public BoundExpiry? NextGuardExpiry {get;set;}public int Empowered {get;set;}public BoundExpiry? EmpowerExpiry {get;set;}public int OutgoingWeakness {get;set;}public BoundExpiry? WeaknessExpiry {get;set;}
    public int NextShieldBonus {get;set;}public BoundExpiry? ShieldBonusExpiry {get;set;}public bool Exposed {get;set;}public int ExposureUntilRound {get;set;}
    public List<DamageOverTime> Dots {get;set;}=[];public BoundExpiry? RestraintExpiry {get;set;}public Dictionary<string,int> Ready {get;set;}=new();
    public List<string> TurnUsed {get;set;}=[];public List<string> EncounterUsed {get;set;}=[];public int Refunds {get;set;}public string? DeviceWardTarget {get;set;}
    public int Pressure {get;set;}public int PressureThreshold {get;set;}public int LastPressureRound {get;set;}=-1;public bool PendingBreak {get;set;}public bool RecoveryLocked {get;set;}public bool Boss {get;set;}
    public EnemyPattern[] Pattern {get;set;}=[];public int PatternIndex {get;set;}
    [JsonIgnore] public bool Alive=>Hp>0;
}
public sealed class BattleSnapshot
{
    public string RulesVersion {get;set;}="mist-rules-1";public string Definition {get;set;}="";public Guid Instance {get;set;}public Guid Attempt {get;set;}public string Difficulty {get;set;}="standard";
    public int EnemyDamageBp {get;set;}=10000;public int Round {get;set;}public List<BattleUnit> Units {get;set;}=[];public List<BattleTrap> Traps {get;set;}=[];public List<EnemyIntent> Intents {get;set;}=[];
    public string[] Queue {get;set;}=[];public int Cursor {get;set;}=-1;public string? CurrentActor {get;set;}public int Aid {get;set;}=2;public List<CarriedSupply> Supplies {get;set;}=[];
    public string? Mechanism {get;set;}public Dictionary<string,bool> Controls {get;set;}=new();public long Sequence {get;set;}public CombatOutcome Outcome {get;set;}=CombatOutcome.Active;
    public List<BattleEvent> Events {get;set;}=[];
    public Dictionary<string,BattleUnit> ReinforcementTemplates {get;set;}=new();
    public List<PendingReinforcement> PendingReinforcements {get;set;}=[];
}
public sealed class BattleDefinitions
{
    public ImmutableDictionary<string,CombatActionDefinition> Actions {get;}
    public ItemCatalog Items {get;}
    public ImmutableDictionary<string,TalentDefinition> Talents {get;}
    public RuntimeCatalog Catalog {get;}
    public BattleDefinitions(RuntimeCatalog catalog)
    {
        Catalog=catalog;Items=new(catalog);Talents=TalentRules.Read(catalog);
        Actions=catalog.Definitions["combat_actions"].Select(d=>
        {
            using var doc=JsonDocument.Parse(d.PayloadJson);var j=doc.RootElement;int I(string key,int fallback=0)=>j.TryGetProperty(key,out var p)?p.GetInt32():fallback;bool B(string key)=>j.TryGetProperty(key,out var p)&&p.GetBoolean();
            var kind=j.GetProperty("kind").GetString() switch{"damage"=>ActionKind.Damage,"shield"=>ActionKind.Shield,"cleanse_shield"=>ActionKind.CleanseShield,"stance"=>ActionKind.Stance,"empower"=>ActionKind.Empower,"trap"=>ActionKind.Trap,"step_shield"=>ActionKind.StepShield,"step_ally_shield"=>ActionKind.StepAllyShield,_=>throw new InvalidDataException("Unknown action kind.")};
            return new CombatActionDefinition(d.Id,d.Name,I("ap"),I("focus"),I("cd"),kind,j.TryGetProperty("power",out var power)?power.GetString():null,I("coefficient_bp"),I("shield_bp"),I("pressure"),I("pressure_if_exposed"),B("expose_after"),I("max_targets",1),I("guard_bp"),I("weaken_after_bp"),I("bonus_bp"));
        }).ToImmutableDictionary(a=>a.Id);
        if(Actions.Count!=30||Items.Items.Count!=28||Items.Modules.Count!=8||Talents.Count!=36)throw new InvalidDataException("Combat definition counts changed.");
    }
    public BuildState ReferenceBuild(string id)
    {
        using var doc=JsonDocument.Parse(Catalog.Definitions["combat_builds"][0].PayloadJson);var j=doc.RootElement.EnumerateArray().Single(b=>b.GetProperty("id").GetString()==id);return ReadBuild(j);
    }
    private static BuildState ReadBuild(JsonElement j)=>new(StrictJson.Deserialize<Attributes>(j.GetProperty("stats").GetRawText()),j.GetProperty("skills").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray(),j.GetProperty("active").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray());
    public BattleSnapshot CreateReference(string buildId,string scenarioId,Guid instance,Guid attempt,string difficulty="standard")
    {
        using var builds=JsonDocument.Parse(Catalog.Definitions["combat_builds"][0].PayloadJson);var b=builds.RootElement.EnumerateArray().Single(x=>x.GetProperty("id").GetString()==buildId);
        using var scene=JsonDocument.Parse(Catalog.Definitions["combat_scenarios"].Single(x=>x.Id==scenarioId).PayloadJson);int level=scene.RootElement.GetProperty("level").GetInt32(),grade=scene.RootElement.GetProperty("grade").GetInt32();
        if(b.GetProperty("level").GetInt32()!=level)throw new InvalidOperationException("Reference build uses post-result level.");
        return Create(scenarioId,level,grade,ReadBuild(b),b.GetProperty("weapon").GetString()!,b.GetProperty("armor").GetString()!,[],[],instance,attempt,difficulty);
    }
    public BattleSnapshot Create(string scenarioId,int level,int grade,BuildState build,string weapon,string armor,IEnumerable<string> relics,IEnumerable<ModuleDefinition> modules,Guid instance,Guid attempt,string difficulty="standard",string[]? helperIds=null)
    {
        TalentRules.Validate(level,build,Talents);using var doc=JsonDocument.Parse(Catalog.Definitions["combat_scenarios"].Single(s=>s.Id==scenarioId).PayloadJson);var scene=doc.RootElement;
        using var modes=JsonDocument.Parse(Catalog.Definitions["combat_difficulty"][0].PayloadJson);var mode=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty);
        if(instance==Guid.Empty||attempt==Guid.Empty)throw new InvalidOperationException("Encounter identities must be issued by the session owner.");
        var s=new BattleSnapshot{Definition=scene.TryGetProperty("source_encounter",out var encounter)?encounter.GetString()!:scenarioId,Instance=instance,Attempt=attempt,Difficulty=difficulty,EnemyDamageBp=mode.GetProperty("enemy_damage_bp").GetInt32(),Mechanism=scene.GetProperty("mechanism").ValueKind==JsonValueKind.Null?null:scene.GetProperty("mechanism").GetString()};
        if(s.Mechanism is not(null or "tower"))throw new InvalidOperationException("Scenario mechanism requires an implemented owner.");
        if(s.Mechanism=="tower")s.Controls=new(){{"left",false},{"right",false},{"isolate",false}};
        s.Units.Add(Actor("hero","主角",0,level,grade,build,weapon,armor,relics.ToArray(),modules));
        using var helpers=JsonDocument.Parse(Catalog.Definitions["combat_companions"][0].PayloadJson);
        helperIds??=["AUX-GUARD","AUX-SUPPORT"];
        if(helperIds.Length!=2||helperIds.Distinct().Count()!=2)throw new InvalidOperationException("Exactly two unique lawful assistance seats are required.");
        for(int index=0;index<helperIds.Length;index++)
        {
            var spec=helpers.RootElement.EnumerateArray().Single(h=>h.GetProperty("id").GetString()==helperIds[index]);
            var profile=spec.GetProperty("profile");var stats=ProgressionRules.AllocateTemplate(level,new[]{"body","strength","agility","perception","focus"}.Select(k=>profile.GetProperty(k).GetInt32()).ToArray());
            string id=helperIds[index];string name=id switch{"AUX-GUARD"=>"地方护卫","AUX-SUPPORT"=>"地方支援","CP-LINA"=>"莉娜","CP-OREN"=>"奥伦","CP-MIRA"=>"米拉","CP-TESS"=>"泰丝",_=>throw new InvalidDataException("Unknown assistance actor.")};
            var helper=Actor(id=="AUX-GUARD"?"guard":id=="AUX-SUPPORT"?"support":id,name,index==0?1:2,level,grade,new(stats,[],[]),spec.GetProperty("weapon").GetString()!,spec.GetProperty("armor").GetString()!,[],[]);helper.Active=spec.GetProperty("native").EnumerateArray().Select(x=>x.GetString()!).ToArray();s.Units.Add(helper);
        }
        foreach(var enemy in scene.GetProperty("enemies").EnumerateArray())
        {
            int hp=enemy.GetProperty("hp").GetInt32()*mode.GetProperty("enemy_hp_bp").GetInt32()/10000;
            string enemyId=enemy.GetProperty("id").GetString()!;
            s.Units.Add(new(){Id=enemyId,Name=enemyId switch{"maintenance"=>"维护机械","tower"=>"潮门守卫",_=>enemyId},Side=CombatSide.Enemy,Slot=enemy.GetProperty("slot").GetInt32(),MaxHp=hp,Hp=hp,ArmorBp=enemy.GetProperty("armor_bp").GetInt32(),PressureThreshold=enemy.GetProperty("pressure_threshold").GetInt32(),Boss=enemy.GetProperty("boss").GetBoolean(),Initiative=enemy.GetProperty("initiative").GetInt32(),Pattern=enemy.GetProperty("pattern").EnumerateArray().Select(p=>new EnemyPattern(p.GetProperty("name").GetString()!,p.GetProperty("damage").GetInt32(),p.GetProperty("target").GetString() switch{"unit"=>TargetLock.Unit,"column"=>TargetLock.Column,"all"=>TargetLock.All,_=>throw new InvalidDataException()},p.GetProperty("attack").GetBoolean(),p.GetProperty("interruptible").GetBoolean())).ToArray()});
        }
        return s;
    }
    private BattleUnit Actor(string id,string name,int slot,int level,int grade,BuildState build,string weapon,string armor,string[] relics,IEnumerable<ModuleDefinition> modules)
    {
        var d=ProgressionRules.Derive(level,grade,build.Attributes,Items.Items[weapon],Items.Items[armor],relics,modules);
        return new(){Id=id,Name=name,Side=CombatSide.Ally,Slot=slot,Level=level,MaxHp=d.MaxHp,Hp=d.MaxHp,MaxFocus=d.MaxFocus,Focus=d.MaxFocus,AttackUnits=d.AttackUnits,TechUnits=d.TechUnits,ArmorBp=d.ArmorBp,GuardBase=d.GuardBp,ExposureBonus=d.ExposureBp,Strength=build.Attributes.Strength,Initiative=d.Initiative,Weapon=weapon,Armor=armor,Scaling=Items.Items[weapon].Scaling!.Value,Learned=build.Learned.ToArray(),Active=build.Active.ToArray(),Relics=relics,DamageBonus=d.DamageBonusBp,FlatPressure=d.FlatPressure};
    }
}
