using Godot;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.UI;
using Mist.Core.Combat;
using Mist.Core.Contracts;

public partial class Root
{
    private Guid? _battlePreview;
    private readonly List<Label> _battleCards=new();
    private string ActionName(string action)=>_catalog.Definitions["talents"].FirstOrDefault(d=>d.Id==action)?.Name??_catalog.Definitions["combat_actions"].FirstOrDefault(d=>d.Id==action)?.Name??(action switch
    {
        "BASIC"=>"基础攻击","GUARD"=>"基础防御","MOVE"=>"相邻换位","AID"=>"共享急救","REVIVE"=>"救起倒地友方","SUPPLY"=>"使用入口个人补给","RECLAIM"=>"回收本人装置","CONTROL"=>"现场操作","END"=>"结束个人计划槽",
        "H01"=>"探隙击","H02"=>"稳步架势","H04"=>"双段突击","H06"=>"掩护步","H07"=>"破绽斩","H10"=>"巡潮终式",
        "M01"=>"电弧","M02"=>"感应绊索","M04"=>"链式电弧","M06"=>"维修补片","M07"=>"牵引锚","M10"=>"磁场围栏",
        "S01"=>"折光护幕","S02"=>"隙光脉冲","S04"=>"协同援护","S06"=>"净化盾","S07"=>"分担护层","S10"=>"稳态屏障",
        "AUX-GUARD-1"=>"压制击","AUX-GUARD-2"=>"协防","AUX-SUPPORT-1"=>"电击","AUX-SUPPORT-2"=>"保护膜",_=>action
    });
    private static string SlotName(int slot)=>new[]{"左前","右前","左后","右后"}[slot];
    private async ValueTask OpenBattleEntrance()
    {
        var hero=await _party.InspectAsync();var flow=Panel("工坊正门 · 战前入口",InputOwner.Confirmation);
        Label(flow,$"当前等级 {hero.Level} · 校准 {hero.Grade}\n主角3AP；地方护卫与地方支援各2AP。共享急救2次。",22);
        Label(flow,"制止维护机械后仍须亲自到仓库取支架、安装、测试。这个遭遇不发击杀经验。",20);
        var difficulty=new OptionButton { AccessibilityName="战斗难度",FitToLongestItem=false };flow.AddChild(difficulty);foreach(string mode in new[]{"标准","轻松","挑战"})difficulty.AddItem(mode);
        var supplies=new Dictionary<string,CheckBox>();foreach(var item in _view.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal)))
        {
            var choice=new CheckBox {Text=item.DefinitionId+" · "+item.Id[^6..]};flow.AddChild(choice);supplies[item.Id]=choice;
        }
        Label(flow,"个人增强补给最多选择两件；不携带也可开始。战前入口会保存为保护检查点。",18);
        Button(flow,"明确进入正门战斗",()=>Execute(async()=>
        {
            string mode=new[]{"standard","relaxed","challenge"}[difficulty.Selected];var result=await _encounters.BeginWorkshopAsync(mode,supplies.Where(i=>i.Value.ButtonPressed).Select(i=>i.Key).ToArray());CheckCommit(result);ClosePanel();await Refresh();await OpenBattle();
        }));
        Button(flow,"返回现场继续调查其他方法",ClosePanel);
    }
    private static void CheckCommit(CommandResult result)
    {
        if(result.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))throw new InvalidOperationException(string.Join("；",result.Issues.Select(i=>i.SafeMessage))+" · "+result.Status);
    }
    private async ValueTask OpenBattle()
    {
        var encounter=await CurrentEncounterAsync();if(encounter is null)throw new InvalidOperationException("尚未进入遭遇。请在工坊正门明确选择战斗。");
        var s=encounter.Battle;var flow=Panel(TowerSelected?"潮门塔现场战斗":"工坊战斗",InputOwner.CombatDecision);
        Label(flow,$"第 {s.Round} 轮 · 共享急救 {s.Aid}/2 · {s.Difficulty}\n当前行动者：{s.Units.SingleOrDefault(u=>u.Id==s.CurrentActor)?.Name??"本次结果已稳定"}",24);
        Label(flow,"计划顺序："+string.Join(" → ",s.Queue.Select(id=>s.Units.Single(u=>u.Id==id).Name)),18);
        _battleCards.Clear();var cards=new GridContainer {Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};cards.AddThemeConstantOverride("h_separation",16);cards.AddThemeConstantOverride("v_separation",12);flow.AddChild(cards);
        foreach(var side in new[]{CombatSide.Ally,CombatSide.Enemy})foreach(int slot in new[]{0,1,2,3})
        {
            var unit=s.Units.SingleOrDefault(u=>u.Side==side&&u.Slot==slot);var card=new PanelContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};cards.AddChild(card);
            _battleCards.Add(Label(card,unit is null?$"{(side==CombatSide.Ally?"我方":"敌方")} {SlotName(slot)} · 空位":$"{(unit.Id==s.CurrentActor?"▶ ":"")}{unit.Name} · {SlotName(slot)}\nHP {unit.Hp}/{unit.MaxHp} · 盾 {unit.Shield} · AP {unit.Ap} · 专注 {unit.Focus}/{unit.MaxFocus}"+(side==CombatSide.Enemy?$"\n稳定 {unit.Pressure}/{unit.PressureThreshold} · {(unit.RecoveryLocked?"恢复中，压力不累积":"可累积压力")}":""),18));
        }
        Label(flow,"公开敌方意图",22);
        var battleRules=new BattleEngine(new BattleDefinitions(_catalog),s);
        foreach(var intent in s.Intents)
        {
            string target=intent.Lock==TargetLock.Unit?string.Join("、",intent.Units.Select(id=>s.Units.Single(u=>u.Id==id).Name)):intent.Lock==TargetLock.All?"全场四位":string.Join("、",intent.Slots.Select(SlotName));
            var forecast=battleRules.PreviewIntent(intent.Source);var enemy=s.Units.Single(e=>e.Id==intent.Source);
            string loss=forecast.Cancelled?forecast.Reason!:string.Join("；",forecast.Losses.Select(v=>s.Units.Single(t=>t.Id==v.Target).Name+$"：盾吸收 {v.ShieldAbsorbed}，HP预计减少 {v.HpLoss}"));
            Label(flow,$"{intent.Name} · {(intent.Lock==TargetLock.Unit?"锁定人物":intent.Lock==TargetLock.Column?"锁定位置":"全场")}：{target}\n原始威胁 {intent.RawDamage}；{(enemy.Boss?"首领失衡削弱攻击，整体行动保留":"普通失衡可取消")}。\n按当前防护估计：{(loss.Length==0?"无直接伤害":loss)}。尚未计入后续行动和未触发装置。"+(forecast.Reinforcement is {} call?"\n"+call:""),18);
        }
        var links=new HFlowContainer();flow.AddChild(links);Button(links,"暂停查已得档案",OpenArchive);Button(links,"暂停编辑同一份草稿",()=>{if(TowerSelected)Execute(OpenFindingWriter);else OpenWriter();});Button(links,"稳定边界保存",()=>Execute(async()=>{if(!await SaveDraft()||!await SaveFindingDraft())throw new InvalidOperationException("草稿尚未持久化。");await _store.CreateCheckpointAsync("manual");_status.Text="战斗状态与当前草稿指针已保存。";}));
        if(s.Outcome!=CombatOutcome.Active)
        {
            Label(flow,s.Outcome==CombatOutcome.Controlled?(TowerSelected?"两阀、隔离与机械制止已经实际成立。到现场安全控制台复核，再回接应泊位明确办理船务交接。":"维护机械已停用。返回场景后从正门走进工坊，亲自取得支架。"):s.Outcome==CombatOutcome.Defeat?"全队倒地。本次局部结果没有任务奖励，可返回战前入口重试。":"已撤回战前入口，本次局部成果已放弃。",22);
            if(s.Outcome==CombatOutcome.Defeat)Button(flow,"从完整入口重试",()=>Execute(async()=>{CheckCommit(await RetryEncounter());ClosePanel();await Refresh();await OpenBattle();}));return;
        }
        var u=s.Units.Single(a=>a.Id==s.CurrentActor);var actions=new[]{"BASIC","GUARD","MOVE","AID","REVIVE","SUPPLY","RECLAIM","CONTROL"}.Concat(u.Active).Append("END").ToArray();var selected=new OptionButton { FitToLongestItem=false,AccessibilityName="计划行动" };flow.AddChild(selected);
        foreach(string action in actions)selected.AddItem(ActionName(action));
        var targets=new Dictionary<string,CheckBox>();var targetRow=new HFlowContainer();flow.AddChild(targetRow);
        string? defaultEnemy=s.Units.FirstOrDefault(t=>t.Side==CombatSide.Enemy&&t.Alive)?.Id;
        foreach(var unit in s.Units)
        {
            var check=new CheckBox {Text=unit.Name+" · "+SlotName(unit.Slot),ButtonPressed=unit.Id==defaultEnemy};targetRow.AddChild(check);targets[unit.Id]=check;
        }
        selected.ItemSelected+=index=>
        {
            string action=actions[(int)index];bool friendly=new BattleDefinitions(_catalog).Actions.TryGetValue(action,out var def)&&def.Kind is not(ActionKind.Damage or ActionKind.Trap)||action is "AID" or "REVIVE" or "SUPPLY";
            foreach(var unit in s.Units)targets[unit.Id].ButtonPressed=friendly?unit.Id==u.Id:(action=="BASIC"||def?.Kind is ActionKind.Damage or ActionKind.Trap)&&unit.Id==defaultEnemy;
        };
        Label(flow,"需要换位时选择相邻位置；装置、补给和机关使用具体对象。",18);
        var positions=new OptionButton {FitToLongestItem=false,AccessibilityName="计划换位位置"};flow.AddChild(positions);positions.AddItem("不换位");foreach(int slot in new[]{0,1,2,3})positions.AddItem(SlotName(slot));
        var deviceChoices=new List<(string Id,string Label)>();deviceChoices.AddRange(s.Traps.Where(t=>t.Owner==u.Id).Select(t=>(t.Id,"本人装置 · "+t.Id)));deviceChoices.AddRange(s.Supplies.Where(i=>!i.Used).Select(i=>(i.Instance,"入口补给 · "+i.Definition)));deviceChoices.AddRange(s.Controls.Keys.Select(key=>(key,key=="left"?"左阀":key=="right"?"右阀":"检查并隔离供给")));
        var device=new OptionButton {FitToLongestItem=false,AccessibilityName="装置、补给或现场工作点"};flow.AddChild(device);device.AddItem("无对象");foreach(var choice in deviceChoices)device.AddItem(choice.Label);
        Button(flow,"预览此行动与费用",()=>Execute(async()=>
        {
            string action=actions[selected.Selected];string[] ids=action is "END" or "GUARD" or "MOVE" or "CONTROL" or "RECLAIM"?[]:targets.Where(t=>t.Value.ButtonPressed).Select(t=>t.Key).ToArray();var request=new BattleRequest(new(Guid.NewGuid()),action,u.Id,ids,positions.Selected>0?positions.Selected-1:null,device.Selected>0?deviceChoices[device.Selected-1].Id:null,ids.FirstOrDefault());
            var preview=await PreviewEncounter(request);if(!preview.Action.Legal)throw new InvalidOperationException(preview.Action.Reason);_battlePreview=preview.Token;
            var confirm=Panel("行动预览",InputOwner.Confirmation);Label(confirm,ActionName(action)+$" · AP {preview.Action.Ap} / 专注 {preview.Action.Focus}\n第一个伤害目标的预计原始结算 {preview.Action.PredictedDamage}（先由护盾吸收）。",22);
            Button(confirm,"执行已选行动",()=>Execute(async()=>{CheckCommit(await ConfirmEncounter(preview.Token));ClosePanel();ClosePanel();await Refresh();await OpenBattle();}));Button(confirm,"取消计划，保留当前资源",ClosePanel);
        }));
        Button(flow,"撤回完整战前入口…",()=>{var confirm=Panel("撤回本次遭遇",InputOwner.Confirmation);Label(confirm,"放弃本次局部伤害、机关操作与消耗，恢复入口。正文与已取得材料保留。",20);Button(confirm,"明确撤回入口",()=>Execute(async()=>{CheckCommit(await WithdrawEncounter());ClosePanel();ClosePanel();await Refresh();}));});
    }
}
