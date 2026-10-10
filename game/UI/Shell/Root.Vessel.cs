using Godot;
using Mist.Application.UI;
using Mist.Application.VesselInterior;
using Mist.Application.Travel;
using Mist.Core.Contracts;
using Mist.Core.VesselInterior;

public partial class Root
{
    private readonly Dictionary<string,Stack<Guid>> _vesselUiPreviews=new();
    private void CloseVesselUiPreview(string title)
    {
        if(_vesselUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_vessel?.ClosePreview(token);
    }
    private async ValueTask<bool> TryVesselInteraction(string spot)
    {
        if(_vessel is null)return false;
        if(spot=="R1-S01.glimmer_handover"&&HasReceipt(await _store.SnapshotAsync(),"Q-104")){await OpenVesselBerth();return true;}
        if(spot is "R2-S01.berth" or "R3-S01.berth" or "R4-S01.berth" or "R5-S01.berth" or "R6-S01.berth"){await OpenVesselPreview(await _vessel.PreviewBoardAsync(),"微光号登船预览","明确登上微光号");return true;}
        if(!spot.StartsWith("HUB-",StringComparison.Ordinal))return false;
        if(spot=="HUB-S01.gangway"){await OpenVesselPreview(await _vessel.PreviewLeaveAsync(),"返回当前泊位","明确离船返回当前泊位");return true;}
        var view=await _vessel.WorkspaceAtAsync(spot);var flow=Panel(view.Station.Label,InputOwner.Workspace);AddVesselLifeButton(flow,spot);
        if(spot is "HUB-S01.deckwatch" or "HUB-S02.correspondence")Button(flow,"莉娜当前受邀个人工作",()=>Execute(OpenLinaWorkList));
        switch(view.Station.Kind)
        {
            case VesselStationKind.Deckwatch:
                Label(flow,"甲板值守：微光号值守岗位。当前泊位 "+view.Ship.DockRegion+"，船务记录属于这艘已移交的船。",22);break;
            case VesselStationKind.HarborView:
                Label(flow,"当前合法泊位："+view.Ship.DockRegion+"。区域航线必须另有实际核查与航行许可。",22);Button(flow,"查看当前地图",OpenMap);break;
            case VesselStationKind.Archive:
                Label(flow,$"当前已取得 {view.Entities.Length} 份材料。原文与中文辅助分别查看。",22);Button(flow,"打开已取得档案",OpenArchive);break;
            case VesselStationKind.Correspondence:
                Label(flow,"已取得的调度与往来记录保存在档案中。来信的时间和来源按原件保留。",22);Button(flow,"查阅当前档案",OpenArchive);break;
            case VesselStationKind.DraftDesk:
                Label(flow,"共用存档中已保存的正文：",22);foreach(var head in view.DraftHeads)Label(flow,head.TaskId+" · 修订 "+head.Revision,18);
                Button(flow,"编辑当前地区工作正文",()=>Execute(OpenCurrentWritingWorkspace));Button(flow,"编辑撤离正文",OpenWriter);Button(flow,"编辑事故核查正文",()=>Execute(OpenFindingWriter));break;
            case VesselStationKind.Chart:
                Label(flow,view.Entities.Length==0?"尚无实际登记航线或泊位。":"当前已核验航路、泊位与返程点：",22);foreach(var entity in view.Entities)Label(flow,entity.Id,18);Button(flow,"查看当前地图",OpenMap);
                if(_voyage is not null&&view.Ship.DockRegion=="R1")
                {
                    Label(flow,"跨区核查委托允许前往修院的公开接驳站。航路登记与起航分别确认；登记不会取得当地档案。",20);
                    Button(flow,"核对并登记 R2 航路与公开泊位",()=>Execute(async()=>
                    {
                        var prepared=await _voyage.VerifyAndRegisterAsync(Mist.Core.Voyage.VoyageRules.R2);CheckCommit(prepared.Handoff);CheckCommit(prepared.RouteProof);CheckCommit(prepared.BerthProof);CheckCommit(prepared.Registration.Route);CheckCommit(prepared.Registration.Berth);ClosePanel();_status.Text="航路与公开泊位已实际登记。仍在船上，请单独预览并确认起航。";
                    }));
                    Button(flow,"预览前往苔灯修院",()=>Execute(OpenFirstVoyagePreview));
                }
                AddRegionalChartActions(flow,view.Ship.DockRegion);
                if(view.Ship.DockRegion=="R6"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))Button(flow,"核对最后起航的实际准备",()=>Execute(OpenChapterSixLaunchPreview));
                foreach(var entity in view.Entities.Where(e=>e.Kind=="vessel_berth"))
                {
                    var berth=Mist.Core.Contracts.StrictJson.Deserialize<VesselBerth>(entity.Json);
                    bool alreadyVisited=berth.Region=="R1"||berth.Region=="R2"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter2")||berth.Region=="R3"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter3")||berth.Region=="R4"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter4")||berth.Region=="R5"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter5")||berth.Region=="R6"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6");
                    if(berth.Region!=view.Ship.DockRegion&&alreadyVisited)
                    {
                        string destination=berth.Region;Button(flow,"预览返回已登记地区 "+destination,()=>Execute(async()=>await OpenRegisteredVoyagePreview(destination)));
                    }
                }
                break;
            case VesselStationKind.Collection:
            case VesselStationKind.Workbench:
                Label(flow,view.Station.Kind==VesselStationKind.Collection?"当前唯一收藏与已装备物品：":"当前持有、装备及已安装模块：",22);
                foreach(var item in view.Items){var definition=_catalog.Definitions.Values.SelectMany(v=>v).FirstOrDefault(d=>d.Id==item.DefinitionId);Label(flow,(definition?.Name??item.DefinitionId)+" · "+item.OwnerKind,18);}
                if(view.Items.Length==0)Label(flow,"当前没有可展示物品。",20);break;
            case VesselStationKind.Roster:
                Label(flow,"当前名册与岗位：",22);foreach(var person in view.Roster)Label(flow,HelperName(person.Entry.Actor)+" · "+person.Entry.Membership+" · "+person.Entry.Availability,18);if(view.Roster.Length==0)Label(flow,"尚无已登记同行者。",20);break;
            case VesselStationKind.WorkOrders:
                Label(flow,"当前工程清单与实际工作记录：",22);foreach(var entity in view.Entities)Label(flow,entity.Id,18);if(view.Entities.Length==0)Label(flow,"尚无当前工程工作单。",20);Button(flow,"查看工作与准备详情",()=>Execute(OpenWorkOrders));break;
        }
        return true;
    }
    private async ValueTask OpenRegisteredVoyagePreview(string destination)
    {
        var snapshot=await _store.SnapshotAsync();var flow=Panel("已登记地区航行预览",InputOwner.Confirmation);
        Label(flow,"目的地区："+destination+"。起航将再次核对当前单船、泊位、已开放航路和真实航图站位。",22);
        Button(flow,"明确按已登记航路返回 "+destination,()=>Execute(async()=>
        {
            if((await _store.SnapshotAsync()).Sequence!=snapshot.Sequence)throw new InvalidOperationException("当前世界已改变，请重新核对起航。");
            CheckCommit(await new TravelService(_store,_catalog,ActiveGeometry).SailAsync(destination));CloseAllPanels();await _store.CreateCheckpointAsync("automatic");_status.Text="已按实际登记航路抵达原地区泊位。";
        }));
    }
    private async ValueTask OpenFirstVoyagePreview()
    {
        if(_voyage is null||_chapterTwo is null)throw new InvalidOperationException("目的地航路服务未加载。");
        var snapshot=await _store.SnapshotAsync();var flow=Panel("跨区起航预览",InputOwner.Confirmation);
        Label(flow,"目的地：苔灯修院公开接驳站。起航使用同一微光号；登陆不等于地方登记或档案许可。",22);
        bool registered=snapshot.Entities.Any(e=>e.Id=="sea-route:R1:R2")&&snapshot.Entities.Any(e=>e.Id=="berth:R2");
        if(!registered){Label(flow,"请先在实际航图工位核对并登记航路与目的泊位。",20);return;}
        Button(flow,"明确起航并在修院公开泊位登陆",()=>Execute(async()=>
        {
            if((await _store.SnapshotAsync()).Sequence!=snapshot.Sequence)throw new InvalidOperationException("当前世界已改变，请关闭旧预览后重新核对起航。");
            CheckCommit(await _voyage.SailAsync("R2"));CheckCommit(await _voyage.RecordArrivalAsync());await _chapterTwo.InitializeRegionAsync();CloseAllPanels();await _store.CreateCheckpointAsync("automatic");_status.Text="已实际航行至修院公开泊位。请走近登陆登记工位，分别办理当地接待。";
        }));
    }
    private async ValueTask OpenVesselBerth()
    {
        if(_vessel is null)return;var flow=Panel("微光号接应泊位",InputOwner.Workspace);
        Label(flow,"移交已完成。登船前检查当前船实例与泊位，登记甲板、档案室、检修舱；检查不会增加奖励。",22);
        Button(flow,"检查泊位并登记三个空间",()=>Execute(async()=>
        {
            var travel=new TravelService(_store,_catalog,_vessel.Geometry);var berth=await _vessel.InspectBerthAsync();CheckCommit(berth);
            if(berth.Receipt is null)throw new InvalidOperationException("泊位检查缺少可靠回执。");CheckCommit(await travel.RegisterBerthAsync(berth.Receipt.Id.Value.ToString()));
            foreach(string scene in VesselInteriorRules.Spaces){var space=await _vessel.PublishSpaceAsync(scene);CheckCommit(space);if(space.Receipt is null)throw new InvalidOperationException("空间检查缺少可靠回执。");CheckCommit(await travel.RegisterShipSpaceAsync(space.Receipt.Id.Value.ToString()));}
            ClosePanel();await OpenVesselPreview(await _vessel.PreviewBoardAsync(),"微光号登船预览","明确登上微光号");
        }));
        Button(flow,"核对当前登记并预览登船",()=>Execute(async()=>await OpenVesselPreview(await _vessel.PreviewBoardAsync(),"微光号登船预览","明确登上微光号")));
    }
    private ValueTask OpenVesselPreview(VesselInteriorPreview preview,string title,string action)
    {
        var flow=Panel(title,InputOwner.Confirmation);Label(flow,preview.Description,22);foreach(string issue in preview.Issues)Label(flow,issue,20);
        if(preview.Ready)
        {
            if(!_vesselUiPreviews.TryGetValue(title,out var stack)){stack=new();_vesselUiPreviews[title]=stack;}stack.Push(preview.Token);
            Button(flow,action,()=>Execute(async()=>{CheckCommit(await _vessel!.ConfirmPreviewAsync(preview.Token));CloseAllPanels();await _store.CreateCheckpointAsync("automatic");_status.Text="已按当前合法泊位和船内出口完成通行。";}));
        }
        return ValueTask.CompletedTask;
    }
}
