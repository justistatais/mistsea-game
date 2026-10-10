#if DEBUG
using Mist.Core.Combat;
using Mist.Core.Contracts;

namespace Mist.DebugCandidates;

// An explicit engineering controller. Every offered action is validated by the shared engine.
// It neither changes battle parameters nor adds skills, items, resources or consent.
public static class FinaleBattleCandidate
{
    public const string StrategyVersion = "rf-controller-2";
    public static BattleRequest Choose(BattleDefinitions definitions,BattleEngine engine)
    {
        var s=engine.Snapshot();string actor=s.CurrentActor??throw new InvalidOperationException("No actual decision slot.");var u=s.Units.Single(x=>x.Id==actor);
        BattleRequest R(string action,string[]? targets=null,int? destination=null,string? device=null)=>new(new(Guid.NewGuid()),action,actor,targets??[],destination,device,targets?.FirstOrDefault());
        bool Legal(BattleRequest request)=>engine.Preview(request).Legal;
        foreach(string device in new[]{"command_left","command_right","isolate_left","isolate_right","verify_link","latch_bypass","verify_bypass","set_damping","verify_damping","arm_controlled_stop"}){var request=R("CONTROL",device:device);if(Legal(request))return request;}
        var allies=s.Units.Where(x=>x.Side==CombatSide.Ally&&x.Alive).ToArray();var enemies=s.Units.Where(x=>x.Side==CombatSide.Enemy&&x.Alive).OrderBy(x=>x.Id==s.Finale?.Subject?1:0).ThenBy(x=>x.Slot).ToArray();
        int Threat(BattleUnit target)=>s.Intents.Sum(i=>engine.PreviewIntent(i.Source).Losses.Where(l=>l.Target==target.Id).Sum(l=>l.ResolvedDamage));
        foreach(var down in s.Units.Where(x=>x.Side==CombatSide.Ally&&!x.Alive)){var revive=R("REVIVE",[down.Id]);if(Legal(revive))return revive;}
        foreach(var wounded in allies.OrderBy(x=>x.Hp).Where(x=>x.Hp*100<x.MaxHp*40||x.Hp+x.Shield<=Threat(x)))
        {
            foreach(var supply in s.Supplies.Where(x=>!x.Used&&x.Definition=="CON-01")){var heal=R("SUPPLY",[wounded.Id],device:supply.Instance);if(Legal(heal))return heal;}
            var aid=R("AID",[wounded.Id]);if(Legal(aid))return aid;
        }
        // Evaluate one legal action against the currently public intentions. Damage,
        // actual interruption, and protection compete for the same finite AP; wounded
        // recipients do not automatically suppress all the team's attacks every round.
        int HpThreat(BattleEngine preview,BattleSnapshot state,string id)
        {
            var target=state.Units.Single(x=>x.Id==id);
            // The same shield pool cannot absorb each declared source afresh.
            int announced=state.Intents.Sum(i=>preview.PreviewIntent(i.Source).Losses.Where(l=>l.Target==id).Sum(l=>l.ResolvedDamage));
            return Math.Min(target.Hp,Math.Max(0,announced-target.Shield));
        }
        var threatened=allies.ToDictionary(x=>x.Id,x=>HpThreat(engine,s,x.Id));
        var choices=new List<(BattleRequest Request,double Value)>();
        void Consider(BattleRequest request)
        {
            var preview=engine.Preview(request);if(!preview.Legal||preview.MechanismLocked)return;
            var probe=new BattleEngine(definitions,s);probe.Apply(request);var after=probe.Snapshot();
            double progress=enemies.Sum(t=>Math.Max(0,t.Hp-after.Units.Single(x=>x.Id==t.Id).Hp)+(after.Units.Single(x=>x.Id==t.Id).Alive?0:45)+(after.Units.Single(x=>x.Id==t.Id).Exposed&&!t.Exposed?15:0));
            double prevention=0;
            foreach(var ally in allies)
            {
                var current=after.Units.Single(x=>x.Id==ally.Id);int loss=HpThreat(probe,after,ally.Id);
                prevention+=Math.Max(0,threatened[ally.Id]-loss)*.30;
                // Imminent loss of a seat is more serious than low but nonlethal HP.
                if(threatened[ally.Id]>=ally.Hp&&current.Hp>loss)prevention+=110;
            }
            double future=0;
            if(definitions.Actions.TryGetValue(request.Action,out var d)&&d.Kind is ActionKind.Empower or ActionKind.Stance)
            {
                foreach(string id in request.Targets)
                {
                    var target=s.Units.Single(x=>x.Id==id);var current=after.Units.Single(x=>x.Id==id);if(current.Empowered<=target.Empowered)continue;
                    // A forecast of the publicly documented basic strike, not an injected
                    // future AP/Focus budget or a promise that another actor will use it.
                    int co=definitions.Items.Items[target.Weapon].BasicCoefficientBp;if(target.Id!="hero")co=co*6500/10000;
                    future+=enemies.Max(e=>Math.Max(0,CombatMath.Damage(target.AttackUnits,co,target.DamageBonus+current.Empowered,0,e.ArmorBp,0)-CombatMath.Damage(target.AttackUnits,co,target.DamageBonus+target.Empowered,0,e.ArmorBp,0)))*.75;
                }
            }
            if(definitions.Actions.TryGetValue(request.Action,out d)&&d.Kind==ActionKind.Trap&&request.Targets.Length==1)
            {
                var target=s.Units.Single(x=>x.Id==request.Targets[0]);
                if(s.Intents.Any(i=>i.Source==target.Id&&i.Attack)&&!s.Traps.Any(t=>t.Target==target.Id))
                    future+=CombatMath.Damage(u.TechUnits,d.Coefficient,u.DamageBonus+(u.Learned.Contains("M11")?2000:0),target.Exposed?u.ExposureBonus:0,target.ArmorBp,0)*.75;
            }
            double value=(progress+prevention+future)/Math.Max(1,preview.Ap)-preview.Focus*1.5;
            if(value>0)choices.Add((request,value));
        }
        foreach(string skill in u.Active.Append("BASIC").Distinct())
        {
            if(skill!="BASIC"&&(!definitions.Actions.TryGetValue(skill,out var spec)||spec.Kind!=ActionKind.Damage))continue;
            int maximum=skill=="BASIC"?1:definitions.Actions[skill].MaximumTargets;
            for(int mask=1;mask<(1<<enemies.Length);mask++)
            {
                var targets=enemies.Where((_,i)=>(mask&(1<<i))!=0).ToArray();if(targets.Length>maximum)continue;Consider(R(skill,targets.Select(x=>x.Id).ToArray()));
            }
        }
        foreach(string skill in u.Active)
        {
            if(!definitions.Actions.TryGetValue(skill,out var d)||d.Kind==ActionKind.Damage)continue;
            if(d.Kind==ActionKind.Trap)
            {foreach(var enemy in enemies)if(s.Intents.Any(i=>i.Source==enemy.Id&&i.Attack)&&!s.Traps.Any(t=>t.Target==enemy.Id))Consider(R(skill,[enemy.Id]));continue;}
            if(d.Kind==ActionKind.Stance){Consider(R(skill,[actor]));continue;}
            for(int mask=1;mask<(1<<allies.Length);mask++)
            {var targets=allies.Where((_,i)=>(mask&(1<<i))!=0).Select(x=>x.Id).ToArray();if(targets.Length<=d.MaximumTargets)Consider(R(skill,targets));}
        }
        Consider(R("GUARD"));
        if(choices.Count>0)return choices.MaxBy(x=>x.Value).Request;
        return R("END");
    }
}
#endif
