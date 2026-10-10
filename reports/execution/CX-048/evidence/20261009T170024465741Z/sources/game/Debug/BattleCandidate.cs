using Mist.Core.Combat;
using Mist.Core.Contracts;

namespace Mist.DebugCandidates;

// A repeatable engineering player: it chooses legal commands and records actual resolution.
// It is not a human usability or language assessment.
public static class BattleCandidate
{
    public static BattleRequest Choose(BattleEngine engine,bool rightValveFirst=false)
    {
        var s=engine.Snapshot();string actor=s.CurrentActor??throw new InvalidOperationException("No decision slot.");var u=s.Units.Single(a=>a.Id==actor);
        BattleRequest R(string action,string[]? targets=null,int? destination=null,string? device=null)=>new(new(Guid.NewGuid()),action,actor,targets??[],destination,device);
        bool Legal(BattleRequest request)=>engine.Preview(request).Legal;
        if(s.Mechanism=="tower")
        {
            foreach(string control in new[]{"left","right","isolate"})
            {
                if(rightValveFirst&&control=="left"&&!s.Controls["right"])continue;
                var request=R("CONTROL",device:control);if(Legal(request))return request;
            }
        }
        foreach(var down in s.Units.Where(a=>a.Side==CombatSide.Ally&&!a.Alive)) { var request=R("REVIVE",[down.Id]);if(Legal(request))return request; }
        foreach(var injured in s.Units.Where(a=>a.Side==CombatSide.Ally&&a.Alive&&a.Hp*100<a.MaxHp*40).OrderBy(a=>a.Hp)) { var request=R("AID",[injured.Id]);if(Legal(request))return request; }
        var enemy=s.Units.Where(a=>a.Side==CombatSide.Enemy&&a.Alive).OrderBy(a=>a.Slot).FirstOrDefault();
        if(enemy is not null)
        {
            foreach(string action in new[]{"H02","M02","H01","M01","S02","AUX-GUARD-1","AUX-SUPPORT-1","CP-LINA-1","CP-OREN-1","CP-MIRA-1","CP-TESS-1","BASIC"})
            {
                var request=R(action,[action=="H02"?actor:enemy.Id]);if(Legal(request))return request;
            }
        }
        var guard=R("GUARD");if(Legal(guard))return guard;return R("END");
    }
    public static BattleSnapshot Complete(BattleEngine engine,bool rightValveFirst=false)
    {
        for(int count=0;count<600&&engine.Snapshot().Outcome==CombatOutcome.Active;count++)engine.Apply(Choose(engine,rightValveFirst));
        var actual=engine.Snapshot();if(actual.Outcome!=CombatOutcome.Controlled)throw new InvalidOperationException($"Candidate ended {actual.Outcome}, round {actual.Round}: "+StrictJson.Serialize(actual));return actual;
    }
}
