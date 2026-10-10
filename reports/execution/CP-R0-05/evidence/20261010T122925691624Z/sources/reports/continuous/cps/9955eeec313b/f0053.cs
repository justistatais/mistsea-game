using Godot;
using Mist.Application.FirstApproaches;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.FirstApproaches;

public partial class CampaignClient
{
    private FirstApproachService? _campaignApproaches;
    private bool _campaignRelaySelected;
    private CombatOutcome? _campaignRelayOutcome;
    private readonly HashSet<Guid> _campaignApproachTokens=[];
    private bool CampaignApproachSelected=>_campaignApproaches is not null&&_campaignRelaySelected;
    private bool CampaignApproachBlocksMovement=>_campaignRelayOutcome is CombatOutcome.Active or CombatOutcome.Defeat;
    private void LoadCampaignFirstApproaches()
    {
        using var file=Godot.FileAccess.Open("res://content/first_approaches/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        if(file is null)throw new InvalidDataException("中继与塔基处置来源包缺失；请修复安装，当前档案保留。");
        _campaignApproaches=new(_store,_store,_catalog,FirstApproachPackage.Parse(file.GetAsText()),_service.Geometry);
    }
    private async Task RefreshCampaignFirstApproaches()
    {
        if(_campaignApproaches is null)return;var current=await _campaignApproaches.EncounterAsync(FirstApproachRules.Relay);_campaignRelayOutcome=current?.Battle.Outcome;
        if(current?.Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat)_campaignRelaySelected=true;
        if(_view.Actor.Scene!="R0-S05"&&!CampaignApproachBlocksMovement)_campaignRelaySelected=false;
    }
    private void ResetCampaignFirstApproaches()
    {foreach(var token in _campaignApproachTokens)_campaignApproaches?.ClosePreview(token);_campaignApproachTokens.Clear();_campaignRelaySelected=false;_campaignRelayOutcome=null;}
    private void AddCampaignFirstApproachControls(VBoxContainer flow,string spot)
    {
        if(_campaignApproaches is null)return;
        foreach(var m in _campaignApproaches.Package.Materials.Where(m=>m.Scene=="R0-S05"&&m.Spot==spot))
        {string id=m.Id;Action(flow,"取得中继防卫完整说明",()=>Run(async()=>{Committed(await _campaignApproaches.DiscoverAsync(id));await OpenCampaignApproachMaterial(id);}));}
        if(spot=="R0-S05.relay")Action(flow,"中继检修边界 · 明确处置或继续",()=>Run(OpenCampaignRelayEntrance));
    }
    private async Task AddCampaignFirstApproachArchive(VBoxContainer flow)
    {if(_campaignApproaches is null)return;foreach(var m in await _campaignApproaches.MaterialsAsync()){string id=m.Id;Action(flow,"处置来源 · "+id,()=>Run(()=>OpenCampaignApproachMaterial(id)));}}
    private async Task OpenCampaignApproachMaterial(string id)
    {
        var m=await _campaignApproaches!.OpenMaterialAsync(id);var flow=Panel("中继防卫完整说明");Text(flow,m.Id+" · "+m.Version+" · 原创候选，人工审校待完成",18);
        var reader=new TextEdit{Text=string.Join("\n\n",m.Paragraphs.Select(p=>p.En)),Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName="中继防卫英文原文"};reader.SetMeta("base_font_size",22);reader.AddThemeFontSizeOverride("font_size",22*_scale/100);flow.AddChild(reader);
        Action(flow,"显示中继防卫中文辅助",()=>Text(flow,string.Join("\n\n",m.Paragraphs.Select(p=>p.Zh)),20));reader.GrabFocus();
    }
    private async Task OpenCampaignRelayEntrance()
    {
        _campaignRelaySelected=true;var current=await _campaignApproaches!.EncounterAsync(FirstApproachRules.Relay);
        if(current is not null&&current.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenCampaignFirstApproachBattle();return;}
        var flow=Panel("中继检修边界 · 实际防卫入口");Text(flow,"本次使用当前主角、装备和共享战斗规则。莉娜留在救援棚。制止浮机只允许接线，不代替隔离、供电、测试、邮包和接应核验。没有击杀经验。",22);
        var difficulty=new OptionButton{AccessibilityName="中继防卫难度"};foreach(string name in new[]{"标准","宽容","挑战"})difficulty.AddItem(name);flow.AddChild(difficulty);
        var supplies=new Dictionary<string,CheckBox>();foreach(var item in _view.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")){var c=new CheckBox{Text=item.DefinitionId+" · "+item.Id,AccessibilityName="中继入口补给 "+item.Id};flow.AddChild(c);supplies.Add(item.Id,c);}Text(flow,"可明确携带最多两件实际持有补给；默认不选。数值仍待人工试玩。",18);
        Action(flow,"明确进入中继防卫",()=>Run(async()=>{Committed(await _campaignApproaches.BeginAsync(FirstApproachRules.Relay,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToArray()));CloseAll();await Refresh();await OpenCampaignFirstApproachBattle();}));
    }
    private async Task OpenCampaignFirstApproachBattle()
    {
        _campaignRelaySelected=true;var state=await _campaignApproaches!.EncounterAsync(FirstApproachRules.Relay)??throw new InvalidOperationException("请在中继检修边界明确进入防卫。");var battle=state.Battle;_campaignRelayOutcome=battle.Outcome;
        var flow=Panel("中继防卫 · ENC-R0-02");Text(flow,$"第 {battle.Round} 轮 · 当前行动者 {battle.CurrentActor??"本次结果已稳定"} · 急救 {battle.Aid}/2 · {battle.Difficulty}",22);
        foreach(var unit in battle.Units)Text(flow,$"{unit.Name} · {(unit.Side==CombatSide.Ally?"我方":"敌方")}位置 {unit.Slot+1} · HP {unit.Hp}/{unit.MaxHp} · 盾 {unit.Shield} · AP {unit.Ap} · 专注 {unit.Focus}"+(unit.Side==CombatSide.Enemy?$" · 稳定 {unit.Pressure}/{unit.PressureThreshold}":""),20);
        var engine=new BattleEngine(_campaignApproaches.BattleDefinitions,battle);foreach(var intent in battle.Intents){var prediction=engine.PreviewIntent(intent.Source);Text(flow,intent.Name+" · 公开原始威胁 "+intent.RawDamage+" · "+(prediction.Cancelled?prediction.Reason:string.Join("；",prediction.Losses.Select(l=>battle.Units.Single(u=>u.Id==l.Target).Name+"预计HP减少 "+l.HpLoss))),18);}
        Action(flow,"暂停查中继已得档案",()=>Run(()=>OpenNav("档案")));Action(flow,"保存中继当前稳定状态",()=>Run(async()=>{await _store.CreateCheckpointAsync("manual");_status.Text="当前中继实例和资源已保存。";}));
        if(battle.Outcome!=CombatOutcome.Active)
        {
            Text(flow,battle.Outcome==CombatOutcome.Controlled?"浮机已实际制止。回到中继操作位继续隔离、接线、供电和测试；没有击杀经验。":"本次未形成接线通行结果，原文保留，可明确恢复入口。",22);
            if(battle.Outcome==CombatOutcome.Defeat)Action(flow,"从中继完整入口重试",()=>Run(async()=>{Committed(await _campaignApproaches.RetryAsync(FirstApproachRules.Relay));CloseAll();await Refresh();await OpenCampaignFirstApproachBattle();}));return;
        }
        var actor=battle.Units.Single(u=>u.Id==battle.CurrentActor);var ids=new[]{"BASIC","GUARD","MOVE","AID","REVIVE","SUPPLY","RECLAIM"}.Concat(actor.Active).Append("END").ToArray();var actions=new OptionButton{FitToLongestItem=false,AccessibilityName="中继计划行动"};foreach(string id in ids)actions.AddItem(id switch{"BASIC"=>"基础攻击","GUARD"=>"防御","MOVE"=>"相邻换位","AID"=>"共享急救","REVIVE"=>"救起友方","SUPPLY"=>"入口补给","RECLAIM"=>"本人装置回收","END"=>"结束个人计划槽",_=>_campaignApproaches.BattleDefinitions.Actions[id].Name});flow.AddChild(actions);
        var targets=new Dictionary<string,CheckBox>();foreach(var unit in battle.Units){var c=new CheckBox{Text=unit.Name,AccessibilityName="中继计划目标 "+unit.Id,ButtonPressed=unit.Id==battle.Units.FirstOrDefault(u=>u.Side==CombatSide.Enemy&&u.Alive)?.Id};flow.AddChild(c);targets.Add(unit.Id,c);}
        actions.ItemSelected+=index=>{string id=ids[(int)index];bool friendly=id is "AID" or "REVIVE" or "SUPPLY"||_campaignApproaches.BattleDefinitions.Actions.TryGetValue(id,out var d)&&d.Kind is not(ActionKind.Damage or ActionKind.Trap);foreach(var unit in battle.Units)targets[unit.Id].ButtonPressed=friendly?unit.Id==actor.Id:unit.Id==battle.Units.FirstOrDefault(u=>u.Side==CombatSide.Enemy&&u.Alive)?.Id;};
        var position=new OptionButton{AccessibilityName="中继计划换位位置"};foreach(string name in new[]{"不换位","左前","右前","左后","右后"})position.AddItem(name);flow.AddChild(position);var deviceItems=battle.Traps.Where(t=>t.Owner==actor.Id).Select(t=>(Id:t.Id,Name:"本人装置 · "+t.Id)).Concat(battle.Supplies.Where(s=>!s.Used).Select(s=>(Id:s.Instance,Name:"入口补给 · "+s.Definition))).ToArray();var device=new OptionButton{AccessibilityName="中继装置或入口补给"};device.AddItem("无对象");foreach(var item in deviceItems)device.AddItem(item.Name);flow.AddChild(device);
        Action(flow,"预览中继行动与费用",()=>Run(async()=>{string action=ids[actions.Selected];string[] chosen=action is "END" or "GUARD" or "MOVE" or "RECLAIM"?[]:targets.Where(t=>t.Value.ButtonPressed).Select(t=>t.Key).ToArray();var request=new BattleRequest(new(Guid.NewGuid()),action,actor.Id,chosen,position.Selected>0?position.Selected-1:null,device.Selected>0?deviceItems[device.Selected-1].Id:null,chosen.FirstOrDefault());var preview=await _campaignApproaches.PreviewActionAsync(FirstApproachRules.Relay,request);if(!preview.Action.Legal)throw new InvalidOperationException(preview.Action.Reason);_campaignApproachTokens.Add(preview.Token);var confirm=Panel("中继行动预览",()=>{_campaignApproaches.ClosePreview(preview.Token);_campaignApproachTokens.Remove(preview.Token);});Text(confirm,$"AP {preview.Action.Ap} · 专注 {preview.Action.Focus} · 预计结算伤害 {preview.Action.PredictedDamage}（先由盾吸收）",22);Action(confirm,"明确执行所选中继行动",()=>Run(async()=>{Committed(await _campaignApproaches.ConfirmAsync(preview.Token));Close();Close();await Refresh();await OpenCampaignFirstApproachBattle();}));}));
        Action(flow,"撤回完整中继入口…",()=>{var confirm=Panel("撤回中继入口");Text(confirm,"放弃本次局部战斗状态，恢复本次入口资源；已得说明和草稿保留。",20);Action(confirm,"明确撤回此次中继入口",()=>Run(async()=>{Committed(await _campaignApproaches.WithdrawAsync(FirstApproachRules.Relay));CloseAll();await Refresh();}));});
    }
}
