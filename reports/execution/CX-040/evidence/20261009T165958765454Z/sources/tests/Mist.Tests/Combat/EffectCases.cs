using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Progression;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Combat;

// Isolated rule fixtures intentionally normalize power/HP. They are not campaign builds or balance evidence.
public static class EffectCases
{
    private static BattleUnit U(BattleSnapshot s,string id)=>s.Units.Single(u=>u.Id==id);
    private static BattleSnapshot Fixture(string action,string[]? passives=null,string weapon="WP-01",string armor="AR-01",string[]? relics=null)
    {
        var s=CombatCases.Reference();foreach(var u in s.Units){u.MaxHp=1000;u.Hp=1000;u.MaxFocus=14;u.Focus=14;u.AttackUnits=1000;u.TechUnits=1000;u.ArmorBp=0;u.GuardBase=2000;u.ExposureBonus=500;u.Strength=5;u.Learned=[];u.Relics=[];}
        var h=U(s,"hero");h.Level=30;h.Initiative=30;h.Active=[action];h.Learned=passives??[];h.Weapon=weapon;h.Armor=armor;h.Relics=relics??[];h.Scaling=CombatCases.Definitions().Items.Items[weapon].Scaling!.Value;
        var enemy=U(s,"maintenance");enemy.MaxHp=10000;enemy.Hp=10000;enemy.PressureThreshold=100;enemy.Pattern=[new("idle fixture",0,TargetLock.Unit,false,true)];return s;
    }
    private static BattleEngine Start(BattleSnapshot s){var e=new BattleEngine(CombatCases.Definitions(),s);e.Start();return e;}
    private static void Act(BattleEngine e,string action,string[]? targets=null,int? slot=null,string? device=null,string? ward=null)=>e.Apply(new(new(Guid.NewGuid()),action,e.Snapshot().CurrentActor!,targets??[],slot,device,targets?.FirstOrDefault(),ward));
    private static void Round(BattleEngine e)
    {
        int old=U(e.Snapshot(),"hero").PersonalTurn;for(int n=0;n<10;n++){Act(e,"END");if(e.Snapshot().CurrentActor=="hero"&&U(e.Snapshot(),"hero").PersonalTurn>old)return;}throw new InvalidOperationException("Did not return to hero.");
    }
    private static void ExtraEnemies(BattleSnapshot s,int count)
    {
        for(int n=1;n<count;n++){var u=StrictJson.Deserialize<BattleUnit>(StrictJson.Serialize(U(s,"maintenance")));u.Id="enemy"+n;u.Slot=n;s.Units.Add(u);}
    }
    public static void Register()
    {
        var damage=new Dictionary<string,int>{{"H01",110},{"H04",130},{"H07",135},{"H10",190},{"M01",95},{"M04",70},{"M07",115},{"M10",90},{"S02",95}};
        foreach(var pair in damage)Add("CX-052",pair.Key+" actual damage, single root event, AP/focus/cooldown",()=>
        {
            var s=Fixture(pair.Key);var def=CombatCases.Definitions().Actions[pair.Key];ExtraEnemies(s,def.MaximumTargets);var targets=s.Units.Where(u=>u.Side==CombatSide.Enemy).Select(u=>u.Id).ToArray();var e=Start(s);Act(e,pair.Key,targets);var after=e.Snapshot();
            foreach(string target in targets)Require(U(after,target).Hp==10000-pair.Value);Require(after.Events.Count(x=>x.Kind=="damage")==targets.Length&&U(after,"hero").Ap==3-def.Ap&&U(after,"hero").Focus==14-def.Focus&&U(after,"hero").Ready[pair.Key]==def.Cooldown+2);
            if(pair.Key=="H01")Require(U(after,"maintenance").Exposed);if(pair.Key=="M10")Require(targets.All(t=>U(after,t).OutgoingWeakness==1500));
        });
        foreach(var pair in new Dictionary<string,int>{{"M06",70},{"S01",75},{"S06",30},{"S07",40},{"S10",60},{"H06",25}})Add("CX-052",pair.Key+" actual shield and distinct targets, no healing or device repair",()=>
        {
            var s=Fixture(pair.Key);U(s,"hero").Dots=[new("ordinary",10,2)];U(s,"guard").Dots=[new("ordinary",10,2)];var e=Start(s);string[] targets=pair.Key is "S07" or "S10"?["hero","guard","support"]:pair.Key=="H06"?["hero"]:["guard"];
            int before=U(e.Snapshot(),targets[0]).Hp;Act(e,pair.Key,targets,pair.Key=="H06"?1:null);var after=e.Snapshot();foreach(string target in targets)Require(U(after,target).Shield==pair.Value);Require(U(after,targets[0]).Hp==before);
            if(pair.Key=="H06")Require(U(after,"hero").Slot==1&&!e.Preview(new(new(Guid.NewGuid()),"MOVE","hero",[],0)).Legal);if(pair.Key=="S06")Require(U(after,"guard").Dots.Count==0);if(pair.Key=="S10")Require(targets.All(t=>U(after,t).Guards.Single().Amount==1500));
        });
        Add("CX-052","H02 stance and S04 empowerment bind to source clock, never return AP",()=>
        {
            var h=Start(Fixture("H02"));Act(h,"H02",["hero"]);Require(U(h.Snapshot(),"hero").Empowered==2000&&U(h.Snapshot(),"hero").Ap==2&&U(h.Snapshot(),"hero").Guards.Single().Amount==1000);
            var s=Start(Fixture("S04"));Act(s,"S04",["guard"]);Require(U(s.Snapshot(),"guard").Empowered==2000&&U(s.Snapshot(),"hero").Ap==2);
        });
        Add("CX-052","M02 deploys paid unique device and consumes exactly one when a real attack begins",()=>
        {
            var s=Fixture("M02");U(s,"maintenance").Pattern=[new("attack fixture",0,TargetLock.Unit,true,true)];var e=Start(s);Act(e,"M02",["maintenance"]);Require(e.Snapshot().Traps.Single().PaidFocus==2&&U(e.Snapshot(),"hero").Ap==2);Round(e);Require(U(e.Snapshot(),"maintenance").Hp==9955&&e.Snapshot().Traps.Count==0&&e.Snapshot().Events.Count(x=>x.Kind=="device_damage")==1);
        });
        Add("CX-052","H03 A damage bonus and H05 exposed bonus are effectful",()=>
        {
            var h=Start(Fixture("BASIC",["H03"]));Act(h,"BASIC",["maintenance"]);Require(U(h.Snapshot(),"maintenance").Hp==9892);
            var s=Fixture("BASIC",["H05"]);U(s,"maintenance").Exposed=true;var e=Start(s);Act(e,"BASIC",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9885&&!U(e.Snapshot(),"maintenance").Exposed);
        });
        Add("CX-052","H08 one encounter refund, H09 next-hit guard, H11 exposed coefficient, H12 threshold",()=>
        {
            var s=Fixture("GUARD",["H08"]);U(s,"hero").Focus=4;var e=Start(s);Act(e,"GUARD");Require(U(e.Snapshot(),"hero").Focus==6&&U(e.Snapshot(),"hero").Refunds==1);Round(e);Act(e,"GUARD");Require(U(e.Snapshot(),"hero").Refunds==1);
            s=Fixture("BASIC",["H09"]);U(s,"maintenance").Exposed=true;e=Start(s);Act(e,"BASIC",["maintenance"]);Require(U(e.Snapshot(),"hero").NextGuard==1500);
            s=Fixture("H10",["H11"]);U(s,"maintenance").Exposed=true;e=Start(s);Act(e,"H10",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9785);
            s=Fixture("GUARD",["H12"]);U(s,"hero").Hp=499;e=Start(s);Act(e,"GUARD");Require(U(e.Snapshot(),"hero").Guards.Single().Amount==3000);
        });
        Add("CX-052","M03 boosts technical active damage but excludes tech basic; M05 grants one derived shield",()=>
        {
            var e=Start(Fixture("M01",["M03"],"WP-05"));Act(e,"M01",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9898);
            e=Start(Fixture("BASIC",["M03"],"WP-05"));Act(e,"BASIC",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9900);
            e=Start(Fixture("M02",["M05"]));Act(e,"M02",["maintenance"]);Require(U(e.Snapshot(),"hero").Shield==20&&e.Snapshot().Events.Count(x=>x.Kind=="shield")==1);
        });
        Add("CX-052","M08 permits two devices but one fires per source; M11 and M12 use nonrecursive budgets",()=>
        {
            var s=Fixture("M02",["M08","M11","M12"]);s.Sequence=1;s.Traps=[new("trap:1","hero","maintenance",4500,1,2,1,"guard")];U(s,"maintenance").Pattern=[new("attack fixture",0,TargetLock.Unit,true,true)];var e=Start(s);Act(e,"M02",["maintenance"],ward:"support");Require(e.Snapshot().Traps.Count==2);Round(e);
            var after=e.Snapshot();Require(after.Traps.Count==1&&U(after,"maintenance").Hp==9946&&U(after,"guard").Shield==25&&U(after,"support").Shield==0&&after.Events.Count(x=>x.Kind=="device_damage")==1&&after.Events.Count(x=>x.Kind=="shield")==1);
        });
        Add("CX-052","M09 cannot refund a zero-fee fixture or another owner's device",()=>
        {
            var s=Fixture("RECLAIM",["M09"]);s.Traps=[new("zero","hero","maintenance",4500,1,0,1),new("other","guard","maintenance",4500,1,1,2)];var e=Start(s);Require(!e.Preview(new(new(Guid.NewGuid()),"RECLAIM","hero",[],Device:"other")).Legal);Act(e,"RECLAIM",device:"zero");Require(U(e.Snapshot(),"hero").Refunds==0);
        });
        Add("CX-052","S03 shield pool and S05 other-target self shield cannot recurse",()=>
        {
            var e=Start(Fixture("S01",["S03"]));Act(e,"S01",["hero"]);Require(U(e.Snapshot(),"hero").Shield==82);
            e=Start(Fixture("S01",["S05"]));Act(e,"S01",["guard"]);Require(U(e.Snapshot(),"guard").Shield==75&&U(e.Snapshot(),"hero").Shield==20&&e.Snapshot().Events.Count(x=>x.Kind=="shield")==2);
        });
        Add("CX-052","S08 survives threshold damage once and never shields a downed actor",()=>
        {
            foreach(int raw in new[]{150,1000})
            {
                var s=Fixture("END",["S08"]);U(s,"hero").Hp=600;U(s,"maintenance").Pattern=[new("threshold fixture",raw,TargetLock.Unit,true,true)];var e=Start(s);
                if(raw==150)Round(e);else {int round=e.Snapshot().Round;for(int n=0;n<4&&e.Snapshot().Round==round;n++)Act(e,"END");Require(e.Snapshot().Round==round+1&&e.Snapshot().CurrentActor!="hero");}
                var h=U(e.Snapshot(),"hero");Require(raw==150?h.Hp==450&&h.Shield==35:h.Hp==0&&h.Shield==0);if(raw==150){Round(e);Require(e.Snapshot().Events.Count(x=>x.Kind=="shield")==1);}
            }
        });
        Add("CX-052","S09 next proactive shield, S11 per-target coefficient and S12 one original-fee discount",()=>
        {
            var e=Start(Fixture("S01",["S09"]));Act(e,"GUARD");Act(e,"S01",["hero"]);Require(U(e.Snapshot(),"hero").Shield==86&&U(e.Snapshot(),"hero").NextShieldBonus==0);
            e=Start(Fixture("S07",["S11"]));Act(e,"S07",["hero","guard","support"]);Require(e.Snapshot().Units.Where(u=>u.Side==CombatSide.Ally).All(u=>u.Shield==50));
            e=Start(Fixture("S10",["S12"],"WP-05"));Act(e,"S10",["hero","guard","support"]);Require(U(e.Snapshot(),"hero").Focus==12&&U(e.Snapshot(),"hero").Ap==0&&U(e.Snapshot(),"hero").EncounterUsed.Contains("s12")&&U(e.Snapshot(),"hero").EncounterUsed.Contains("wp5"));
        });
        Add("CX-052","WP01 ordered basic guard, WP02 hammer and WP03 first basic only",()=>
        {
            var e=Start(Fixture("BASIC"));Act(e,"BASIC",["maintenance"]);Act(e,"GUARD");Require(U(e.Snapshot(),"hero").Guards.Single().Amount==2500);
            e=Start(Fixture("BASIC",weapon:"WP-02"));Act(e,"BASIC",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9910&&U(e.Snapshot(),"maintenance").Pressure==1);
            e=Start(Fixture("BASIC",weapon:"WP-03"));Act(e,"BASIC",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9885);Round(e);Act(e,"BASIC",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9785);
        });
        Add("CX-052","WP04 requires existing exposure; WP05 discount persists; WP06 first proactive shield",()=>
        {
            var s=Fixture("BASIC",weapon:"WP-04");U(s,"maintenance").Exposed=true;var e=Start(s);Act(e,"BASIC",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==9885&&!U(e.Snapshot(),"maintenance").Exposed);
            e=Start(Fixture("M01",weapon:"WP-05"));Act(e,"M01",["maintenance"]);Require(U(e.Snapshot(),"hero").Focus==13);Round(e);Round(e);int before=U(e.Snapshot(),"hero").Focus;Act(e,"M01",["maintenance"]);Require(U(e.Snapshot(),"hero").Focus==before-2);
            e=Start(Fixture("S01",weapon:"WP-06"));Act(e,"S01",["hero"]);Require(U(e.Snapshot(),"hero").Shield==82&&U(e.Snapshot(),"hero").TurnUsed.Contains("wp6"));
        });
        Add("CX-052","AR01 baseline, AR02/03 initiative, AR04 device shield, AR05 DOT, AR06 other shield",()=>
        {
            var defs=CombatCases.Definitions().Items;DerivedStats D(string armor)=>ProgressionRules.Derive(1,0,new(),defs.Items["WP-01"],defs.Items[armor]);Require(D("AR-01").ArmorBp==800&&D("AR-02").Initiative==8&&D("AR-03").Initiative==12);
            var s=Fixture("M02",armor:"AR-04");U(s,"maintenance").Pattern=[new("attack fixture",0,TargetLock.Unit,true,true)];var e=Start(s);Act(e,"M02",["maintenance"]);Round(e);Require(U(e.Snapshot(),"hero").Shield==20&&e.Snapshot().Events.Count(x=>x.Kind=="shield")==1);
            s=Fixture("END",armor:"AR-05");U(s,"hero").Dots=[new("ordinary",20,2)];e=Start(s);Require(U(e.Snapshot(),"hero").Hp==982&&U(e.Snapshot(),"hero").Dots.Single().RemainingSlots==1);
            e=Start(Fixture("S01",armor:"AR-06"));Act(e,"S01",["guard"]);Require(U(e.Snapshot(),"guard").Shield==82);
        });
        Add("CX-052","RE01/02/05/06/08 conditional effects produce actual shield, pressure and device damage",()=>
        {
            var e=Start(Fixture("GUARD",relics:["RE-01"]));Act(e,"GUARD");Require(U(e.Snapshot(),"hero").Shield==25);
            e=Start(Fixture("S01",relics:["RE-02"]));Act(e,"S01",["hero"]);Require(U(e.Snapshot(),"hero").Shield==82);
            e=Start(Fixture("M01",relics:["RE-05"]));Act(e,"M01",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Pressure==2);
            var s=Fixture("M02",relics:["RE-06"]);U(s,"maintenance").Pattern=[new("attack fixture",0,TargetLock.Unit,true,true)];e=Start(s);Act(e,"M02",["maintenance"]);Round(e);Require(U(e.Snapshot(),"maintenance").Hp==9951);
            e=Start(Fixture("S01",relics:["RE-08"]));Act(e,"S01",["guard"]);Require(U(e.Snapshot(),"hero").Shield==15&&e.Snapshot().Events.Count(x=>x.Kind=="shield")==2);
        });
        Add("CX-052","RE03/04/07/09/11 static effects derive from actual equipped definitions",()=>
        {
            var defs=CombatCases.Definitions().Items;var d=ProgressionRules.Derive(1,0,new(),defs.Items["WP-01"],defs.Items["AR-01"],["RE-03","RE-04","RE-07","RE-09","RE-11"]);Require(d.ArmorBp==1100&&d.Initiative==12&&d.ExposureBp==1000&&d.MaxFocus==7&&d.GuardBp==2500);
        });
        Add("CX-052","RE10 successful ordinary cleanse only; RE12 revive shield only once per encounter",()=>
        {
            var s=Fixture("S06",relics:["RE-10"]);U(s,"guard").Dots=[new("ordinary",10,2)];var e=Start(s);Act(e,"S06",["guard"]);Require(U(e.Snapshot(),"guard").Dots.Count==0&&U(e.Snapshot(),"hero").EncounterUsed.Contains("re10"));
            s=Fixture("REVIVE",relics:["RE-12"]);U(s,"guard").Hp=0;e=Start(s);Act(e,"REVIVE",["guard"]);Require(U(e.Snapshot(),"guard").Hp==200&&U(e.Snapshot(),"guard").Shield==25&&U(e.Snapshot(),"guard").EligibleRound==2);
        });
        foreach(var pair in new Dictionary<string,int>{{"CON-01",700},{"CON-02",150},{"CON-03",9},{"CON-04",400}})Add("CX-052",pair.Key+" actual carried supply effect and once-per-slot ownership",()=>
        {
            var s=Fixture("SUPPLY");U(s,"hero").Hp=200;U(s,"hero").Focus=5;U(s,"hero").Dots=pair.Key=="CON-02"?[new("ordinary",0,2)]:[];s.Supplies=[new("paid-fixture",pair.Key)];var e=Start(s);Act(e,"SUPPLY",["hero"],device:"paid-fixture");var h=U(e.Snapshot(),"hero");Require(pair.Key=="CON-01"?h.Hp==pair.Value:pair.Key=="CON-03"?h.Focus==pair.Value:h.Shield==pair.Value);Require(e.Snapshot().Supplies.Single().Used&&e.Snapshot().Aid==2&&!e.Preview(new(new(Guid.NewGuid()),"SUPPLY","hero",["hero"],Device:"paid-fixture")).Legal);
        });
        foreach(string id in new[]{"MOD-D1","MOD-D2","MOD-K1","MOD-K2","MOD-L1","MOD-L2","MOD-E1","MOD-E2"})Add("CX-052",id+" equipped stat effect and exact declared resource cost",()=>
        {
            var defs=CombatCases.Definitions().Items;var m=defs.Modules[id];var d=ProgressionRules.Derive(1,0,new(),defs.Items["WP-01"],defs.Items["AR-01"],[],[m]);Require(id.StartsWith("MOD-D")?d.DamageBonusBp==m.Value:id.StartsWith("MOD-K")?d.FlatPressure==m.Value:id.StartsWith("MOD-L")?d.ArmorBp==800+m.Value:d.MaxFocus==6+m.Value);Require(id.EndsWith('1')?m.Marks==80&&m.Parts==2&&m.ConsumesModule is null:m.Marks==160&&m.Parts==3&&m.ConsumesModule is not null);
        });
    }
}
