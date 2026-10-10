using System.Collections.Immutable;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Finale;
using Mist.Core.Progression;
using Mist.Core.Skills;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Finale;

// These are deliberately instrumented mechanism fixtures, NOT normal campaign entry,
// language calibration, human balance approval or proof of a complete 28-level battle.
public static class FinaleBattleCases
{
    private static BattleDefinitions Definitions()=>Combat.CombatCases.Definitions();
    private static BattleUnit U(BattleSnapshot s,string id)=>s.Units.Single(u=>u.Id==id);
    private static BattleSnapshot Fixture(string id="ENC-RF-04",string difficulty="standard",bool idle=true)
    {
        var defs=Definitions();var build=new BuildState(ProgressionRules.AllocateTemplate(28,[4,3,1,1,2]),["M01"],["M01"]);
        var party=defs.Create("BENCH-WORKSHOP",28,4,build,"WP-05","AR-01",[],[],Guid.NewGuid(),Guid.NewGuid(),difficulty);
        using var modes=System.Text.Json.JsonDocument.Parse(defs.Catalog.Definitions["combat_difficulty"][0].PayloadJson);int hp=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty).GetProperty("enemy_hp_bp").GetInt32();
        // Original capacities keep the 1900→1330→665→1 mechanism regression stable.
        // Normal production and normal-route tests use the versioned current candidate.
        var s=FinaleEncounterFactory.Attach(party,id,hp,FinaleEncounterFactory.OriginalNumericVersion);int initiative=30;
        foreach(var u in s.Units.Where(u=>u.Side==CombatSide.Ally)){u.MaxHp=5000;u.Hp=5000;u.MaxFocus=14;u.Focus=14;u.AttackUnits=100000;u.TechUnits=100000;u.Initiative=initiative;initiative-=10;}
        if(idle)foreach(var u in s.Units.Where(u=>u.Side==CombatSide.Enemy))u.Pattern=[new("instrumented inert fixture",0,TargetLock.Unit,false,true)];
        return s;
    }
    private static BattleEngine Start(BattleSnapshot s){var e=new BattleEngine(Definitions(),s);e.Start();return e;}
    private static BattleRequest R(BattleEngine e,string action,string[]? targets=null,string? device=null,int? slot=null)=>new(new(Guid.NewGuid()),action,e.Snapshot().CurrentActor!,targets??[],slot,device,targets?.FirstOrDefault());
    private static void Act(BattleEngine e,string action,string[]? targets=null,string? device=null,int? slot=null)=>e.Apply(R(e,action,targets,device,slot));
    private static void Until(BattleEngine e,string id,int? minTurn=null)
    {for(int n=0;n<120;n++){var s=e.Snapshot();if(s.CurrentActor==id&&(minTurn is null||U(s,id).PersonalTurn>=minTurn))return;Require(s.Outcome==CombatOutcome.Active,"mechanism fixture unexpectedly ended");Act(e,"END");}throw new InvalidOperationException("mechanism clock did not reach actor");}
    private static BattleEngine Linked()
    {var e=Start(Fixture());Act(e,"CONTROL",device:"isolate_left");Act(e,"END");Act(e,"CONTROL",device:"isolate_right");Act(e,"END");Act(e,"CONTROL",device:"verify_link");Until(e,"hero",2);return e;}
    private static void Phase2(BattleEngine e)
    {Act(e,"BASIC",["rf-central"]);Require(e.Snapshot().Finale!.Phase==FinaleCorePhase.AutomaticRestart);int next=e.Snapshot().Round+1;Until(e,"hero",U(e.Snapshot(),"hero").PersonalTurn+1);Require(e.Snapshot().Round==next);}
    private static void StopRelays(BattleEngine e)
    {Act(e,"END");Act(e,"BASIC",["rf-relay-left"]);Act(e,"END");Act(e,"BASIC",["rf-relay-right"]);Until(e,"hero",U(e.Snapshot(),"hero").PersonalTurn+1);}
    public static void Register()
    {
        Add("CP-RF-03","Core actual engine locks phase1 damage and rejects wrong device/stand without resources",()=>
        {
            var e=Start(Fixture());string before=StrictJson.Serialize(e.Snapshot());var invalid=e.Preview(R(e,"CONTROL",device:"verify_link"));Require(!invalid.Legal&&StrictJson.Serialize(e.Snapshot())==before);var locked=e.Preview(R(e,"BASIC",["rf-central"]));Require(locked.Legal&&locked.MechanismLocked&&locked.PredictedDamage==0);Act(e,"BASIC",["rf-central"]);Require(U(e.Snapshot(),"rf-central").Hp==1900&&e.Snapshot().Finale!.Phase==FinaleCorePhase.ForcedLink);Throws<InvalidOperationException>(()=>Act(e,"CONTROL",device:"isolate_right"));
        });
        Add("CP-RF-03","Core overflow is clamped before down; atomic phase change preserves actual resources and devices",()=>
        {
            var e=Linked();var s=e.Snapshot();s.Aid=1;s.Supplies=[new("instrumented-spent","CON-01",true)];U(s,"hero").Focus=6;U(s,"hero").Ready["M01"]=7;U(s,"hero").EncounterUsed.Add("wp5");s.Traps.Add(new("instrumented-trap","hero","rf-central",1000,0,1,1));e=new(Definitions(),s);var p=e.Preview(R(e,"BASIC",["rf-central"]));Require(p.PredictedDamage<=570);Act(e,"BASIC",["rf-central"]);var after=e.Snapshot();Require(U(after,"rf-central").Hp==1330&&after.Finale!.Phase==FinaleCorePhase.AutomaticRestart&&after.Outcome==CombatOutcome.Active);Require(U(after,"hero").Ap==U(s,"hero").Ap-2&&U(after,"hero").Focus==6&&after.Aid==1&&after.Supplies.Single().Used&&U(after,"hero").Ready["M01"]==7&&U(after,"hero").EncounterUsed.Contains("wp5")&&after.Traps.Count==1);Require(after.Intents.All(i=>!i.Attack&&i.RawDamage==0)&&after.PendingReinforcements.Count==2&&after.PendingReinforcements.All(p=>p.EligibleRound==after.Round+1)&&after.Units.All(u=>!u.Id.StartsWith("rf-relay-"))&&after.Events.All(v=>v.Kind!="down"||v.Source!="rf-central"));
        });
        Add("CP-RF-03","Core finite relays join next round with no immediate action and persist through serialization",()=>
        {
            var e=Linked();Phase2(e);var a=e.Snapshot();Require(a.Units.Count(u=>u.Id.StartsWith("rf-relay-"))==2&&a.Units.Where(u=>u.Id.StartsWith("rf-relay-")).All(u=>u.PersonalTurn==0&&u.JoinRound==a.Round));Require(a.Events.All(v=>v.Source is not("rf-relay-left" or "rf-relay-right")||v.Kind!="enemy_action"));var loaded=new BattleEngine(Definitions(),StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(a)));Act(e,"END");Act(loaded,"END");Require(StrictJson.Serialize(e.Snapshot())==StrictJson.Serialize(loaded.Snapshot()));
        });
        Add("CP-RF-03","Core 35 percent gate waits for actual relays and verification; verification transitions without extra hit",()=>
        {
            var e=Linked();Phase2(e);Act(e,"BASIC",["rf-central"]);Require(U(e.Snapshot(),"rf-central").Hp==665&&e.Snapshot().Finale!.Phase==FinaleCorePhase.AutomaticRestart);Act(e,"END");Act(e,"BASIC",["rf-relay-left"]);Act(e,"END");Act(e,"BASIC",["rf-relay-right"]);Until(e,"hero",U(e.Snapshot(),"hero").PersonalTurn+1);Act(e,"CONTROL",device:"latch_bypass");Until(e,"support");Act(e,"CONTROL",device:"verify_bypass");Require(U(e.Snapshot(),"rf-central").Hp==665&&e.Snapshot().Finale!.Phase==FinaleCorePhase.DampingStop&&e.Snapshot().Outcome==CombatOutcome.Active);
        });
        Add("CP-RF-03","Core controlled stop at one is an actual control action; approval preserves verification and changes revoke it",()=>
        {
            var e=Linked();Phase2(e);StopRelays(e);Act(e,"CONTROL",device:"latch_bypass");Act(e,"BASIC",["rf-central"]);Until(e,"support");Act(e,"CONTROL",device:"verify_bypass");Until(e,"hero",U(e.Snapshot(),"hero").PersonalTurn+1);Act(e,"BASIC",["rf-central"]);Require(U(e.Snapshot(),"rf-central").Hp==1&&e.Snapshot().Outcome==CombatOutcome.Active);Until(e,"guard");Act(e,"CONTROL",device:"set_damping");Until(e,"support");Act(e,"CONTROL",device:"verify_damping");var verified=e.Snapshot().Finale!.VerifiedConfiguration;Until(e,"hero",U(e.Snapshot(),"hero").PersonalTurn+1);Act(e,"CONTROL",device:"arm_controlled_stop");var final=e.Snapshot();Require(final.Outcome==CombatOutcome.Controlled&&final.Finale!.ControlledStop&&final.Finale.VerifiedConfiguration==verified&&final.Finale.StopApproval==verified&&U(final,"rf-central").Hp==0);
            var probe=Fixture();probe.Finale!.Phase=FinaleCorePhase.DampingStop;probe.Finale.RelaysScheduled=true;probe.Finale.DampingSet=true;probe.Finale.VerifiedConfiguration=0;probe.Finale.StopApproval=0;foreach(string id in probe.Finale.RelayUnits){var relay=StrictJson.Deserialize<BattleUnit>(StrictJson.Serialize(probe.ReinforcementTemplates[id]));relay.Hp=0;probe.Units.Add(relay);}var other=Start(probe);Until(other,"guard");Act(other,"CONTROL",device:"unset_damping");Require(other.Snapshot().Finale!.VerifiedConfiguration is null&&other.Snapshot().Finale!.StopApproval is null&&other.Snapshot().Finale!.Configuration==1);
        });
        Add("CP-RF-03","Core uses difficulty-derived floors and declared actual capacities",()=>
        {
            using var modes=System.Text.Json.JsonDocument.Parse(Definitions().Catalog.Definitions["combat_difficulty"][0].PayloadJson);foreach(var mode in modes.RootElement.EnumerateArray()){string id=mode.GetProperty("id").GetString()!;var s=Fixture(difficulty:id);var e=Start(s);Act(e,"CONTROL",device:"isolate_left");Act(e,"END");Act(e,"CONTROL",device:"isolate_right");Act(e,"END");Act(e,"CONTROL",device:"verify_link");Until(e,"hero",2);Act(e,"BASIC",["rf-central"]);Require(U(e.Snapshot(),"rf-central").Hp==(int)Math.Ceiling(U(s,"rf-central").MaxHp*.70));}
        });
        Add("CP-RF-03","Rook cannot bypass two actual points; result preserves custody semantics separate from core",()=>
        {
            var e=Start(Fixture("ENC-RF-02"));Act(e,"BASIC",["rf-rook"]);Require(U(e.Snapshot(),"rf-rook").Hp==1&&e.Snapshot().Outcome==CombatOutcome.Active);Act(e,"CONTROL",device:"command_left");Until(e,"guard");Act(e,"CONTROL",device:"command_right");Until(e,"hero",2);Act(e,"BASIC",["rf-rook"]);Until(e,"guard");Act(e,"BASIC",["rf-rook-guard-left"]);Until(e,"support");Act(e,"BASIC",["rf-rook-guard-right"]);Require(e.Snapshot().Outcome==CombatOutcome.Controlled&&e.Snapshot().Finale!.Kind==FinaleMechanismKind.RookCommand&&!e.Snapshot().Finale!.ControlledStop);
        });
        Add("CP-RF-03","Rook combined intention names participants; guard control removes only declared portion and break cancels same attack",()=>
        {
            var s=Fixture("ENC-RF-02",idle:false);U(s,"rf-rook").PatternIndex=2;var e=Start(s);var declared=e.Snapshot().Finale!.Combination!;Require(declared.Parts.Select(p=>p.Unit).SequenceEqual(new[]{"rf-rook","rf-rook-guard-left","rf-rook-guard-right"})&&declared.Parts.Sum(p=>p.RawDamage)==200);var before=e.PreviewIntent("rf-rook");Act(e,"BASIC",["rf-rook-guard-left"]);var after=e.PreviewIntent("rf-rook");Require(after.Losses.All(x=>x.ResolvedDamage<before.Losses.Single(y=>y.Target==x.Target).ResolvedDamage));var snapshot=e.Snapshot();U(snapshot,"hero").Ap=3;U(snapshot,"hero").TechUnits=1;U(snapshot,"hero").Focus=14;U(snapshot,"rf-rook").PressureThreshold=1;U(snapshot,"hero").Learned=["M01"];U(snapshot,"hero").Active=["M01"];var broken=new BattleEngine(Definitions(),snapshot);Act(broken,"M01",["rf-rook"]);Require(broken.Snapshot().Finale!.Combination!.Cancelled&&broken.PreviewIntent("rf-rook").Cancelled);
        });
    }
}
