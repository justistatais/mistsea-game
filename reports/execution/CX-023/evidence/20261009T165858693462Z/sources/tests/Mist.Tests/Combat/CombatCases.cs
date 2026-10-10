using System.Collections.Immutable;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Progression;
using Mist.Core.Skills;
using Mist.DebugCandidates;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Combat;

public static class CombatCases
{
    public static BattleDefinitions Definitions()=>new(Q102.Q102Cases.Catalog());
    public static BattleSnapshot Reference(string build="BUILD-Q102-ENTRY-H",string scenario="BENCH-WORKSHOP")=>Definitions().CreateReference(build,scenario,Guid.NewGuid(),Guid.NewGuid());
    private static BattleRequest R(BattleEngine engine,string action,string[]? targets=null,string? device=null,int? destination=null)=>new(new(Guid.NewGuid()),action,engine.Snapshot().CurrentActor!,targets??[],destination,device);
    private static BattleUnit U(BattleSnapshot s,string id)=>s.Units.Single(u=>u.Id==id);
    private static void Until(BattleEngine e,string actor,int personalTurn)
    {
        for(int count=0;count<60;count++){var s=e.Snapshot();if(s.CurrentActor==actor&&U(s,actor).PersonalTurn>=personalTurn)return;e.Apply(R(e,"END"));}throw new InvalidOperationException("Clock did not reach target.");
    }
    public static void Register()
    {
        Add("CX-040","Threshold edges, 62 attributes and 16 skills; Q102 result level differs from entry",()=>
        {
            Require(ProgressionRules.Threshold(3)==240&&ProgressionRules.Threshold(4)==420&&ProgressionRules.Threshold(5)==640&&ProgressionRules.Threshold(30)==19140);
            Require(ProgressionRules.Level(239)==2&&ProgressionRules.Level(240)==3&&ProgressionRules.Level(340)==3&&ProgressionRules.Level(500)==4&&ProgressionRules.Level(19140)==30);
            Require(ProgressionRules.AttributeBudget(30)==62&&ProgressionRules.SkillBudget(1)==0&&ProgressionRules.SkillBudget(2)==2&&ProgressionRules.SkillBudget(30)==16);
            Require(ProgressionRules.Grade(["Q-102:resolution"])==0&&ProgressionRules.Grade(["Q-104:resolution"])==1);
            var def=Definitions();var stats=ProgressionRules.Derive(3,0,new(7,9,6,6,5),def.Items.Items["WP-01"],def.Items.Items["AR-01"]);Require(stats.MaxHp==108&&stats.AttackUnits==276&&stats.TechUnits==244&&stats.ArmorBp==800&&stats.MaxFocus==6);
            Throws<InvalidOperationException>(()=>new Attributes(25,25,25,25,25).Validate(30));Throws<ArgumentOutOfRangeException>(()=>ProgressionRules.Level(-1));
            Throws<InvalidOperationException>(()=>def.CreateReference("BUILD-Q102-H","BENCH-WORKSHOP",Guid.NewGuid(),Guid.NewGuid()));
        });
        Add("CX-041","Whole build prerequisites cannot self-fund; passives use no active slot",()=>
        {
            var def=Definitions();Require(def.Talents.Count==36);TalentRules.Validate(3,def.ReferenceBuild("BUILD-Q102-ENTRY-H"),def.Talents);
            Throws<InvalidOperationException>(()=>TalentRules.Validate(3,new(new(),["H07"],["H07"]),def.Talents));
            Throws<InvalidOperationException>(()=>TalentRules.Validate(3,new(new(),["H01","H01"],["H01"]),def.Talents));
            foreach(string id in new[]{"BUILD-H","BUILD-M","BUILD-S","BUILD-HM"})TalentRules.Validate(30,def.ReferenceBuild(id),def.Talents);
            var p=def.ReferenceBuild("BUILD-H");Throws<InvalidOperationException>(()=>TalentRules.Validate(30,p with{Active=p.Active.Add("H03")},def.Talents));
            for(int level=1;level<=30;level++){var stats=ProgressionRules.AllocateTemplate(level,[4,3,1,1,2]);Require(stats.Values.Sum(v=>v-5)==ProgressionRules.AttributeBudget(level));stats.Validate(level);}
        });
        Add("CX-044","Damage floors once, capped pools and exact golden values",()=>
        {
            Require(CombatMath.Damage(276,11000,0,0,1000,0)==27);
            Require(CombatMath.Damage(1000,10000,9999,9999,9999,9999)==75);
            Require(CombatMath.Damage(244,13000,0,600,1000,0)==30);
            Require(CombatMath.Shield(244,7500,1000)==20&&CombatMath.Pressure(2,1,25)==4&&CombatMath.Pressure(0,0,25)==0&&CombatMath.Pressure(10,10,25)==6);
            Throws<InvalidDataException>(()=>CombatMath.Shield(-1,1000,0));Throws<InvalidDataException>(()=>CombatMath.Damage(1,1000,-1,0,0,0));
        });
        Add("CX-043","Ally phase first, stable ID ties, no UI refill and CD1 skips an entire personal slot",()=>
        {
            var s=Reference();U(s,"maintenance").MaxHp=10000;U(s,"maintenance").Hp=10000;U(s,"maintenance").Pattern=[new("idle",0,TargetLock.Unit,false,true)];
            foreach(var u in s.Units)u.Initiative=10;var e=new BattleEngine(Definitions(),s);e.Start();Require(e.Snapshot().CurrentActor=="guard");Until(e,"hero",1);
            e.Apply(R(e,"H01",["maintenance"]));var after=e.Snapshot();Require(U(after,"hero").Ap==1&&U(after,"hero").Ready["H01"]==3);var restored=new BattleEngine(Definitions(),after);Require(StrictJson.Serialize(restored.Snapshot())==StrictJson.Serialize(after));Throws<InvalidOperationException>(restored.Start);
            Until(restored,"hero",2);Require(!restored.Preview(R(restored,"H01",["maintenance"])).Legal);Until(restored,"hero",3);Require(restored.Preview(R(restored,"H01",["maintenance"])).Legal);
        });
        Add("CX-044","Shield takes max and source expiry survives source down; weak guard cannot renew strong guard",()=>
        {
            var s=Reference();U(s,"hero").Initiative=20;U(s,"maintenance").Pattern=[new("idle",0,TargetLock.Unit,false,true)];var hero=U(s,"hero");hero.Guards=[new("strong",3000,new("guard",1)),new("weak",1000,new("support",2))];hero.Shield=40;hero.ShieldExpiry=new("guard",2);U(s,"guard").Hp=0;
            var e=new BattleEngine(Definitions(),s);e.Start();Require(U(e.Snapshot(),"hero").Guards.Count==2);e.Apply(R(e,"END"));Require(U(e.Snapshot(),"guard").PersonalTurn==1&&U(e.Snapshot(),"hero").Guards.Count==1&&U(e.Snapshot(),"hero").Guards[0].Amount==1000);
            e.Apply(R(e,"AUX-SUPPORT-2",["hero"]));Require(U(e.Snapshot(),"hero").Shield==40);Until(e,"support",2);Require(U(e.Snapshot(),"hero").Shield==40);Until(e,"support",3);Require(U(e.Snapshot(),"hero").Shield==0);
        });
        Add("CX-045","Ordinary stagger cancels once, consumes pattern index and needs recovery slot",()=>
        {
            var s=Reference("BUILD-Q102-ENTRY-M");U(s,"maintenance").PressureThreshold=1;U(s,"maintenance").MaxHp=10000;U(s,"maintenance").Hp=10000;U(s,"hero").Initiative=20;var e=new BattleEngine(Definitions(),s);e.Start();e.Apply(R(e,"M01",["maintenance"]));
            Require(U(e.Snapshot(),"maintenance").PendingBreak&&U(e.Snapshot(),"maintenance").RecoveryLocked);Until(e,"hero",2);
            Require(e.Snapshot().Events.Count(x=>x.Kind=="intent_cancelled")==1&&U(e.Snapshot(),"maintenance").PatternIndex==1&&U(e.Snapshot(),"maintenance").RecoveryLocked);
            e.Apply(R(e,"M02",["maintenance"]));Until(e,"hero",3);Require(!U(e.Snapshot(),"maintenance").RecoveryLocked&&e.Snapshot().Events.Count(x=>x.Kind=="device_damage")==1);
        });
        Add("CX-045","Boss break weakens one attack while interlocks and recovery remain",()=>
        {
            var s=Reference("BUILD-TOWER-M","BENCH-TOWER");s.Controls["left"]=true;s.Controls["right"]=true;U(s,"tower").PressureThreshold=1;U(s,"hero").Initiative=20;var e=new BattleEngine(Definitions(),s);e.Start();e.Apply(R(e,"M01",["tower"]));Until(e,"hero",2);
            Require(e.Snapshot().Events.Any(x=>x.Kind=="enemy_action")&&!e.Snapshot().Events.Any(x=>x.Kind=="intent_cancelled")&&U(e.Snapshot(),"tower").RecoveryLocked&&!e.Snapshot().Controls["isolate"]);
        });
        Add("CX-045","Revive waits until next round; down personal clock still advances; aid is shared",()=>
        {
            var s=Reference();U(s,"hero").Hp=0;U(s,"hero").Initiative=20;U(s,"maintenance").Pattern=[new("idle",0,TargetLock.Unit,false,true)];var e=new BattleEngine(Definitions(),s);e.Start();Require(e.Snapshot().CurrentActor=="guard"&&U(e.Snapshot(),"hero").PersonalTurn==1);
            e.Apply(R(e,"REVIVE",["hero"]));Require(e.Snapshot().Aid==1&&U(e.Snapshot(),"hero").EligibleRound==2&&U(e.Snapshot(),"hero").Ap==0);Require(!e.Preview(R(e,"AID",["hero"])).Legal);
            Until(e,"hero",2);Require(U(e.Snapshot(),"hero").Ap==3&&e.Snapshot().Aid==1);
            var defeat=Reference();foreach(var u in defeat.Units.Where(u=>u.Side==CombatSide.Ally))u.Hp=0;var allDown=new BattleEngine(Definitions(),defeat);allDown.Start();Require(allDown.Snapshot().Outcome==CombatOutcome.Defeat);
        });
        Add("CX-046","Unit locks follow person; column locks follow positions without hidden retarget",()=>
        {
            foreach(bool column in new[]{false,true})
            {
                var s=Reference();U(s,"hero").Initiative=20;U(s,"maintenance").Pattern=[new("lock",40,column?TargetLock.Column:TargetLock.Unit,true,true)];var e=new BattleEngine(Definitions(),s);e.Start();var frozen=e.Snapshot().Intents.ToArray();e.Apply(R(e,"MOVE",destination:1));Until(e,"hero",2);
                Require(StrictJson.Serialize(frozen)==StrictJson.Serialize(e.Snapshot().Events.Where(x=>x.Kind=="intents_declared").Take(1).Select(x=>StrictJson.Deserialize<EnemyIntent[]>(x.Detail)).Single()));
                var eventHit=e.Snapshot().Events.Where(x=>x.Kind=="damage"&&x.Source=="maintenance").Select(x=>x.Target).ToArray();Require(column?eventHit.Contains("guard")&&!eventHit.Contains("hero"):eventHit.SequenceEqual(new[]{"hero"}));
            }
        });
        Add("CX-046","Rear melee blocked, swap budget once, missing targets reject without mutation",()=>
        {
            var s=Reference();var rear=StrictJson.Deserialize<BattleUnit>(StrictJson.Serialize(U(s,"maintenance")));rear.Id="rear";rear.Slot=2;s.Units.Add(rear);U(s,"hero").Initiative=20;var e=new BattleEngine(Definitions(),s);e.Start();var before=StrictJson.Serialize(e.Snapshot());
            Require(!e.Preview(R(e,"BASIC",["rear"])).Legal);Throws<InvalidOperationException>(()=>e.Apply(R(e,"BASIC",["missing"])));Require(before==StrictJson.Serialize(e.Snapshot()));e.Apply(R(e,"MOVE",destination:1));Require(!e.Preview(R(e,"MOVE",destination:0)).Legal&&U(e.Snapshot(),"guard").Slot==0);
        });
        Add("CX-045","A canceled or preparatory enemy attack cannot consume traps; paid recovery refunds once",()=>
        {
            var s=Reference("BUILD-Q102-ENTRY-M");U(s,"hero").Initiative=20;U(s,"maintenance").Pattern=[new("idle",0,TargetLock.Unit,false,true)];U(s,"hero").Learned=["M01","M02","M09"];var e=new BattleEngine(Definitions(),s);e.Start();e.Apply(R(e,"M02",["maintenance"]));string trap=e.Snapshot().Traps.Single().Id;int focus=U(e.Snapshot(),"hero").Focus;
            e.Apply(R(e,"RECLAIM",device:trap));Require(U(e.Snapshot(),"hero").Focus==focus+1&&U(e.Snapshot(),"hero").Refunds==1&&!e.Preview(R(e,"RECLAIM",device:trap)).Legal);
            Until(e,"hero",4);e.Apply(R(e,"M02",["maintenance"]));Until(e,"hero",5);Require(e.Snapshot().Traps.Count==1&&!e.Snapshot().Events.Any(x=>x.Kind=="device_damage"));
        });
        Add("CX-047","All six assistance kits use 2 AP; helper basic preview equals actual 0.65 resolution",()=>
        {
            var d=Definitions();foreach(string helper in new[]{"CP-LINA","CP-OREN","CP-MIRA","CP-TESS","AUX-GUARD","AUX-SUPPORT"})
            {
                var s=d.Create("BENCH-WORKSHOP",3,0,d.ReferenceBuild("BUILD-Q102-ENTRY-H"),"WP-01","AR-01",[],[],Guid.NewGuid(),Guid.NewGuid(),helperIds:[helper,helper=="AUX-SUPPORT"?"AUX-GUARD":"AUX-SUPPORT"]);
                string id=helper=="AUX-GUARD"?"guard":helper=="AUX-SUPPORT"?"support":helper;U(s,id).Initiative=30;var e=new BattleEngine(d,s);e.Start();Require(e.Snapshot().CurrentActor==id&&U(e.Snapshot(),id).Ap==2);
                var request=R(e,"BASIC",["maintenance"]);int predicted=e.Preview(request).PredictedDamage;e.Apply(request);Require(U(e.Snapshot(),"maintenance").Hp==190-predicted&&U(e.Snapshot(),id).Ap==0);
            }
        });
        Add("CX-048","Three actual level-3 workshop candidates finish with finite resources and no rare equipment",()=>
        {
            foreach(string route in new[]{"H","M","S"})
            {
                var e=new BattleEngine(Definitions(),Reference("BUILD-Q102-ENTRY-"+route));e.Start();var final=BattleCandidate.Complete(e);Require(final.Outcome==CombatOutcome.Controlled&&final.Aid is >=0 and <=2&&final.Units.Where(u=>u.Side==CombatSide.Ally).All(u=>u.Level==3));Write("workshop-"+route,final);
            }
        });
        Add("CX-049","Three actual level-5 tower candidates, both valve orders, no grade-1 advance",()=>
        {
            foreach(string route in new[]{"H","M","S"})foreach(bool rightFirst in new[]{false,true})
            {
                var e=new BattleEngine(Definitions(),Reference("BUILD-TOWER-"+route,"BENCH-TOWER"));e.Start();var final=BattleCandidate.Complete(e,rightFirst);Require(final.Controls.Values.All(v=>v)&&final.Outcome==CombatOutcome.Controlled&&final.Units.Where(u=>u.Side==CombatSide.Ally).All(u=>u.ArmorBp==800));Write("tower-"+route+"-"+rightFirst,final);
            }
        });
        Add("CX-049","Tower damage stays zero before valves and 1HP before isolation; cleansing cannot unlock it",()=>
        {
            var s=Reference("BUILD-TOWER-H","BENCH-TOWER");U(s,"hero").Initiative=20;U(s,"hero").AttackUnits=100000;s.Controls["left"]=false;s.Controls["right"]=false;var e=new BattleEngine(Definitions(),s);e.Start();Require(e.Preview(R(e,"BASIC",["tower"])).MechanismLocked);e.Apply(R(e,"BASIC",["tower"]));Require(U(e.Snapshot(),"tower").Hp==310);
            e.Apply(R(e,"CONTROL",device:"left"));e.Apply(R(e,"END"));e.Apply(R(e,"CONTROL",device:"right"));e.Apply(R(e,"END"));e.Apply(R(e,"END"));Require(e.Snapshot().CurrentActor=="hero");e.Apply(R(e,"BASIC",["tower"]));Require(U(e.Snapshot(),"tower").Hp==1&&e.Snapshot().Outcome==CombatOutcome.Active&&!e.Snapshot().Controls["isolate"]);
        });
    }
    private static void Write(string name,BattleSnapshot state)
    {
        string directory=Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"combat-candidates");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,name+".json"),StrictJson.Serialize(state));
    }
}
