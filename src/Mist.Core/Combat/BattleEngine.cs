using Mist.Core.Contracts;
using Mist.Core.Inventory;
using Mist.Core.Finale;

namespace Mist.Core.Combat;

public sealed record ActionPreviewView(bool Legal,string? Reason,int Ap,int Focus,int PredictedDamage,bool MechanismLocked);
public sealed record ThreatLoss(string Target,int ResolvedDamage,int ShieldAbsorbed,int HpLoss);
public sealed record IntentPreviewView(string Source,bool Cancelled,string? Reason,ThreatLoss[] Losses,string? Reinforcement);
public sealed class BattleEngine
{
    private readonly BattleDefinitions _definitions;
    private readonly BattleSnapshot _s;
    public BattleEngine(BattleDefinitions definitions,BattleSnapshot snapshot)
    {
        _definitions=definitions;_s=Clone(snapshot);ValidateState();
    }
    public BattleSnapshot Snapshot()=>Clone(_s);
    private static BattleSnapshot Clone(BattleSnapshot state)=>StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(state));
    private BattleUnit Unit(string id)=>_s.Units.SingleOrDefault(u=>u.Id==id)??throw new InvalidOperationException("作战主体不存在。");
    private IEnumerable<BattleUnit> Allies(bool living=true)=>_s.Units.Where(u=>u.Side==CombatSide.Ally&&(!living||u.Alive));
    private IEnumerable<BattleUnit> Enemies(bool living=true)=>_s.Units.Where(u=>u.Side==CombatSide.Enemy&&(!living||u.Alive));
    private void ValidateState()
    {
        if(_s.RulesVersion!="mist-rules-1"||_s.Instance==Guid.Empty||_s.Attempt==Guid.Empty||_s.Units.Count==0||_s.Units.Any(u=>string.IsNullOrEmpty(u.Id))||_s.Units.Select(u=>u.Id).Distinct().Count()!=_s.Units.Count||_s.Units.GroupBy(u=>(u.Side,u.Slot)).Any(g=>g.Count()>1)||Allies(false).Count()>3||Enemies(false).Count()>4||_s.Aid is <0 or >2||_s.Supplies.Count>2||_s.Supplies.Select(i=>i.Instance).Distinct().Count()!=_s.Supplies.Count||_s.Supplies.Any(i=>i.Definition is not("CON-01" or "CON-02" or "CON-03" or "CON-04")))throw new InvalidDataException("战斗快照的身份或容量无效。");
        foreach(var u in _s.Units)if(u.Slot is <0 or >3||u.MaxHp<1||u.Hp<0||u.Hp>u.MaxHp||u.Focus<0||u.Focus>u.MaxFocus||u.Ap<0||u.Ap>(u.Id=="hero"?3:2)||u.Shield<0||u.Shield>u.MaxHp/2||u.PersonalTurn<0||u.Side==CombatSide.Enemy&&u.Pattern.Length==0||u.Ready.Values.Any(v=>v<0))throw new InvalidDataException("战斗快照数值无效。");
        foreach(var u in _s.Units)if(u.Variants.Any(v=>v.Key is not(12 or 20 or 24)||u.Level<v.Key||v.Value is not("A" or "B"))||u.Variants.Count>0&&!u.Id.StartsWith("CP-",StringComparison.Ordinal))throw new InvalidDataException("同伴变体的等级或选择无效。");
        if(_s.Queue.Distinct().Count()!=_s.Queue.Length||_s.Queue.Any(id=>!_s.Units.Any(u=>u.Id==id))||_s.CurrentActor is { } current&&!_s.Units.Any(u=>u.Id==current))throw new InvalidDataException("战斗队列无效。");
        if(_s.Round<0||_s.Sequence<0||_s.ReinforcementTemplates.Count>4||_s.PendingReinforcements.Count>4||_s.PendingReinforcements.Select(p=>p.Template).Distinct().Count()!=_s.PendingReinforcements.Count)throw new InvalidDataException("增援数量或战斗时钟无效。");
        foreach(var u in _s.Units)if(u.AttackUnits<0||u.TechUnits<0||u.ArmorBp is <0 or >4500||u.GuardBase is <0 or >3500||u.ExposureBonus is <0 or >4000||u.Pressure<0||u.PatternIndex<0||u.Dots.Any(d=>d.Raw<0||d.RemainingSlots<1)||u.Pattern.Any(p=>p.Damage<0||p.Reinforcement is not null&&!_s.ReinforcementTemplates.ContainsKey(p.Reinforcement)))throw new InvalidDataException("效果、意图或伤害边界无效。");
        foreach(var pair in _s.ReinforcementTemplates){var u=pair.Value;if(u.Side!=CombatSide.Enemy||u.Slot is <0 or >3||u.MaxHp<1||u.Hp!=u.MaxHp||u.PersonalTurn!=0||u.Pattern.Length==0||u.Pattern.Any(p=>p.Damage<0||p.Reinforcement is not null)||string.IsNullOrWhiteSpace(u.Id))throw new InvalidDataException("增援必须使用有限、无递归召唤的完整敌方模板。");}
        foreach(var p in _s.PendingReinforcements)if(!_s.ReinforcementTemplates.ContainsKey(p.Template)||!_s.Units.Any(u=>u.Id==p.Source&&u.Side==CombatSide.Enemy)||p.EligibleRound<1||p.Deferrals is <0 or >1)throw new InvalidDataException("待加入单位的来源或阶段无效。");
        FinaleBattleRules.Validate(_s,_s.Finale);
    }
    private void Log(string kind,string source,string? target=null,int value=0,string detail="")=>_s.Events.Add(new(_s.Round,kind,source,target,value,detail));
    private static bool Has(BattleUnit u,string skill)=>u.Learned.Contains(skill,StringComparer.Ordinal);
    private static bool Relic(BattleUnit u,string id)=>u.Relics.Contains(id,StringComparer.Ordinal);
    private static bool Take(List<string> tokens,string token){if(tokens.Contains(token,StringComparer.Ordinal))return false;tokens.Add(token);return true;}
    private BoundExpiry Future(BattleUnit source,int slots,bool end=false)=>new(source.Id,checked(source.PersonalTurn+slots),end);
    private bool Due(BoundExpiry? expiry,string? ending=null)
    {
        if(expiry is null)return false;var source=Unit(expiry.Source);return source.PersonalTurn>expiry.PersonalTurn||source.PersonalTurn==expiry.PersonalTurn&&(!expiry.AtEnd||ending==source.Id);
    }
    private (int Round,int Order,bool End) Forecast(BoundExpiry expiry)
    {
        var source=Unit(expiry.Source);var order=_s.Units.OrderBy(u=>u.Side).ThenByDescending(u=>u.Initiative).ThenBy(u=>u.Id,StringComparer.Ordinal).Select(u=>u.Id).ToArray();
        return (_s.Round+expiry.PersonalTurn-source.PersonalTurn,Array.IndexOf(order,expiry.Source),expiry.AtEnd);
    }
    private void Expire(string? ending=null)
    {
        foreach(var u in _s.Units)
        {
            if(Due(u.ShieldExpiry,ending)){u.Shield=0;u.ShieldExpiry=null;}
            u.Guards.RemoveAll(g=>Due(g.Expiry,ending));if(Due(u.NextGuardExpiry,ending)){u.NextGuard=0;u.NextGuardExpiry=null;}
            if(Due(u.EmpowerExpiry,ending)){u.Empowered=0;u.EmpowerExpiry=null;}if(Due(u.WeaknessExpiry,ending)){u.OutgoingWeakness=0;u.WeaknessExpiry=null;}
            if(Due(u.ShieldBonusExpiry,ending)){u.NextShieldBonus=0;u.ShieldBonusExpiry=null;}if(Due(u.RestraintExpiry,ending))u.RestraintExpiry=null;
        }
    }
    public void Start()
    {
        if(_s.Round!=0||_s.Outcome!=CombatOutcome.Active)throw new InvalidOperationException("不能将继续存档当作新遭遇回满。");
        BeginRound();Advance();
    }
    private void BeginRound()
    {
        _s.Round++;JoinReinforcements();_s.Queue=_s.Units.Where(u=>u.JoinRound<=_s.Round).OrderBy(u=>u.Side).ThenByDescending(u=>u.Initiative).ThenBy(u=>u.Id,StringComparer.Ordinal).Select(u=>u.Id).ToArray();_s.Cursor=0;
        _s.Intents.Clear();var targets=Allies().OrderBy(u=>u.Slot).ThenBy(u=>u.Id,StringComparer.Ordinal).ToArray();
        foreach(var e in Enemies().Where(e=>e.JoinRound<=_s.Round))
        {
            var p=e.Pattern[e.PatternIndex%e.Pattern.Length];string[] people=p.Lock==TargetLock.Unit&&targets.Length>0?[targets[0].Id]:[];int[] slots=p.Lock==TargetLock.All?[0,1,2,3]:p.Lock==TargetLock.Column&&targets.Length>0?[targets[0].Slot%2,targets[0].Slot%2+2]:[];
            _s.Intents.Add(new(e.Id,p.Name,p.Damage,p.Lock,people,slots,p.Attack,p.Interruptible,p.Reinforcement));
        }
        if(_s.Finale is {} finale)FinaleBattleRules.DeclareRookCombination(_s,finale);
        Log("intents_declared","system",detail:StrictJson.Serialize(_s.Intents));
    }
    private void JoinReinforcements()
    {
        foreach(var pending in _s.PendingReinforcements.Where(p=>p.EligibleRound<=_s.Round).ToArray())
        {
            var template=_s.ReinforcementTemplates[pending.Template];bool full=Enemies(false).Count()>=4||_s.Units.Any(u=>u.Side==CombatSide.Enemy&&u.Slot==template.Slot)||_s.Units.Any(u=>u.Id==template.Id);
            _s.PendingReinforcements.Remove(pending);
            if(full)
            {
                if(pending.Deferrals==0){_s.PendingReinforcements.Add(pending with{EligibleRound=_s.Round+1,Deferrals=1});Log("reinforcement_delayed",pending.Source,detail:"No free declared slot; one retry, then the declared no-arrival fallback.");}
                else Log("reinforcement_fallback",pending.Source,detail:"Declared no-arrival fallback; no existing entity was replaced.");
                continue;
            }
            var unit=StrictJson.Deserialize<BattleUnit>(StrictJson.Serialize(template));unit.JoinRound=_s.Round;unit.EligibleRound=_s.Round;_s.Units.Add(unit);Log("reinforcement_joined",pending.Source,unit.Id,detail:"Intention is declared before this round's ally decisions.");
        }
    }
    private bool BeginSlot(BattleUnit u)
    {
        u.PersonalTurn++;u.TurnUsed.Clear();u.Ap=0;Expire();
        foreach(var dot in u.Dots.ToArray())
        {
            if(u.Alive)Hurt(u,u,dot.Raw*10,10000,0,0,u.Armor=="AR-05"?1000:0,false,false);
            u.Dots.Remove(dot);if(dot.RemainingSlots>1&&u.Alive)u.Dots.Add(dot with{RemainingSlots=dot.RemainingSlots-1});
        }
        if(!u.Alive||u.EligibleRound>_s.Round)return false;
        if(u.Side==CombatSide.Ally){u.Focus=Math.Min(u.MaxFocus,u.Focus+1);u.Ap=u.Id=="hero"?3:2;}_s.CurrentActor=u.Id;return true;
    }
    private void EndSlot(BattleUnit u){u.Ap=0;Expire(u.Id);_s.CurrentActor=null;}
    private void Advance()
    {
        while(_s.Outcome==CombatOutcome.Active)
        {
            Terminal();if(_s.Outcome!=CombatOutcome.Active)return;
            if(_s.Cursor>=_s.Queue.Length)
            {
                foreach(var e in Enemies())if(!e.RecoveryLocked&&e.LastPressureRound!=_s.Round)e.Pressure=Math.Max(0,e.Pressure-1);
                foreach(var u in _s.Units)if(u.Exposed&&u.ExposureUntilRound<=_s.Round)u.Exposed=false;BeginRound();
            }
            var actor=Unit(_s.Queue[_s.Cursor]);bool living=BeginSlot(actor);AfterAtomicAction(actor.Id);Terminal();if(_s.Outcome!=CombatOutcome.Active)return;
            if(living&&actor.Side==CombatSide.Ally)return;
            if(living){EnemyAct(actor);AfterAtomicAction(actor.Id);}EndSlot(actor);_s.Cursor++;Terminal();
        }
    }
    private void Terminal()
    {
        if(!Allies().Any())_s.Outcome=CombatOutcome.Defeat;
        else if(_s.Finale is {} finale?FinaleBattleRules.CanEndControlled(_s,finale):!Enemies().Any()&&_s.PendingReinforcements.Count==0&&(_s.Mechanism!="tower"||_s.Controls.Values.All(v=>v)))_s.Outcome=CombatOutcome.Controlled;
        if(_s.Outcome!=CombatOutcome.Active)_s.CurrentActor=null;
    }
    private static bool Friendly(ActionKind kind)=>kind is ActionKind.Shield or ActionKind.CleanseShield or ActionKind.Stance or ActionKind.Empower or ActionKind.StepShield or ActionKind.StepAllyShield;
    private bool MeleeLegal(BattleUnit actor,BattleUnit target,string? power)=>power!="A"||actor.Scaling!=WeaponScaling.Melee||target.Slot<2||!Enemies().Any(u=>u.Slot==target.Slot-2);
    private bool Locked(BattleUnit target)=>_s.Finale is {} finale&&FinaleBattleRules.DamageLocked(_s,finale,target)||target.Side==CombatSide.Enemy&&target.Id=="tower"&&_s.Mechanism=="tower"&&!(_s.Controls["left"]&&_s.Controls["right"]);
    private void AfterAtomicAction(string actor)
    {
        if(_s.Finale is {} finale)FinaleBattleRules.AfterAtomicAction(_s,finale,actor);
    }
    private int Guard(BattleUnit unit,int extra=0)=>Math.Max(extra,Math.Max(unit.NextGuard,unit.Guards.Count==0?0:unit.Guards.Max(g=>g.Amount)));
    private (int Fee,string[] Tokens) Cost(BattleUnit u,CombatActionDefinition d)
    {
        int reduction=0;var tokens=new List<string>();bool tech=d.Power=="T"||d.Kind is ActionKind.Shield or ActionKind.CleanseShield or ActionKind.StepShield or ActionKind.StepAllyShield;
        if(d.Focus>0&&tech&&u.Weapon=="WP-05"&&!u.EncounterUsed.Contains("wp5")){reduction++;tokens.Add("wp5");}
        if(d.Focus>=3&&Has(u,"S12")&&!u.EncounterUsed.Contains("s12")){reduction++;tokens.Add("s12");}
        return(d.Focus==0?0:Math.Max(1,d.Focus-reduction),tokens.ToArray());
    }
    private string? Validate(BattleRequest request)
    {
        if(_s.Outcome!=CombatOutcome.Active||_s.CurrentActor!=request.Actor)return "当前不是此角色的计划槽。";
        var u=Unit(request.Actor);if(!u.Alive||u.EligibleRound>_s.Round||u.Side!=CombatSide.Ally)return "角色目前不能行动。";
        if(request.Action=="END")return request.Targets.Length==0?null:"结束回合不接受目标。";
        if(request.Action=="MOVE")return request.Destination is { } slot&&u.Ap>=1&&CanMove(u,slot)?null:"无合法相邻换位，或本槽预算已使用。";
        if(request.Action=="GUARD")return u.Ap>=1&&!u.TurnUsed.Contains("guard")?null:"本槽已防御或AP不足。";
        if(request.Action=="RECLAIM")return u.Ap>=1&&_s.Traps.Any(t=>t.Id==request.Device&&t.Owner==u.Id)?null:"没有本人未触发的装置可回收。";
        if(request.Action=="CONTROL")return u.Ap>=1&&CanOperate(u,request.Device)?null:"当前操作点或联锁条件不满足。";
        if(request.Targets.Length==0||request.Targets.Distinct(StringComparer.Ordinal).Count()!=request.Targets.Length||request.Targets.Any(id=>!_s.Units.Any(t=>t.Id==id)))return "目标缺失、重复或不在遭遇内。";
        var targets=request.Targets.Select(Unit).ToArray();
        if(request.Action is "AID" or "REVIVE" or "SUPPLY")
        {
            if(targets.Length!=1||targets[0].Side!=CombatSide.Ally||u.TurnUsed.Contains("supply"))return "每角色每槽最多一次补给；目标必须属于友方。";
            var t=targets[0];if(request.Action=="REVIVE")return !t.Alive&&u.Ap>=2&&_s.Aid>0?null:"救起需要倒地友方、2AP与共享配发。";
            if(!t.Alive||u.Ap<1)return "补给不能用于倒地目标或AP不足。";
            if(request.Action=="AID")return _s.Aid>0&&t.Hp<t.MaxHp?null:"没有配发或目标无需治疗。";
            var supply=_s.Supplies.SingleOrDefault(i=>i.Instance==request.Device&&!i.Used);if(supply is null)return "物品没有在入口携带，或已经使用。";
            if(supply.Definition=="CON-01"&&t.Hp==t.MaxHp||supply.Definition=="CON-03"&&t.Focus==t.MaxFocus)return "当前资源已满。";
            return null;
        }
        if(request.Action=="BASIC")return u.Ap>=2&&targets.Length==1&&targets[0].Side!=u.Side&&targets[0].Alive&&MeleeLegal(u,targets[0],"A")?null:"普通攻击目标被前排保护、已经倒地，或AP不足。";
        if(!_definitions.Actions.TryGetValue(request.Action,out var d)||!u.Active.Contains(d.Id,StringComparer.Ordinal))return "技能未在当前主动栏或援助技能中。";
        d=Variant(u,d);
        if(u.Ap<d.Ap||u.Focus<Cost(u,d).Fee||u.PersonalTurn<u.Ready.GetValueOrDefault(d.Id))return "AP、专注或冷却尚不满足。";
        if(targets.Length>d.MaximumTargets||targets.Any(t=>!t.Alive||(Friendly(d.Kind)?t.Side!=u.Side:t.Side==u.Side)))return "技能目标类型、数量或状态不符。";
        if(d.Kind is ActionKind.Stance or ActionKind.StepShield&&targets[0].Id!=u.Id)return "该技能只能作用于自身。";
        if(d.Kind==ActionKind.Damage&&targets.Any(t=>!MeleeLegal(u,t,d.Power)))return "近战不能越过存活的同列前排。";
        if(d.Kind==ActionKind.Trap&&_s.Traps.Count(t=>t.Owner==u.Id)>=(Has(u,"M08")||u.Id=="CP-OREN"&&u.Variants.GetValueOrDefault(24)=="B"?2:1))return "本人装置容量已占用。";
        if(d.Kind==ActionKind.Damage&&targets.Length>1&&u.FlatPressure>0&&(request.PressureTarget is null||!request.Targets.Contains(request.PressureTarget)))return "附加失衡压力必须明确分配给一个实际目标。";
        if(request.WardTarget is { } ward&&!Allies().Any(a=>a.Id==ward))return "装置援护目标不是当前站立友方。";
        return null;
    }
    public ActionPreviewView Preview(BattleRequest request)
    {
        string? issue=Validate(request);if(issue is not null)return new(false,issue,0,0,0,false);var u=Unit(request.Actor);
        if(request.Action=="BASIC"){var target=Unit(request.Targets[0]);int co=_definitions.Items.Items[u.Weapon].BasicCoefficientBp;if(u.Id!="hero")co=co*6500/10000;return new(true,null,2,0,Locked(target)?0:Predicted(u,target,"A",co,"BASIC"),Locked(target));}
        if(_definitions.Actions.TryGetValue(request.Action,out var d))
        {
            d=Variant(u,d);
            var t=Unit(request.Targets[0]);return new(true,null,d.Ap,Cost(u,d).Fee,d.Kind==ActionKind.Damage&&!Locked(t)?Predicted(u,t,d.Power!,d.Coefficient,d.Id):0,Locked(t));
        }
        return new(true,null,request.Action=="REVIVE"?2:request.Action=="END"?0:1,0,0,false);
    }
    public IntentPreviewView PreviewIntent(string source)
    {
        var intent=_s.Intents.SingleOrDefault(i=>i.Source==source)??throw new InvalidOperationException("该来源没有已公布意图。");var enemy=Unit(source);
        bool acted=Array.IndexOf(_s.Queue,source)<_s.Cursor;var targets=IntentTargets(intent);
        string? reason=!enemy.Alive?"来源已倒地":acted?"本轮已经执行":enemy.PendingBreak&&!enemy.Boss?"普通失衡取消本次意图":_s.Finale?.Combination is{Cancelled:true} combo&&combo.Source==source&&combo.Round==_s.Round?"公开合击已被实际失衡取消":targets.Length==0?"原定目标已不在场":null;
        var losses=reason is not null||!intent.Attack?[]:targets.Select(t=>
        {
            int declared=_s.Finale is {} finale?FinaleBattleRules.RookCombinedRaw(_s,finale,intent):intent.RawDamage;
            int raw=CombatMath.Damage(declared*10,_s.EnemyDamageBp,0,0,t.ArmorBp,Guard(t,Math.Max(enemy.PendingBreak&&enemy.Boss?3500:0,enemy.OutgoingWeakness)));
            int absorbed=Math.Min(raw,t.Shield);return new ThreatLoss(t.Id,raw,absorbed,Math.Min(t.Hp,raw-absorbed));
        }).ToArray();
        string? reinforcement=intent.Reinforcement is {} key?"增援 "+_s.ReinforcementTemplates[key].Name+" ×1；最早下一轮加入，最多四敌；占位时延后一轮，再执行未到场结果。":null;
        return new(source,reason is not null,reason,losses,reinforcement);
    }
    public void Apply(BattleRequest request)
    {
        ApplyAtomic(request);
        if(request.Action!="END")AfterAtomicAction(request.Actor);
        Terminal();ValidateState();
    }
    private void ApplyAtomic(BattleRequest request)
    {
        string? issue=Validate(request);if(issue is not null)throw new InvalidOperationException(issue);var u=Unit(request.Actor);
        if(request.Action=="END"){EndSlot(u);_s.Cursor++;Advance();return;}
        if(request.Action=="MOVE"){u.Ap--;Move(u,request.Destination!.Value);Log("move",u.Id,value:u.Slot);return;}
        if(request.Action=="CONTROL")
        {
            if(_s.Finale is {} finale){var result=FinaleBattleRules.ApplyControl(_s,finale,u,request.Device!);if(result.ControlledStop)Down(Unit(finale.Subject));}
            else _s.Controls[request.Device!]=true;
            u.Ap--;u.TurnUsed.Add("control");Log("control",u.Id,detail:request.Device!);return;
        }
        if(request.Action=="GUARD")
        {
            u.Ap--;u.TurnUsed.Add("guard");int value=u.GuardBase+(u.Weapon=="WP-01"&&u.TurnUsed.Contains("basic")?500:0)+(Has(u,"H12")&&u.Hp*2<u.MaxHp?1000:0);AddGuard(u,u,value,1,"guard");
            if(Has(u,"H08")&&Take(u.EncounterUsed,"h8"))Refund(u);if(Has(u,"S09")){u.NextShieldBonus=1500;u.ShieldBonusExpiry=Future(u,1,true);}if(Relic(u,"RE-01")&&Take(u.EncounterUsed,"re01"))Shield(u,u,2500,false);Log("guard",u.Id,value:Math.Min(3500,value));return;
        }
        if(request.Action=="RECLAIM")
        {
            var trap=_s.Traps.Single(t=>t.Id==request.Device&&t.Owner==u.Id);u.Ap--;_s.Traps.Remove(trap);if(Has(u,"M09")&&trap.PaidFocus>0&&Take(u.EncounterUsed,"m9"))Refund(u);Log("reclaim",u.Id,detail:trap.Id);return;
        }
        var targets=request.Targets.Select(Unit).ToArray();
        if(request.Action is "AID" or "REVIVE" or "SUPPLY"){Supply(u,targets[0],request);return;}
        if(request.Action=="BASIC")
        {
            u.Ap-=2;int co=_definitions.Items.Items[u.Weapon].BasicCoefficientBp;if(u.Id!="hero")co=co*6500/10000;
            DamageAction(u,targets,"A",co,u.Weapon=="WP-02"?1:0,0,false,"BASIC",false,targets[0].Id);u.TurnUsed.Add("basic");Take(u.EncounterUsed,"wp3");return;
        }
        var d=Variant(u,_definitions.Actions[request.Action]);var cost=Cost(u,d);u.Ap-=d.Ap;u.Focus-=cost.Fee;foreach(string token in cost.Tokens)Take(u.EncounterUsed,token);u.Ready[d.Id]=checked(u.PersonalTurn+d.Cooldown+1);
        Log("skill",u.Id,value:cost.Fee,detail:d.Id+" -> "+string.Join(',',request.Targets));
        switch(d.Kind)
        {
            case ActionKind.Damage:
                DamageAction(u,targets,d.Power!,d.Coefficient,d.Pressure,d.PressureIfExposed,d.ExposeAfter,d.Id,false,request.PressureTarget??targets[0].Id);
                if(d.WeakenBp>0)foreach(var t in targets.Where(t=>t.Alive)){t.OutgoingWeakness=d.WeakenBp;t.WeaknessExpiry=Future(u,1);}break;
            case ActionKind.Shield:case ActionKind.CleanseShield:case ActionKind.StepShield:case ActionKind.StepAllyShield:
                int coefficient=d.Shield+(d.Id is "S07" or "S10"&&Has(u,"S11")?1000:0);
                foreach(var t in targets)
                {
                    if(d.Kind==ActionKind.CleanseShield||d.Id=="CP-LINA-2"&&u.Variants.GetValueOrDefault(24)=="B")Cleanse(u,t,d.Id=="CP-MIRA-2"&&u.Variants.GetValueOrDefault(20)=="B"&&u.Variants.GetValueOrDefault(24)!="B"?2:1);
                    if(d.Id=="CP-TESS-2"&&u.Variants.GetValueOrDefault(20)=="B")t.RestraintExpiry=null;
                    if(d.Kind is ActionKind.StepShield or ActionKind.StepAllyShield&&request.Destination is { } slot&&CanMove(t,slot))Move(t,slot);
                    Shield(u,t,coefficient,true);if(d.GuardBp>0)AddGuard(u,t,d.GuardBp,1,d.Id);
                }
                FinishShield(u,targets);break;
            case ActionKind.Stance:u.Empowered=Math.Max(u.Empowered,2000);u.EmpowerExpiry=Future(u,1,true);AddGuard(u,u,1000,1,d.Id);break;
            case ActionKind.Empower:targets[0].Empowered=Math.Max(targets[0].Empowered,d.BonusBp);targets[0].EmpowerExpiry=Future(u,1,true);break;
            case ActionKind.Trap:
                _s.Sequence++;_s.Traps.Add(new("trap:"+_s.Sequence,u.Id,targets[0].Id,d.Coefficient,d.Pressure,cost.Fee,_s.Sequence,request.WardTarget));if(request.WardTarget is { } ward)u.DeviceWardTarget=ward;
                if(Has(u,"M05")&&Take(u.TurnUsed,"m5"))Shield(u,u,2000,false);break;
            default:throw new InvalidDataException("Unimplemented action kind.");
        }
    }
    private CombatActionDefinition Variant(BattleUnit u,CombatActionDefinition d)
    {
        if(u.Variants.GetValueOrDefault(24)=="B"&&d.Id=="CP-MIRA-2")d=d with{MaximumTargets=2,Shield=4000};
        if(u.Variants.GetValueOrDefault(12)=="A"&&d.Id.EndsWith("-1",StringComparison.Ordinal))d=d with{Coefficient=d.Coefficient+1000};
        if(u.Variants.GetValueOrDefault(12)=="B"&&d.Id.EndsWith("-2",StringComparison.Ordinal))d=d.Kind==ActionKind.Trap?d with{Coefficient=d.Coefficient+1000}:d with{Shield=d.Shield+1000};
        if(u.Variants.GetValueOrDefault(20)=="A"&&d.Id=="CP-OREN-1")d=d with{Pressure=2};if(u.Variants.GetValueOrDefault(24)=="A"&&d.Id is "CP-LINA-1" or "CP-OREN-1" or "CP-TESS-1")d=d with{Cooldown=0};
        if(u.Variants.GetValueOrDefault(24)=="A"&&d.Id=="CP-MIRA-1")d=d with{Pressure=d.Pressure+1};
        if(u.Variants.GetValueOrDefault(20)=="B"&&d.Id=="CP-LINA-2")d=d with{Kind=ActionKind.StepAllyShield};
        if(u.Variants.GetValueOrDefault(24)=="B"&&d.Id=="CP-TESS-2")d=d with{GuardBp=1500};return d;
    }
    private int Bonus(BattleUnit u,BattleUnit target,string power,string skill,bool device)
    {
        int bonus=u.DamageBonus+(device?0:u.Empowered);if(power=="A"&&Has(u,"H03")&&!device)bonus+=800;if(power=="T"&&Has(u,"M03")&&(device||skill!="BASIC"))bonus+=800;
        if(device){if(Has(u,"M11"))bonus+=2000;if(Relic(u,"RE-06"))bonus+=1000;}
        if(skill=="BASIC"&&u.Weapon=="WP-03"&&!u.EncounterUsed.Contains("wp3"))bonus+=1500;if(skill=="BASIC"&&u.Weapon=="WP-04"&&target.Exposed)bonus+=1000;return bonus;
    }
    private int Predicted(BattleUnit u,BattleUnit target,string power,int co,string skill)
    {
        int raw=CombatMath.Damage(power=="A"?u.AttackUnits:u.TechUnits,co+(skill=="H10"&&Has(u,"H11")&&target.Exposed?1500:0),Bonus(u,target,power,skill,false),target.Exposed?u.ExposureBonus+(Has(u,"H05")?1000:0):0,target.ArmorBp,Guard(target));
        if(_s.Finale is {} finale&&target.Id==finale.Subject){int absorbed=Math.Min(raw,target.Shield);return absorbed+FinaleBattleRules.ClampHpLoss(_s,finale,target,raw-absorbed);}
        return raw;
    }
    private void DamageAction(BattleUnit u,BattleUnit[] targets,string power,int coefficient,int pressure,int pressureIfExposed,bool expose,string skill,bool device,string pressureTarget)
    {
        bool effective=false;
        foreach(var target in targets)
        {
            if(!target.Alive||Locked(target)){Log("mechanism_locked",u.Id,target.Id);continue;}effective=true;bool oldExposure=target.Exposed;int bonus=Bonus(u,target,power,skill,device);int exposure=oldExposure?u.ExposureBonus+(Has(u,"H05")?1000:0):0;
            int raw=Hurt(u,target,power=="A"?u.AttackUnits:u.TechUnits,coefficient+(skill=="H10"&&oldExposure&&Has(u,"H11")?1500:0),bonus,exposure,0,device,true);
            int basePressure=pressure+(oldExposure?pressureIfExposed:0)+(oldExposure&&skill=="CP-TESS-1"&&u.Variants.GetValueOrDefault(20)=="A"?1:0);if(target.Id==pressureTarget)
            {
                int flat=u.FlatPressure;if((basePressure+flat)>0&&!device&&Relic(u,"RE-05")&&Take(u.TurnUsed,"re05"))flat++;Pressure(u,target,basePressure,flat);
            }
            else if(basePressure>0)throw new InvalidDataException("Multi-target pressure needs an explicit distribution.");
            if(expose&&target.Alive)Expose(target);if(oldExposure&&raw>0&&Has(u,"H09")&&Take(u.TurnUsed,"h9")){u.NextGuard=1500;u.NextGuardExpiry=Future(u,1);}
            if(u.Variants.GetValueOrDefault(20)=="A"&&skill=="CP-LINA-1")AddGuard(u,u,1000,1,"lina20");if(u.Variants.GetValueOrDefault(20)=="A"&&skill=="CP-MIRA-1")Shield(u,u,1500,false);
        }
        if(effective&&!device){u.Empowered=0;u.EmpowerExpiry=null;}
    }
    private int Hurt(BattleUnit source,BattleUnit target,int units,int coefficient,int bonus,int exposure,int extraGuard,bool device,bool consumeExposure)
    {
        int raw=CombatMath.Damage(units,coefficient,bonus,exposure,target.ArmorBp,Guard(target,extraGuard));int before=target.Hp,absorbed=Math.Min(raw,target.Shield);target.Shield-=absorbed;int hp=Math.Min(target.Hp,raw-absorbed);
        if(target.Id=="tower"&&_s.Mechanism=="tower"&&!_s.Controls["isolate"])hp=Math.Min(hp,Math.Max(0,target.Hp-1));
        if(_s.Finale is {} finale){int proposed=hp;hp=FinaleBattleRules.ClampHpLoss(_s,finale,target,hp);if(proposed>hp)Log("finale_overflow_discarded",source.Id,target.Id,proposed-hp,"Phase floor is applied before HP loss.");}
        target.Hp-=hp;
        if(consumeExposure)target.Exposed=false;if(raw>0){target.NextGuard=0;target.NextGuardExpiry=null;}Log(device?"device_damage":"damage",source.Id,target.Id,hp,"raw="+raw+", shield="+absorbed);
        if(!target.Alive)Down(target);else if(before*2>=target.MaxHp&&target.Hp*2<target.MaxHp&&Has(target,"S08")&&Take(target.EncounterUsed,"s8"))Shield(target,target,3500,false);return raw;
    }
    private void Down(BattleUnit u)
    {
        u.Hp=0;u.Shield=0;u.ShieldExpiry=null;u.Guards.Clear();u.NextGuard=0;u.NextGuardExpiry=null;u.Empowered=0;u.EmpowerExpiry=null;u.OutgoingWeakness=0;u.WeaknessExpiry=null;u.NextShieldBonus=0;u.ShieldBonusExpiry=null;u.Exposed=false;u.Dots.Clear();u.RestraintExpiry=null;u.Ap=0;_s.Traps.RemoveAll(t=>t.Owner==u.Id||t.Target==u.Id);Log("down",u.Id);
    }
    private void Expose(BattleUnit target){target.Exposed=true;target.ExposureUntilRound=_s.Round+1;Log("exposed",target.Id);}
    private void Pressure(BattleUnit source,BattleUnit target,int declared,int flat)
    {
        if(!target.Alive||target.Side!=CombatSide.Enemy||Locked(target)||target.RecoveryLocked)return;int amount=CombatMath.Pressure(declared,flat,source.Strength);if(amount==0)return;
        target.Pressure+=amount;target.LastPressureRound=_s.Round;Log("pressure",source.Id,target.Id,amount);
        if(target.Pressure>=target.PressureThreshold&&target.PressureThreshold>0){target.Pressure=0;target.PendingBreak=true;target.RecoveryLocked=true;if(_s.Finale is {} finale)FinaleBattleRules.CancelDeclaredRookCombination(finale,target.Id);Expose(target);Log("break",source.Id,target.Id,detail:target.Boss?"boss_weakened":"next_slot_cancelled");}
    }
    private int ShieldBonus(BattleUnit source,BattleUnit target,bool proactive)
    {
        int bonus=Has(source,"S03")?1000:0;
        if(proactive){if(source.Weapon=="WP-06"&&!source.TurnUsed.Contains("wp6"))bonus+=1000;if(source.Armor=="AR-06"&&target.Id!=source.Id)bonus+=1000;if(Relic(source,"RE-02")&&!source.EncounterUsed.Contains("re02"))bonus+=1000;bonus+=source.NextShieldBonus;}return bonus;
    }
    private void AddShield(BattleUnit source,BattleUnit target,int amount)
    {
        if(!source.Alive||!target.Alive)return;target.Shield=Math.Min(target.MaxHp/2,Math.Max(target.Shield,amount));var expiry=Future(source,2);if(target.ShieldExpiry is null||Forecast(expiry).CompareTo(Forecast(target.ShieldExpiry))>0)target.ShieldExpiry=expiry;Log("shield",source.Id,target.Id,target.Shield);
    }
    private void Shield(BattleUnit source,BattleUnit target,int coefficient,bool proactive)=>AddShield(source,target,CombatMath.Shield(source.TechUnits,coefficient,ShieldBonus(source,target,proactive)));
    private void FinishShield(BattleUnit source,BattleUnit[] targets)
    {
        Take(source.TurnUsed,"wp6");Take(source.EncounterUsed,"re02");source.NextShieldBonus=0;source.ShieldBonusExpiry=null;
        if(targets.Any(t=>t.Id!=source.Id))
        {
            if(Has(source,"S05")&&Take(source.TurnUsed,"s5"))Shield(source,source,2000,false);if(Relic(source,"RE-08")&&Take(source.TurnUsed,"re08"))Shield(source,source,1500,false);
        }
    }
    private void AddGuard(BattleUnit source,BattleUnit target,int amount,int slots,string key){target.Guards.RemoveAll(g=>g.Id==key+":"+source.Id);target.Guards.Add(new(key+":"+source.Id,Math.Min(3500,amount),Future(source,slots)));}
    private void Refund(BattleUnit u){if(u.Refunds>=2||!Take(u.TurnUsed,"refund"))return;u.Refunds++;u.Focus=Math.Min(u.MaxFocus,u.Focus+1);Log("focus_refund",u.Id,value:1);}
    private bool CanMove(BattleUnit u,int slot)
    {
        if(slot is <0 or >3||!u.Alive||u.TurnUsed.Contains("move")||u.RestraintExpiry is not null||Math.Abs(u.Slot%2-slot%2)+Math.Abs(u.Slot/2-slot/2)!=1)return false;
        var other=_s.Units.SingleOrDefault(t=>t.Side==u.Side&&t.Slot==slot);return other is null||other.Alive&&other.RestraintExpiry is null;
    }
    private void Move(BattleUnit u,int slot){var other=_s.Units.SingleOrDefault(t=>t.Side==u.Side&&t.Slot==slot);if(other is not null)other.Slot=u.Slot;u.Slot=slot;Take(u.TurnUsed,"move");}
    private bool CanOperate(BattleUnit u,string? control)=>_s.Finale is {} finale?FinaleBattleRules.CanOperate(_s,finale,u,control):_s.Mechanism=="tower"&&control is "left" or "right" or "isolate"&&!u.TurnUsed.Contains("control")&&!_s.Controls[control]&&u.Slot==(control=="left"?0:control=="right"?1:2)&&(control!="isolate"||_s.Controls["left"]&&_s.Controls["right"]);
    private void Cleanse(BattleUnit source,BattleUnit target,int count)
    {
        bool removed=false;
        for(int i=0;i<count;i++){if(target.Dots.Count>0){target.Dots.RemoveAt(0);removed=true;}else if(target.RestraintExpiry is not null){target.RestraintExpiry=null;removed=true;}else if(target.OutgoingWeakness>0){target.OutgoingWeakness=0;target.WeaknessExpiry=null;removed=true;}}
        if(removed){Log("cleanse",source.Id,target.Id);if(Relic(source,"RE-10")&&Take(source.EncounterUsed,"re10"))Shield(source,target,2500,false);}
    }
    private void Supply(BattleUnit u,BattleUnit target,BattleRequest request)
    {
        u.Ap-=request.Action=="REVIVE"?2:1;Take(u.TurnUsed,"supply");
        if(request.Action is "AID" or "REVIVE")
        {
            _s.Aid--;target.Hp=Math.Min(target.MaxHp,target.Hp+target.MaxHp*(request.Action=="REVIVE"?20:30)/100);
            if(request.Action=="REVIVE"){target.EligibleRound=_s.Round+1;target.Ap=0;if(Relic(u,"RE-12")&&Take(u.EncounterUsed,"re12"))Shield(u,target,2500,false);}Log(request.Action,u.Id,target.Id,target.Hp);return;
        }
        var supply=_s.Supplies.Single(i=>i.Instance==request.Device);int index=_s.Supplies.IndexOf(supply);_s.Supplies[index]=supply with{Used=true};
        switch(supply.Definition)
        {
            case "CON-01":target.Hp=Math.Min(target.MaxHp,target.Hp+target.MaxHp/2);break;
            case "CON-02":Cleanse(u,target,1);AddShield(u,target,target.MaxHp*15/100);break;
            case "CON-03":target.Focus=Math.Min(target.MaxFocus,target.Focus+3);break;
            case "CON-04":AddShield(u,target,target.MaxHp*40/100);break;
            default:throw new InvalidDataException("Unknown carried supply.");
        }
        Log("personal_supply",u.Id,target.Id,detail:supply.Definition);
    }
    private BattleUnit[] IntentTargets(EnemyIntent intent)=>intent.Lock==TargetLock.Unit?intent.Units.Select(Unit).Where(u=>u.Alive).ToArray():Allies().Where(u=>intent.Slots.Contains(u.Slot)).ToArray();
    private void TriggerTraps(BattleUnit enemy)
    {
        var fired=new HashSet<string>();
        foreach(var trap in _s.Traps.OrderBy(t=>t.Sequence).ToArray())
        {
            if(!_s.Traps.Contains(trap)||trap.Target!=enemy.Id||!enemy.Alive||enemy.PendingBreak&&!enemy.Boss||fired.Contains(trap.Owner))continue;var owner=Unit(trap.Owner);if(!owner.Alive)continue;
            _s.Traps.Remove(trap);fired.Add(trap.Owner);DamageAction(owner,[enemy],"T",trap.Coefficient,trap.BasePressure,0,false,"device",true,enemy.Id);
            if(owner.Id=="CP-OREN"&&owner.Variants.GetValueOrDefault(20)=="B"&&trap.WardTarget is { } ward){var ally=Unit(ward);if(ally.Alive)Shield(owner,ally,2000,false);}
            if(owner.Armor=="AR-04"&&Take(owner.EncounterUsed,"ar4"))Shield(owner,owner,2000,false);if(Has(owner,"M12")&&Take(owner.TurnUsed,"m12")){var target=trap.WardTarget is null?owner:Unit(trap.WardTarget);if(target.Alive)Shield(owner,target,2500,false);}
        }
    }
    private void EnemyAct(BattleUnit enemy)
    {
        var intent=_s.Intents.SingleOrDefault(i=>i.Source==enemy.Id);if(intent is null)return;
        if(enemy.PendingBreak&&!enemy.Boss){enemy.PendingBreak=false;enemy.PatternIndex++;Log("intent_cancelled",enemy.Id,detail:"stagger");return;}
        if(IntentTargets(intent).Length==0){enemy.PatternIndex++;if(!enemy.PendingBreak)enemy.RecoveryLocked=false;Log("intent_cancelled",enemy.Id,detail:"target_gone");return;}
        if(intent.Attack)TriggerTraps(enemy);if(!enemy.Alive)return;
        if(enemy.PendingBreak&&!enemy.Boss){enemy.PendingBreak=false;enemy.PatternIndex++;Log("intent_cancelled",enemy.Id,detail:"device_stagger");return;}
        int declared=_s.Finale is {} finale?FinaleBattleRules.RookCombinedRaw(_s,finale,intent):intent.RawDamage;
        bool weakened=enemy.PendingBreak;enemy.PendingBreak=false;var targets=IntentTargets(intent);
        if(intent.Reinforcement is { } template&&!(weakened&&enemy.Boss&&intent.Interruptible))
        {
            if(Take(enemy.EncounterUsed,"reinforcement"))
            {
                if(_s.Units.Any(u=>u.Id==_s.ReinforcementTemplates[template].Id)||_s.PendingReinforcements.Any(p=>p.Template==template))Log("reinforcement_fallback",enemy.Id,detail:"The unique declared unit is already present or booked.");
                else {_s.PendingReinforcements.Add(new(enemy.Id,template,_s.Round+1));Log("reinforcement_booked",enemy.Id,value:1,detail:"Source, count 1, once per encounter; earliest next round; occupied-slot fallback declared.");}
            }
            else Log("reinforcement_fallback",enemy.Id,detail:"Source has already used its one finite call.");
        }
        if(intent.Attack)foreach(var target in targets)Hurt(enemy,target,declared*10,_s.EnemyDamageBp,0,0,Math.Max(weakened&&enemy.Boss?3500:0,enemy.OutgoingWeakness),false,false);
        if(intent.Attack&&targets.Length>0){enemy.OutgoingWeakness=0;enemy.WeaknessExpiry=null;}enemy.PatternIndex++;if(enemy.RecoveryLocked&&!weakened)enemy.RecoveryLocked=false;
        Log("enemy_action",enemy.Id,value:declared,detail:intent.Name+" -> "+string.Join(',',targets.Select(t=>t.Id)));
    }
}
