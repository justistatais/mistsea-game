using Mist.Core.Combat;
using Mist.Core.Contracts;

namespace Mist.Core.Finale;

// Receives the actual party/equipment-derived fresh snapshot from BattleDefinitions.
// It replaces benchmark enemies BEFORE entry, not after an action. It owns no clock.
public static class FinaleEncounterFactory
{
    public const string OriginalNumericVersion = "rf-balance-1";
    public const string PreviousNumericVersion = "rf-balance-2";
    public const string NumericVersion = "rf-balance-3";
    public const string NumericStatus = "original_unreviewed_human_pending";
    public static BattleSnapshot Attach(BattleSnapshot party, string encounter, int enemyHpBp, string numericVersion = NumericVersion)
    {
        if (numericVersion is not (OriginalNumericVersion or PreviousNumericVersion or NumericVersion)) throw new InvalidOperationException("Unregistered finale numeric candidate.");
        bool revised = numericVersion != OriginalNumericVersion;
        if (party.Round != 0 || party.Outcome != CombatOutcome.Active || party.Instance == Guid.Empty || party.Attempt == Guid.Empty || enemyHpBp < 1000 || enemyHpBp > 20000 || party.Units.Count(u=>u.Side==CombatSide.Ally)!=3) throw new InvalidOperationException("Finale factory needs a fresh, actual party encounter.");
        var s = StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(party));
        s.Definition = encounter;s.Units.RemoveAll(u=>u.Side==CombatSide.Enemy);s.Intents.Clear();s.Controls.Clear();s.ReinforcementTemplates.Clear();s.PendingReinforcements.Clear();s.Finale = null;s.Mechanism = null;
        BattleUnit Enemy(string id,string name,int slot,int hp,int armor,int threshold,bool boss,params EnemyPattern[] pattern)
        {int scaled=Math.Max(1,hp*enemyHpBp/10000);return new(){Id=id,Name=name,Side=CombatSide.Enemy,Slot=slot,MaxHp=scaled,Hp=scaled,ArmorBp=armor,PressureThreshold=threshold,Boss=boss,Initiative=boss?12:10,Pattern=pattern};}
        switch (encounter)
        {
            case "ENC-RF-01":
                s.Units.Add(Enemy("rf-coil","竖井线圈哨卫",0,460,1000,6,false,new("线圈点射",100,TargetLock.Unit,true,true),new("公开充能",0,TargetLock.Unit,false,true),new("竖井列扫",60,TargetLock.Column,true,true)));break;
            case "ENC-RF-02":
                s.Mechanism=FinaleBattleRules.RookMechanism;s.Finale=FinaleBattleRules.CreateRook();
                s.Units.Add(Enemy("rf-rook","鲁克的作战抵抗",0,revised?1000:1200,2000,8,true,new("鲁克：点名压击",180,TargetLock.Unit,true,true),new("鲁克：列压制",100,TargetLock.Column,true,true),new("鲁克：公开参与者合击",200,TargetLock.All,true,true)));
                s.Units.Add(Enemy("rf-rook-guard-left","左护卫",1,revised?180:220,500,4,false,new("左护卫公开行动",60,TargetLock.Unit,true,true),new("左护卫守位",0,TargetLock.Unit,false,true)));
                s.Units.Add(Enemy("rf-rook-guard-right","右护卫",2,revised?180:220,500,4,false,new("右护卫公开行动",60,TargetLock.Unit,true,true),new("右护卫守位",0,TargetLock.Unit,false,true)));break;
            case "ENC-RF-03":
                s.Units.Add(Enemy("rf-projector","继电层护场投射器",0,580,1200,6,false,new("护场冲击",110,TargetLock.Unit,true,true),new("投射器检视",0,TargetLock.Unit,false,true),new("继电列脉冲",65,TargetLock.Column,true,true)));break;
            case "ENC-RF-04":
                s.Mechanism=FinaleBattleRules.CoreMechanism;s.Finale=FinaleBattleRules.CreateCore();
                s.Units.Add(Enemy("rf-central","镇潮机组失控抵抗",0,numericVersion==NumericVersion?1400:revised?1500:1900,1800,8,true,new("机组压击",150,TargetLock.Unit,true,true),new("机组场脉冲",85,TargetLock.All,true,true),new("机组维护准备",0,TargetLock.Unit,false,true)));
                s.ReinforcementTemplates.Add("rf-relay-left",Enemy("rf-relay-left","左续启组件",1,revised?180:240,500,4,false,new EnemyPattern("左组件公开馈送",55,TargetLock.Unit,true,true)));
                s.ReinforcementTemplates.Add("rf-relay-right",Enemy("rf-relay-right","右续启组件",2,revised?180:240,500,4,false,new EnemyPattern("右组件公开馈送",55,TargetLock.Unit,true,true)));break;
            default:throw new InvalidOperationException("Unregistered finale encounter.");
        }
        FinaleBattleRules.Validate(s,s.Finale);return s;
    }
}
