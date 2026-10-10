using System.Collections.Immutable;
using Godot;
using Mist.Application.Drafts;
using Mist.Application.OptionalLocalWorks;
using Mist.Application.UI;
using Mist.Core.Contracts;
using Mist.Core.OptionalLocalWorks;
using Mist.Core.Quests;
public partial class Root
{
    private SettlementSupportService? _settlementSupport;
    private readonly Dictionary<string,DraftEditor> _settlementEditors=[];
    private readonly Dictionary<string,Stack<Guid>> _settlementPreviews=[];
    private TextEdit? _settlementWriter;private Label? _settlementSaveStatus;
    private SettlementSupportService SettlementService()
    {if(_settlementSupport is not null)return _settlementSupport;using var f=Godot.FileAccess.Open("res://content/settlement_support_contracts.json",Godot.FileAccess.ModeFlags.Read)??throw new InvalidDataException("当前玩家包缺完整聚落公开合同。");return _settlementSupport=new(_store,_chapterSix??throw new InvalidOperationException("边境服务未加载。"),SettlementSupportContract.ParseJson(f.GetAsText()));}
    private void ResetSettlementSupportUi(){foreach(var stack in _settlementPreviews.Values)foreach(var token in stack)_settlementSupport?.ClosePreview(token);_settlementPreviews.Clear();_settlementEditors.Clear();_settlementSupport=null;_settlementWriter=null;_settlementSaveStatus=null;}
    private void CloseSettlementPreview(string title){if(_settlementPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_settlementSupport?.ClosePreview(token);if(title.StartsWith("聚落正文 · ",StringComparison.Ordinal)){_settlementWriter=null;_settlementSaveStatus=null;}}
    private void SettlementButton(VBoxContainer flow,string text,Func<ValueTask> action)=>Button(flow,text,()=>Execute(async()=>{await action();ClosePanel();await Refresh();}));
    private async ValueTask<bool> TrySettlementSupportInteraction(string spot)
    {
        if(_chapterSix is null||spot is not("R6-O01.notice" or "R6-O02.notice" or "R6-S05.manifest"))return false;
        var snap=await _store.SnapshotAsync();var q=snap.Entities.SingleOrDefault(e=>e.Id=="quest:Q-601");if(q is null||StrictJson.Deserialize<QuestState>(q.Json) is not{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed})return false;
        var service=SettlementService();var view=await service.ViewAsync();if(spot=="R6-S05.manifest"&&view.Packet is not{Decision:"accept",Delivered:false})return false;
        var flow=Panel("聚落局部生活 · "+spot,InputOwner.Workspace);Label(flow,"这些是公开新局部候选，尚未经人工语言评阅。追加包、共享压线台和借灯分别登记；不领取经验、刻币或零件，不占六塔、五箱补充或结局资格。读原文、保存正文、核验配置、预览和明确交付分开进行。",20);
        if(spot!="R6-S05.manifest")
        {string id=spot=="R6-O01.notice"?"O-R6-01":"O-R6-02";Button(flow,"实际取得聚落原告示和公开本地合同",()=>Execute(async()=>{CheckCommit(await _chapterSix.DiscoverAsync(id=="O-R6-01"?"R6-O01-R-NEEDS":"R6-O02-R-MAINTENANCE"));CheckCommit(await service.DiscoverAsync(id));ClosePanel();await OpenSettlementContract(id);}));if(view.Acquired.Contains(id))Button(flow,"阅读当前已取得的完整本地合同",()=>Execute(async()=>await OpenSettlementContract(id)));}
        if(spot=="R6-O01.notice")
        {
            var st=view.Packet;Label(flow,st is null?"两个天气预报：增补运输尚未接受。":$"追加运输 {st.Decision}；比较 {st.Comparison}；派运 {st.Dispatched}；同批交付 {st.Delivered}。",18);
            if(st?.Delivered!=true&&st?.Dispatched!=true)
            {foreach(var pair in new[]{("accept","明确接受三件可选增补运输"),("defer","暂缓增补运输"),("decline","拒绝增补运输")}){string d=pair.Item1;SettlementButton(flow,pair.Item2,async()=>CheckCommit(await service.DecidePacketAsync(d)));}
                Button(flow,"逐项比较两份预报的地点与窗口",()=>OpenSettlementForecastChoice());}
            if(st?.Dispatched==true&&st.Delivered!=true){Button(flow,"编辑增补运输实际正文",()=>Execute(async()=>await OpenSettlementWriter(SettlementSupportService.PacketTask)));Button(flow,"核对增补运输正文意思",()=>Execute(async()=>await OpenSettlementMeaning(SettlementSupportService.PacketTask)));Button(flow,"预览当前同批增补的实际收货",()=>Execute(async()=>await OpenSettlementDelivery("packet")));}
            if(view.Lamp is{Carried:true,Returned:false} lamp)
                SettlementButton(flow,"由原主人现场接收原同灯和已选有限安排",async()=>CheckCommit(await service.OwnerReceiveLampAsync(SettlementSupportService.LampOwner,lamp.Arrangement,"manual_reserve")));
            if(view.Lamp is{Returned:true,Completed:false}){Button(flow,"编辑借灯归还实际正文",()=>Execute(async()=>await OpenSettlementWriter(SettlementSupportService.LampTask)));Button(flow,"核对借灯归还正文意思",()=>Execute(async()=>await OpenSettlementMeaning(SettlementSupportService.LampTask)));Button(flow,"预览已归还同灯的有限安排",()=>Execute(async()=>await OpenSettlementDelivery("lamp")));}
        }
        else if(spot=="R6-S05.manifest")
        {SettlementButton(flow,"实际逐件核对独立三件增补库存",async()=>CheckCommit(await service.InspectExtraStockAsync(SettlementSupportService.PacketSerials.ToArray())));SettlementButton(flow,"明确由原司机和值守岗派运同一追加包",async()=>CheckCommit(await service.DispatchPacketAsync(SettlementSupportService.PacketSerials.ToArray(),"ROLE-R6-CART-DRIVER","ROLE-R6-CARGO-WATCH")));Label(flow,"派运后须沿当前真实出口到聚落甲，原到访不算本批抵达；阅读无现实倒计时。",18);}
        else
        {
            var press=view.Press;if(press is not null)Label(flow,$"当前真实压线台 DRIVE {(press.Input=="OFF"?0:12)}；REF 10。",18);Label(flow,press is null?"共享压线台维护尚未接受。":$"压线台 {press.Decision}；输入 {press.Input}；固定 {press.JawLatched}；配置 {press.Configuration}；当前零核验 {press.ZeroCheck==press.Configuration}；交接 {press.Completed}。",18);
            if(press?.Completed!=true)
            {
                foreach(var pair in new[]{("accept","明确接受地方共享压线台工作"),("defer","暂缓共享压线台工作"),("withdraw","撤回当前压线台许可")}){string d=pair.Item1;SettlementButton(flow,pair.Item2,async()=>CheckCommit(await service.DecidePressAsync(d)));}
                SettlementButton(flow,"向两个不同当地岗位请求有限服务窗口许可",async()=>CheckCommit(await service.RequestPressPermissionAsync("service_interval","ROLE-R6-O02-OPERATOR","ROLE-R6-O02-CUSTODIAN","manual_reserve")));
                SettlementButton(flow,"实际检查同两件手工备用",async()=>CheckCommit(await service.InspectManualReserveAsync(["R6-O02-MANUAL-01","R6-O02-MANUAL-02"])));
                ChoiceAction(flow,"压线台实际输入档位",["尚未选择","OFF","LIVE"],async n=>CheckCommit(await service.SetPressInputAsync(n==1?"OFF":"LIVE")));
                SettlementButton(flow,"断输入后实际固定压口",async()=>CheckCommit(await service.LatchPressJawAsync(true)));
                ZeroChoice(flow,"压线台",press?.Configuration??0,async(c,n,v)=>CheckCommit(await service.VerifyPressZeroAsync(c,n,v)));
                Button(flow,"编辑共享维护实际正文",()=>Execute(async()=>await OpenSettlementWriter(SettlementSupportService.PressTask)));Button(flow,"核对共享维护正文意思",()=>Execute(async()=>await OpenSettlementMeaning(SettlementSupportService.PressTask)));Button(flow,"预览当前压线台的维护责任",()=>Execute(async()=>await OpenSettlementDelivery("press")));
            }
            var lamp=view.Lamp;if(lamp is not null)Label(flow,$"当前真实借灯 DRIVE {(lamp.Isolated?0:6)}；REF 10。",18);Label(flow,lamp is null?"借来的灯：同灯保养归还尚未接受。":$"借灯 {lamp.Decision}；配置 {lamp.Configuration}；保养 {lamp.Serviced}；实际归还 {lamp.Returned}；交接 {lamp.Completed}。灯盒家庭便条始终没有阅读许可。",18);
            if(lamp?.Completed!=true&&lamp?.Carried!=true)
            {
                foreach(var pair in new[]{("accept","明确接受原同灯保养归还邀请"),("defer","暂缓原同灯邀请"),("withdraw","撤回当前同灯许可")}){string d=pair.Item1;SettlementButton(flow,pair.Item2,async()=>CheckCommit(await service.DecideLampAsync(d)));}
                SettlementButton(flow,"核对双方原同灯保养归还限定许可",async()=>CheckCommit(await service.RequestLampPermissionAsync(SettlementSupportService.LampOwner,SettlementSupportService.LampBorrower,"same_lamp_service_return_only")));
                SettlementButton(flow,"实际检查借来的原同一灯序号",async()=>CheckCommit(await service.InspectLampAsync(SettlementSupportService.LampSerial)));
                ChoiceAction(flow,"借灯实际输入档位",["尚未选择","OFF","LIVE"],async n=>CheckCommit(await service.SetLampInputAsync(n==1?"OFF":"LIVE")));
                SettlementButton(flow,"断输入后实际固定同灯灯盖",async()=>CheckCommit(await service.LatchLampCoverAsync()));ZeroChoice(flow,"借灯",lamp?.Configuration??0,async(c,n,v)=>CheckCommit(await service.VerifyLampZeroAsync(c,n,v)));
                SettlementButton(flow,"当前零核验后清理原同灯触点",async()=>CheckCommit(await service.ServiceSameLampAsync(SettlementSupportService.LampSerial)));
                ChoiceAction(flow,"归还后明确有限安排",["尚未选择","原同灯归还留在主人处","一班主人后一班借用的有限互助"],async n=>CheckCommit(await service.CarrySameLampAsync(n==1?"return_same_lamp":"finite_mutual_aid")));
            }
        }
        return true;
    }
    private void ChoiceAction(VBoxContainer flow,string name,string[] labels,Func<int,ValueTask> action)
    {var choice=new OptionButton{AccessibilityName=name};foreach(var t in labels)choice.AddItem(t);flow.AddChild(choice);SettlementButton(flow,"明确记录："+name,async()=>{if(choice.Selected==0)throw new InvalidOperationException("请选择当前明确动作。");await action(choice.Selected);});}
    private void ZeroChoice(VBoxContainer flow,string device,long version,Func<string,int,long,ValueTask> action)
    {var channel=new OptionButton{AccessibilityName=device+"核零通道"};foreach(var t in new[]{"尚未选择","DRIVE","REF"})channel.AddItem(t);flow.AddChild(channel);var value=new SpinBox{MinValue=0,MaxValue=100,Step=1,AccessibilityName=device+"实际读数"};flow.AddChild(value);SettlementButton(flow,"明确核对"+device+"当前配置零能",async()=>{if(channel.Selected==0)throw new InvalidOperationException("请明确通道与当前读数。");await action(channel.Selected==1?"DRIVE":"REF",(int)value.Value,version);});}
    private async ValueTask OpenSettlementContract(string id)
    {var service=SettlementService();var source=await service.OpenContractAsync(id);var flow=Panel("聚落完整公开原文 · "+id,InputOwner.Workspace);var reader=new TextEdit{Text=string.Join("\n\n",source.Paragraphs.Select(p=>p.English)),Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,420),AccessibilityName="聚落公开合同完整英文原文"};flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"查看本地合同中文辅助",()=>{foreach(var p in source.Paragraphs)Label(flow,p.Chinese,20);});Label(flow,"地方预报、序号和岗位是公开新局部候选；原地区告示未改。没有借灯家庭便条，也没有候选录音播放或人审声称。",18);}
    private void OpenSettlementForecastChoice()
    {var flow=Panel("两份预报地点窗口比较",InputOwner.Confirmation);(string Key,string[] Labels,string[] Values)[] rows=[("山口窗口",["尚未选择","current_survey_stage","earlier_arrival_stage"],["","current_survey_stage","earlier_arrival_stage"]),("地方观测地点",["尚未选择","southern_inlet","north_delivery_ridge"],["","southern_inlet","north_delivery_ridge"]),("地方观测窗口",["尚未选择","earlier_arrival_stage","current_survey_stage"],["","earlier_arrival_stage","current_survey_stage"]),("当前运输通道",["尚未选择","marked_leeward","low_cut"],["","marked_leeward","low_cut"]),("备用",["尚未选择","sheltered_return","no_return"],["","sheltered_return","no_return"])];var picks=rows.Select(r=>{Label(flow,r.Key,18);var o=new OptionButton{AccessibilityName=r.Key};foreach(var text in r.Labels)o.AddItem(text);flow.AddChild(o);return o;}).ToArray();SettlementButton(flow,"明确核对两份有地点和窗口的预报",async()=>{if(picks.Any(p=>p.Selected==0))throw new InvalidOperationException("逐项选择公开原文中的比较。");var v=rows.Select((r,i)=>r.Values[picks[i].Selected]).ToArray();CheckCommit(await SettlementService().CompareForecastsAsync(v[0],v[1],v[2],v[3],v[4]));});}
    private async ValueTask OpenSettlementWriter(string task)
    {if(!await SaveSettlementDrafts())throw new InvalidOperationException("请完成输入法候选并保存现有本地稿。");if(!_settlementEditors.TryGetValue(task,out var editor)){var d=await _store.GetDraftAsync(task);var ses=_store.Session;editor=new(_store,new(ses.Profile,ses.Branch,ses.Load,task,d?.Id,d?.Revision??0,d?.Body??"",SettlementService().Contract.Version));_settlementEditors[task]=editor;}string title="聚落正文 · "+task;if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_settlementWriter?.GrabFocus();return;}var flow=Panel(title,InputOwner.Editable);Label(flow,"Write a bounded local note identifying the actual source, places and stages, same physical objects, ownership and limits. Saving prose does not deliver items or certify English understanding.",22);var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="聚落实际英文正文"};flow.AddChild(writer);_settlementWriter=writer;_settlementSaveStatus=Label(flow,"当前稿件须由真实保存回执确认。",18);var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveSettlementDrafts();};writer.TextChanged+=()=>{editor.Edit(writer.Text);timer.Start();foreach(var stack in _settlementPreviews.Values)foreach(var token in stack)SettlementService().ClosePreview(token);_settlementPreviews.Clear();if(_settlementSaveStatus is{} label&&IsInstanceValid(label))label.Text="当前正文有未保存修改，旧确认已关闭。";};writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveSettlementDrafts())throw new InvalidOperationException("正文未保存，先完成候选或导出。");ClosePanel();});Button(flow,"保存当前聚落正文",()=>Execute(async()=>{if(!await SaveSettlementDrafts())throw new InvalidOperationException("本地正文尚未保存，请完成输入法候选或导出。");}));Button(flow,"导出当前聚落正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string p=Path.Combine(dir,"settlement-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(p,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前聚落正文已导出："+p;});Button(flow,"逐项核对当前聚落正文意思",()=>Execute(async()=>await OpenSettlementMeaning(task)));writer.GrabFocus();}
    private async ValueTask<bool> SaveSettlementDrafts()
    {if(Composing())return false;try{foreach(var e in _settlementEditors.Values)if(!e.IsSaved)await e.SaveAsync();bool saved=_settlementEditors.Values.All(e=>e.IsSaved);if(_settlementSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"当前正文已由真实CAS回执保存。":"修改尚未全数保存。";return saved;}catch(Exception error){if(_settlementSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，原文仍可导出："+error.Message;return false;}}
    private async ValueTask OpenSettlementMeaning(string task)
    {if(!await SaveSettlementDrafts())throw new InvalidOperationException("先完成输入法候选并保存正文。");var view=await SettlementService().ViewAsync();(string Key,string Value)[] fields=task switch{SettlementSupportService.PacketTask=>[("route","marked_leeward"),("fallback","sheltered_return"),("windows","different_places_and_stages"),("stock","three_optional_serials"),("scope","extra_only_no_ending")],SettlementSupportService.PressTask=>[("window","service_interval"),("operator","ROLE-R6-O02-OPERATOR"),("reviewer","ROLE-R6-O02-CUSTODIAN"),("fallback","manual_reserve"),("scope","isolated_press_only")],_=>[("serial",SettlementSupportService.LampSerial),("owner",SettlementSupportService.LampOwner),("arrangement",view.Lamp?.Arrangement??""),("fallback","manual_reserve"),("privacy","family_note_not_opened"),("scope","same_serviced_returned_lamp_only")]};var flow=Panel("聚落正文意思核对 · "+task,InputOwner.Confirmation);Label(flow,"当前为明确结构化辅助；不自动理解英文，不计语言分数。每项须由你明确选择，且符合实际现场事实。",20);var choices=fields.Select(f=>{Label(flow,f.Key,18);var o=new OptionButton{AccessibilityName=f.Key};o.AddItem("尚未选择");o.AddItem(f.Value);o.AddItem("其他或尚未核对");flow.AddChild(o);return o;}).ToArray();SettlementButton(flow,"明确记录当前正文的逐项意思",async()=>{if(choices.Any(o=>o.Selected==0))throw new InvalidOperationException("请逐项明确选择正文意思。");var values=fields.Select((f,i)=>new KeyValuePair<string,string>(f.Key,choices[i].Selected==1?f.Value:"unverified_other")).ToImmutableDictionary();CheckCommit(await SettlementService().ConfirmMeaningAsync(task,values,true));});}
    private async ValueTask OpenSettlementDelivery(string kind)
    {if(!await SaveSettlementDrafts())throw new InvalidOperationException("请先保存当前稿件。");var service=SettlementService();var p=kind switch{"packet"=>await service.PreviewPacketDeliveryAsync(),"press"=>await service.PreviewPressResponsibilityAsync(),_=>await service.PreviewLampHandoverAsync()};string title="聚落明确交付预览 · "+kind;var flow=Panel(title,InputOwner.Confirmation);if(!p.Ready){foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,20);return;}if(!_settlementPreviews.TryGetValue(title,out var stack)){stack=[];_settlementPreviews[title]=stack;}stack.Push(p.Token);Label(flow,"只读预览对应当前源版本、同批实物或同机配置、实际到场回执和同一保存稿件。关闭不交付。明确确认后一次登记本地后果，不发主线奖励、塔站准备或结局条件。",20);SettlementButton(flow,"明确提交当前已核验本地交付",async()=>CheckCommit(await service.ConfirmPreviewAsync(p.Token)));}
}
