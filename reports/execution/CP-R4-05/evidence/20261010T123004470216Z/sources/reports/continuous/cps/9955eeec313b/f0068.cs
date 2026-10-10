using System.Collections.Immutable;
using Godot;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Learning;
using Mist.Application.OptionalEncounterContracts;
using Mist.Application.UI;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Learning;
using Mist.Core.OptionalEncounterContracts;

public partial class Root
{
    private OptionalEncounterService? _optionalEncounters;
    private string? _optionalEncounterSelected;
    private void LoadOptionalEncounterContracts(ImmutableDictionary<string,SceneGeometry> geometry)
    {
        using var file=Godot.FileAccess.Open("res://content/optional_encounter_contracts/learner_package.json",Godot.FileAccess.ModeFlags.Read)??throw new InvalidDataException("独立可选安全合同来源包缺失；请修复当前安装。");
        _optionalEncounters=new(_store,_store,_catalog,OptionalContractPackage.Parse(file.GetAsText()),geometry);
        RegisterRegionalLearningScope("optional-contracts",ReadOptionalEncounterLearningSources);
    }
    private ImmutableDictionary<string,SceneGeometry> ExtendOptionalEncounterGeometry(ImmutableDictionary<string,SceneGeometry> geometry)=>_optionalEncounters is null?geometry:OptionalContractRules.ExtendGeometry(_optionalEncounters.Package,geometry);
    private void ResetOptionalEncounterUi(){_optionalEncounterSelected=null;}
    private bool OptionalEncounterSelected
    {
        get
        {
            if(_optionalEncounters is null||_view is null)return false;
            var active=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&OptionalContractRules.Ids.Any(id=>e.Id=="combat:"+id)).Select(e=>StrictJson.Deserialize<OptionalContractBattle>(e.Json)).SingleOrDefault(e=>e.Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat);
            if(active is not null)_optionalEncounterSelected=active.Encounter;
            if(_optionalEncounterSelected is null)return false;var d=_optionalEncounters.Package.Contracts.Single(c=>c.Id==_optionalEncounterSelected);if(d.Scene!=_view.Actor.Scene){_optionalEncounterSelected=null;return false;}return true;
        }
    }
    private string OptionalEncounterTitle=>"独立可选安全合同 · "+_optionalEncounters!.Package.Contracts.Single(c=>c.Id==_optionalEncounterSelected).TitleZh;
    private async ValueTask<EncounterState?> OptionalCurrentEncounterAsync(){var s=await _optionalEncounters!.EncounterAsync(_optionalEncounterSelected!);return s is null?null:new(s.Battle,s.Entry,s.ReturnActor,s.ResultApplied);}
    private ValueTask<EncounterActionPreview> OptionalPreviewEncounter(BattleRequest r)=>_optionalEncounters!.PreviewActionAsync(_optionalEncounterSelected!,r);
    private ValueTask<CommandResult> OptionalConfirmEncounter(Guid token)=>_optionalEncounters!.ConfirmAsync(token);
    private ValueTask<CommandResult> OptionalRetryEncounter()=>_optionalEncounters!.RetryAsync(_optionalEncounterSelected!);
    private async ValueTask<CommandResult> OptionalWithdrawEncounter(){var result=await _optionalEncounters!.WithdrawAsync(_optionalEncounterSelected!);if(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted)_optionalEncounterSelected=null;return result;}
    private void OptionalCloseEncounterPreview(Guid token)=>_optionalEncounters?.ClosePreview(token);
    private async ValueTask<IReadOnlyCollection<LearningSourceAccess>> ReadOptionalEncounterLearningSources()=>_optionalEncounters is null?Array.Empty<LearningSourceAccess>():(await _optionalEncounters.MaterialsAsync()).Select(m=>new LearningSourceAccess(m.Id,m.ContentVersion,OptionalContractRules.Fingerprint(m))).ToArray();
    private async ValueTask AddOptionalEncounterArchive(VBoxContainer flow)
    {if(_optionalEncounters is null)return;foreach(var source in await _optionalEncounters.MaterialsAsync()){string id=source.Id;Button(flow,"独立安全合同原说明 · "+id,()=>Execute(async()=>await OpenOptionalEncounterMaterial(id)));}}
    private async ValueTask OpenOptionalEncounterMaterial(string id)
    {
        var m=await _optionalEncounters!.OpenMaterialAsync(id);var d=_optionalEncounters.Package.Contracts.Single(c=>c.Material==id);var flow=Panel("独立可选合同原说明 · "+d.TitleZh,InputOwner.Workspace);Label(flow,m.ContentVersion+" · 原创内容候选，语言与数值仍待人工复核",18);var reader=new TextEdit{Text=string.Join("\n\n",m.Paragraphs.Select(p=>p.En)),Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,420),AccessibilityName="独立合同完整英文原文 "+id};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"显示这份独立合同的中文辅助",()=>Label(flow,string.Join("\n\n",m.Paragraphs.Select(p=>p.Zh)),20));reader.GrabFocus();
    }
    private async ValueTask<bool> TryOptionalEncounterInteraction(string spot)
    {
        if(_optionalEncounters is null)return false;var d=_optionalEncounters.Package.Contracts.SingleOrDefault(c=>c.Spot==spot);if(d is null)return false;
        var flow=Panel("独立可选安全合同 · "+d.TitleZh,InputOwner.Workspace);Label(flow,d.Title+" · "+d.Id,22);Label(flow,"这份追加工作只覆盖本站声明的封闭备用设备。当前主线、已有物资与塔站安全分别记账；合同没有经验、币或零件奖励。",20);
        Button(flow,"取得本站这份完整英文合同",()=>Execute(async()=>{CheckCommit(await _optionalEncounters.DiscoverAsync(d.Material));ClosePanel();await OpenOptionalEncounterMaterial(d.Material);}));
        if((await _optionalEncounters.MaterialsAsync()).Any(m=>m.Id==d.Material))Button(flow,"打开这份已取得的独立合同",()=>Execute(async()=>await OpenOptionalEncounterMaterial(d.Material)));
        var result=await _optionalEncounters.ResultAsync(d.Id);if(result is not null){Label(flow,"本次设备制止与现场检查已受理。目标："+result.Target+"；同一合同保留一个结果。",22);return true;}
        var permit=await _optionalEncounters.PermitAsync(d.Id);
        if(permit is not{Allowed:true})Button(flow,"明确接受这份追加安全合同",()=>Execute(async()=>{if(!await SaveAdventureDrafts())throw new InvalidOperationException("当前正文未保存，窗口保留，请重试或导出。");CheckCommit(await _optionalEncounters.AcceptContractAsync(d.Id));ClosePanel();await Refresh();}));
        else
        {
            Label(flow,"本次明确许可只覆盖：阅读本说明、制止所列设备、检查目标 "+d.Target+"。保管岗位："+d.LocalCustodian,18);
            Button(flow,"撤回这份合同许可并交还完整入口",()=>Execute(async()=>{if(!await SaveAdventureDrafts())throw new InvalidOperationException("当前正文未保存，窗口保留。");CheckCommit(await _optionalEncounters.RevokeContractAsync(d.Id));_optionalEncounterSelected=null;CloseAllPanels();await Refresh();}));
            var encounter=await _optionalEncounters.EncounterAsync(d.Id);
            if(encounter?.Battle.Outcome==CombatOutcome.Controlled)Button(flow,"现场检查这份合同的已停备用设备",()=>Execute(async()=>{CheckCommit(await _optionalEncounters.InspectTargetAsync(d.Id,d.InspectionAction));ClosePanel();await Refresh();}));
            Button(flow,encounter?.Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat?"继续这份独立合同的同一遭遇":"明确选择这份独立合同的战斗入口",()=>Execute(async()=>await OpenOptionalEncounterEntrance(d.Id)));
        }
        Button(flow,"先保留原说明，离开追加工作窗口",()=>Execute(async()=>{if(!await SaveAdventureDrafts())throw new InvalidOperationException("当前正文未保存，窗口保留。");ClosePanel();}));return true;
    }
    private async ValueTask OpenOptionalEncounterEntrance(string id)
    {
        _optionalEncounterSelected=id;var old=await _optionalEncounters!.EncounterAsync(id);if(old is not null&&old.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}
        var d=_optionalEncounters.Package.Contracts.Single(c=>c.Id==id);var flow=Panel("独立合同实际战前入口 · "+d.TitleZh,InputOwner.Confirmation);Label(flow,"使用当前真实主角、装备与两个合法援助席。各设备意图在计划前公布，战斗制止后仍须回本站检查具体目标。继续沿用同一实例；撤回恢复本次完整战前入口。",22);
        var difficulty=new OptionButton{AccessibilityName="独立合同遭遇难度"};foreach(string text in new[]{"标准","宽容","挑战"})difficulty.AddItem(text);flow.AddChild(difficulty);var supplies=new Dictionary<string,CheckBox>();foreach(var item in _view.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")){var check=new CheckBox{Text=item.DefinitionId+" · "+item.Id,AccessibilityName="独立合同入口补给 "+item.Id};flow.AddChild(check);supplies.Add(item.Id,check);}Label(flow,"最多选择两件当前持有补给，默认不带。候选数值尚未经人工平衡验收。",18);
        Button(flow,"明确进入这份独立合同的遭遇",()=>Execute(async()=>{if(!await SaveAdventureDrafts())throw new InvalidOperationException("当前正文未保存，入口未建立。");CheckCommit(await _optionalEncounters.BeginAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
}
