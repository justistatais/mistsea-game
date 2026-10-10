using Mist.Core.Combat;
using Mist.Core.Contracts;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Combat;

public static class VariantAndIntentCases
{
    private static BattleUnit U(BattleSnapshot s,string id)=>s.Units.Single(u=>u.Id==id);
    private static BattleSnapshot Fixture(string companion="CP-LINA",int level=30,int choice=0,string side="A")
    {
        var defs=CombatCases.Definitions();var s=defs.Create("BENCH-WORKSHOP",3,0,defs.ReferenceBuild("BUILD-Q102-ENTRY-H"),"WP-01","AR-01",[],[],Guid.NewGuid(),Guid.NewGuid(),helperIds:[companion,"AUX-GUARD"]);
        foreach(var u in s.Units){u.MaxHp=1000;u.Hp=1000;u.MaxFocus=14;u.Focus=14;u.AttackUnits=1000;u.TechUnits=1000;u.ArmorBp=0;u.Strength=5;u.Learned=[];u.Relics=[];u.Weapon="WP-01";u.Guards=[];u.Level=level;}
        U(s,"hero").Initiative=30;U(s,companion).Initiative=20;U(s,"guard").Initiative=10;var enemy=U(s,"maintenance");enemy.MaxHp=10000;enemy.Hp=10000;enemy.PressureThreshold=100;enemy.Pattern=[new("finite idle fixture",0,TargetLock.Unit,false,true)];
        if(choice>0)U(s,companion).Variants[choice]=side;return s;
    }
    private static BattleEngine Start(BattleSnapshot s,string companion){var e=new BattleEngine(CombatCases.Definitions(),s);e.Start();Act(e,"END");Require(e.Snapshot().CurrentActor==companion);return e;}
    private static void Act(BattleEngine e,string action,string[]? targets=null,int? slot=null,string? ward=null)=>e.Apply(new(new(Guid.NewGuid()),action,e.Snapshot().CurrentActor!,targets??[],slot,PressureTarget:targets?.FirstOrDefault(),WardTarget:ward));
    private static void NextRound(BattleEngine e){int before=e.Snapshot().Round;for(int n=0;n<8&&e.Snapshot().Round==before;n++)Act(e,"END");Require(e.Snapshot().Round==before+1);}
    public static void Register()
    {
        foreach(var pair in new Dictionary<string,int>{{"CP-LINA",85},{"CP-OREN",85},{"CP-MIRA",90},{"CP-TESS",95}})Add("CX-047",pair.Key+" level12 A changes actual attack coefficient, preserving two AP budget",()=>
        {
            var e=Start(Fixture(pair.Key,choice:12),pair.Key);Act(e,pair.Key+"-1",["maintenance"]);Require(U(e.Snapshot(),"maintenance").Hp==10000-pair.Value&&U(e.Snapshot(),pair.Key).Ap==0);
        });
        foreach(var pair in new Dictionary<string,int>{{"CP-LINA",45},{"CP-OREN",45},{"CP-MIRA",65},{"CP-TESS",40}})Add("CX-047",pair.Key+" level12 B changes actual shield or paid trap coefficient",()=>
        {
            var s=Fixture(pair.Key,choice:12,side:"B");U(s,"maintenance").Pattern=[new("finite attack fixture",0,TargetLock.Unit,true,true)];var e=Start(s,pair.Key);Act(e,pair.Key+"-2",[pair.Key=="CP-OREN"?"maintenance":"hero"]);
            if(pair.Key=="CP-OREN"){Require(e.Snapshot().Traps.Single().PaidFocus==2);NextRound(e);Require(U(e.Snapshot(),"maintenance").Hp==9955);}else Require(U(e.Snapshot(),"hero").Shield==pair.Value);
        });
        foreach(string id in new[]{"CP-LINA","CP-OREN","CP-MIRA","CP-TESS"})Add("CX-047",id+" level20 A applies the declared independent guard, pressure or shield effect",()=>
        {
            var s=Fixture(id,choice:20);if(id=="CP-TESS")U(s,"maintenance").Exposed=true;var e=Start(s,id);Act(e,id+"-1",["maintenance"]);var after=e.Snapshot();
            Require(id=="CP-LINA"?U(after,id).Guards.Single().Amount==1000:id=="CP-MIRA"?U(after,id).Shield==15:U(after,"maintenance").Pressure==2);Require(U(after,id).Ap==0&&U(after,"hero").Ap==0);
        });
        foreach(string id in new[]{"CP-LINA","CP-OREN","CP-MIRA","CP-TESS"})Add("CX-047",id+" level20 B cannot mint an additional turn or perform unselected support",()=>
        {
            var s=Fixture(id,choice:20,side:"B");U(s,"hero").Dots=[new("ordinary1",0,2),new("ordinary2",0,2)];U(s,"hero").RestraintExpiry=id=="CP-TESS"?new(id,3):null;U(s,"maintenance").Pattern=[new("finite attack fixture",0,TargetLock.Unit,true,true)];var e=Start(s,id);
            Act(e,id+"-2",[id=="CP-OREN"?"maintenance":"hero"],id=="CP-LINA"?2:null,ward:id=="CP-OREN"?"hero":null);var after=e.Snapshot();
            if(id=="CP-LINA")Require(U(after,"hero").Slot==2&&U(after,"hero").TurnUsed.Contains("move"));if(id=="CP-MIRA")Require(U(after,"hero").Dots.Count==0);if(id=="CP-TESS")Require(U(after,"hero").RestraintExpiry is null);
            Require(U(after,"hero").Ap==0&&U(after,id).Ap==(id=="CP-OREN"?1:0));if(id=="CP-OREN"){NextRound(e);Require(U(e.Snapshot(),"hero").Shield==20&&e.Snapshot().Events.Count(v=>v.Kind=="device_damage")==1);}
        });
        foreach(string id in new[]{"CP-LINA","CP-OREN","CP-MIRA","CP-TESS"})Add("CX-047",id+" level24 A changes cooldown or pressure without same-slot repeat",()=>
        {
            var e=Start(Fixture(id,choice:24),id);Act(e,id+"-1",["maintenance"]);var after=e.Snapshot();Require(U(after,id).Ready[id+"-1"]==2&&!e.Preview(new(new(Guid.NewGuid()),id+"-1",id,["maintenance"])).Legal);if(id=="CP-MIRA")Require(U(after,"maintenance").Pressure==1);
        });
        foreach(string id in new[]{"CP-LINA","CP-OREN","CP-MIRA","CP-TESS"})Add("CX-047",id+" level24 B retains finite cleanse, target, guard and device capacity",()=>
        {
            var s=Fixture(id,choice:24,side:"B");U(s,"hero").Dots=[new("ordinary1",0,2),new("ordinary2",0,2)];U(s,"guard").Dots=[new("ordinary",0,2)];
            if(id=="CP-OREN"){s.Traps=[new("trap:1",id,"maintenance",3500,1,2,1,"hero")];s.Sequence=1;U(s,"maintenance").Pattern=[new("finite attack fixture",0,TargetLock.Unit,true,true)];}
            if(id=="CP-MIRA")U(s,id).Variants[20]="B";var e=Start(s,id);Act(e,id+"-2",id=="CP-MIRA"?["hero","guard"]:[id=="CP-OREN"?"maintenance":"hero"]);var after=e.Snapshot();
            if(id=="CP-LINA")Require(U(after,"hero").Dots.Count==1);if(id=="CP-TESS")Require(U(after,"hero").Guards.Single().Amount==1500&&U(after,"hero").Shield==30);
            if(id=="CP-MIRA")Require(U(after,"hero").Shield==40&&U(after,"guard").Shield==40&&U(after,"hero").Dots.Count==1&&U(after,"guard").Dots.Count==0);
            if(id=="CP-OREN"){Require(after.Traps.Count==2);NextRound(e);Require(e.Snapshot().Traps.Count==1&&e.Snapshot().Events.Count(v=>v.Kind=="device_damage")==1);}
        });
        Add("CX-047","Choices below their level gate and simultaneous invalid choice are rejected before actions",()=>
        {
            Throws<InvalidDataException>(()=>new BattleEngine(CombatCases.Definitions(),Fixture(level:11,choice:12)));var s=Fixture();U(s,"CP-LINA").Variants[12]="AB";Throws<InvalidDataException>(()=>new BattleEngine(CombatCases.Definitions(),s));
        });
        Add("CX-046","Threat preview is pure and distinguishes original unit, column, shield and HP loss",()=>
        {
            var s=Fixture();U(s,"hero").ArmorBp=1000;U(s,"hero").Shield=20;U(s,"hero").ShieldExpiry=new("CP-LINA",2);U(s,"maintenance").Pattern=[new("announced attack",100,TargetLock.Unit,true,true)];var e=new BattleEngine(CombatCases.Definitions(),s);e.Start();string before=StrictJson.Serialize(e.Snapshot());var p=e.PreviewIntent("maintenance");Require(p.Losses.Single()==new ThreatLoss("hero",90,20,70)&&StrictJson.Serialize(e.Snapshot())==before);Act(e,"MOVE",slot:2);Require(e.PreviewIntent("maintenance").Losses.Single().Target=="hero");
            s=Fixture();U(s,"maintenance").Pattern=[new("declared column",100,TargetLock.Column,true,true)];e=new(CombatCases.Definitions(),s);e.Start();Act(e,"MOVE",slot:1);Require(e.PreviewIntent("maintenance").Losses.All(t=>t.Target!="hero"));
        });
        Add("CX-046","A movement shield without a legal move still shields, with no extra action",()=>
        {
            var s=Fixture("CP-TESS");var e=Start(s,"CP-TESS");Act(e,"CP-TESS-2",["hero"],slot:3);Require(U(e.Snapshot(),"hero").Slot==0&&U(e.Snapshot(),"hero").Shield==30&&U(e.Snapshot(),"CP-TESS").Ap==0&&U(e.Snapshot(),"hero").Ap==0);
        });
        Add("CX-046","Reinforcement is declared in advance, acts only next round and never repeats its source call",()=>
        {
            var s=ReinforcementFixture();var e=new BattleEngine(CombatCases.Definitions(),s);e.Start();Require(e.PreviewIntent("maintenance").Reinforcement!.Contains("下一轮"));NextRound(e);var a=e.Snapshot();Require(a.Units.Count(u=>u.Side==CombatSide.Enemy)==2&&U(a,"reinforcement").PersonalTurn==0&&a.Events.All(v=>v.Source!="reinforcement"||v.Kind!="enemy_action"));
            var loaded=new BattleEngine(CombatCases.Definitions(),StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(a)));NextRound(e);NextRound(loaded);Require(StrictJson.Serialize(e.Snapshot())==StrictJson.Serialize(loaded.Snapshot()));Require(e.Snapshot().Events.Count(v=>v.Kind=="reinforcement_booked")==1&&e.Snapshot().Events.Any(v=>v.Kind=="enemy_action"&&v.Source=="reinforcement"&&v.Round==2));
        });
        Add("CX-046","Occupied reinforcement slot defers once then falls back, without replacing any of four enemies",()=>
        {
            var s=ReinforcementFixture();for(int n=1;n<4;n++){var enemy=StrictJson.Deserialize<BattleUnit>(StrictJson.Serialize(U(s,"maintenance")));enemy.Id="occupied"+n;enemy.Slot=n;enemy.Pattern=[new("idle",0,TargetLock.Unit,false,true)];s.Units.Add(enemy);}var e=new BattleEngine(CombatCases.Definitions(),s);e.Start();NextRound(e);Require(e.Snapshot().PendingReinforcements.Single().Deferrals==1);NextRound(e);Require(e.Snapshot().PendingReinforcements.Count==0&&e.Snapshot().Units.Count(u=>u.Side==CombatSide.Enemy)==4&&e.Snapshot().Units.All(u=>u.Id!="reinforcement")&&e.Snapshot().Events.Any(v=>v.Kind=="reinforcement_fallback"));
        });
        Add("CX-046","Unknown or recursive reinforcement templates fail validation and ordinary stagger cancels the call",()=>
        {
            var s=ReinforcementFixture();s.ReinforcementTemplates.Clear();Throws<InvalidDataException>(()=>new BattleEngine(CombatCases.Definitions(),s));s=ReinforcementFixture();s.ReinforcementTemplates["finite"].Pattern=[new("recursive",0,TargetLock.Unit,false,true,"finite")];Throws<InvalidDataException>(()=>new BattleEngine(CombatCases.Definitions(),s));s=ReinforcementFixture();U(s,"maintenance").PendingBreak=true;U(s,"maintenance").RecoveryLocked=true;var e=new BattleEngine(CombatCases.Definitions(),s);e.Start();NextRound(e);Require(e.Snapshot().Units.All(u=>u.Id!="reinforcement")&&e.Snapshot().PendingReinforcements.Count==0);
        });
    }
    private static BattleSnapshot ReinforcementFixture()
    {
        var s=Fixture();U(s,"maintenance").Pattern=[new("one finite reinforcement",0,TargetLock.Unit,false,true,"finite")];var r=StrictJson.Deserialize<BattleUnit>(StrictJson.Serialize(U(s,"maintenance")));r.Id="reinforcement";r.Name="增援测试单位";r.Slot=1;r.Pattern=[new("declared next-round attack",10,TargetLock.Unit,true,true)];s.ReinforcementTemplates["finite"]=r;return s;
    }
}
