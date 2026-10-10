using Godot;
using Mist.Application.Contracts;
using Mist.Application.Voyage;
using Mist.Application.UI;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Voyage;
using System.Collections.Immutable;

public partial class Root
{
    private Mist.Application.ChapterThree.ChapterThreeService? _chapterThree;
    private Mist.Application.ChapterFour.ChapterFourService? _chapterFour;
    private Mist.Application.ChapterFive.ChapterFiveService? _chapterFive;
    private Mist.Application.ChapterSix.ChapterSixService? _chapterSix;
    private Mist.Application.Finale.FinaleService? _finale;
    private Mist.Application.ChapterSix.FinaleLaunchService? _finaleLaunch;
    private readonly Dictionary<string,RegionalVoyageService> _laterVoyages=new(StringComparer.Ordinal);
    private string ReadableSpotName(Spot spot)=>
        _r1Life?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??
        _finale?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??
        _chapterSix?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??
        _chapterFive?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??
        _chapterFour?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??
        _chapterThree?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??
        _chapterTwo?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??WorldCanvas.DisplayName(spot);
    private string? CurrentPortIssue(Port port)=>
        Mist.Application.OptionalLocalWorks.SideWellObservationService.OwnPort(port.Id)&&_chapterThree is not null?SideWellPortIssue(_view.Snapshot,port):
        _r2Life is not null&&port.Id is "R2-S03.to.R2-O01" or "R2-O01.to.R2-S03"?_r2Life.PortIssue(_view.Snapshot,port):
        _r1Life is not null&&_r1Life.Package.PortGates.ContainsKey(port.Id)?_r1Life.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal)&&_finale is not null?_finale.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal)&&_chapterSix is not null?_chapterSix.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal)&&_chapterFive is not null?_chapterFive.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("R4-",StringComparison.Ordinal)&&_chapterFour is not null?_chapterFour.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("R3-",StringComparison.Ordinal)&&_chapterThree is not null?_chapterThree.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("R2-",StringComparison.Ordinal)&&_chapterTwo is not null?_chapterTwo.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal)?"请在当前船舱的登记出口实际核对":
        _chapter is null?_q.PortIssue(_view,port):_chapter.PortIssue(_view.Snapshot,port);
    private string CurrentWorkRegion()
    {
        if(_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal))
        {
            var ship=_view.Snapshot.Entities.SingleOrDefault(e=>e.Id=="ship:glimmer");
            return ship is null?"R1":StrictJson.Deserialize<Mist.Application.Travel.SharedVessel>(ship.Json).DockRegion;
        }
        return _view.Actor.Scene.Split('-')[0];
    }
    private bool RegionalMaterialId(string id)=>(_r1Life?.Package.Materials.Any(m=>m.Id==id)??false)||(_finale?.Package.Materials.Any(m=>m.Id==id)??false)||(_chapterSix?.Package.Materials.Any(m=>m.Id==id)??false)||(_chapterTwo?.Package.Materials.Any(m=>m.Id==id)??false)||(_chapterThree?.Package.Materials.Any(m=>m.Id==id)??false)||(_chapterFour?.Package.Materials.Any(m=>m.Id==id)??false)||(_chapterFive?.Package.Materials.Any(m=>m.Id==id)??false);
    private void ResetRegionalUiState(){ResetScopedPersonalUiState();ResetOldCityLifeUi();ResetR3ShiftUiState();ResetVesselLifeUiState();ResetLinaWorkUi();ResetSettlementSupportUi();ResetOptionalEncounterUi();ResetFieldRelayRepairUi();ResetFirstApproachUiState();ResetR2LifeUiState();ResetSideWellObservationUi();ResetR1LifeUiState();ResetTessLocalReceiptBridgeUi();ResetChapterFourStudyUi();ResetChapterFourCreditUi();ResetOptionalLocalWorksUiState();ResetChapterTwoUiState();ResetChapterThreeUiState();ResetChapterFourUiState();ResetChapterFiveUiState();ResetChapterSixUiState();ResetFinaleUiState();}
    private void RefreshRegionalHud()
    {
        string region=CurrentWorkRegion();
        if(_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal)||region is not("R3" or "R4" or "R5" or "R6" or "RF"))return;
        string final=region switch{"R3"=>"Q-304","R4"=>"Q-404","R5"=>"Q-504","R6"=>"Q-604",_=>"Q-F04"};
        _location.Text=(region switch{"R3"=>"沉钟矿区","R4"=>"玻璃学城","R5"=>"失名旧都","R6"=>"风暴边境",_=>"第七座灯塔"})+" · "+_world.SceneTitle+" / 内部开发候选";
        _sectionTitle.Text=region switch{"R3"=>"矿区救援与批次核查","R4"=>"学城核验与有限试验","R5"=>"旧城核查、救援与受限归档","R6"=>"测站、护送与六塔实际准备",_=>"指挥交接、核心停机与政策选择"};
        _objective.Text=region=="RF"?(HasReceipt(_view.Snapshot,final)?"最终政策已保存。可查阅各地区实际后果和起航前保护档。":"先完成当前现场核验、接管与核心停机，再明确选择已满足准备条件的政策。"):
            region=="R6"?"测站调查后并行办理护送与六塔准备；准备封存后到微光号海图确认最终起航。":
            HasReceipt(_view.Snapshot,final)?"实际事实交接已完成。回到公开泊位登船，到航图站位核对下一地区航路。":"现场材料、实际处置、正文确认与报告交接分别办理。打开任务查看当前工作与前置。";
        _details.Text=region switch{"R3"=>"轨道、泵房、仪表、护送和塔站各自检查。完成救援后继续核对批次与受限供给。","R4"=>"实际样本接收、调查比较、试验准备、执行、观察与验收分别登记；失败历史保留。","R5"=>"现行通行、释放护送、分项责任、保管范围与归档塔分别核对。历史记录和个人责任保留。","R6"=>"工程在各塔原工位实际执行；私人资源、邀请与共同协议各自核验。",_=>"三阶段核心操作按当前配置复核；停机结果与最终政策分别确认。"};
    }

    private void LoadRegionalChapters()
    {
        using var three=Godot.FileAccess.Open("res://content/chapter_three/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        using var four=Godot.FileAccess.Open("res://content/chapter_four/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        using var six=Godot.FileAccess.Open("res://content/chapter_six/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        using var finale=Godot.FileAccess.Open("res://content/finale/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        using var five=Godot.FileAccess.Open("res://content/chapter_five/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        if(three is null||four is null||five is null||six is null||finale is null)throw new InvalidDataException("区域现场内容缺失；请修复内容安装，当前档案保留。");
        _chapterThree=new(_store,_catalog,Mist.Core.ChapterThree.ChapterThreePackage.ParseJson(three.GetAsText()));
        _chapterFour=new(_store,_catalog,Mist.Core.ChapterFour.ChapterFourPackage.ParseJson(four.GetAsText()));
        _chapterFive=new(_store,_catalog,Mist.Core.ChapterFive.ChapterFivePackage.ParseJson(five.GetAsText()));
        var worksites=_chapter!.Geometry.SetItems(_chapterTwo!.Geometry).SetItems(_chapterThree.Geometry).SetItems(_chapterFour.Geometry).SetItems(_chapterFive.Geometry);
        _chapterSix=new(_store,_catalog,Mist.Core.ChapterSix.ChapterSixPackage.ParseJson(six.GetAsText()),worksites,_store);
        _finale=new(_store,_catalog,Mist.Core.Finale.FinalePackage.ParseJson(finale.GetAsText()),_store);
    }

    private ImmutableDictionary<string,SceneGeometry> MergeRegionalGeometry(ImmutableDictionary<string,SceneGeometry> geometry)
        =>geometry.SetItems(_chapterThree!.Geometry).SetItems(_chapterFour!.Geometry).SetItems(_chapterFive!.Geometry).SetItems(_chapterSix!.Geometry).SetItems(_finale!.Geometry);

    private void LoadRegionalVoyages()
    {
        _laterVoyages.Clear();
        foreach(string segment in new[]{"r2_r3","r3_r4","r4_r5","r5_r6"})
        {
            using var source=Godot.FileAccess.Open("res://content/voyage/"+segment+"_voyage.json",Godot.FileAccess.ModeFlags.Read);
            if(source is null)throw new InvalidDataException("区域航行内容缺失："+segment+"。当前档案保留。");
            var definition=StrictJson.Deserialize<RegionalVoyageDefinition>(source.GetAsText());
            _laterVoyages.Add(definition.From,new(_store,_catalog,definition,_vessel!.Geometry));
        }
    }

    private void AddRegionalChartActions(VBoxContainer flow,string region)
    {
        if(!_laterVoyages.TryGetValue(region,out var voyage))return;
        string next=voyage.Definition.To;
        if(!HasReceipt(_view.Snapshot,voyage.Definition.SourceQuest))return;
        Label(flow,"已完成的实际交接允许申请 "+next+" 公开泊位。当地档案许可、现场工作和任务交接分别办理。",20);
        Button(flow,"核对并登记 "+next+" 航路与公开泊位",()=>Execute(async()=>
        {
            var prepared=await voyage.VerifyAndRegisterAsync();CheckCommit(prepared.Handoff);CheckCommit(prepared.RouteProof);CheckCommit(prepared.BerthProof);CheckCommit(prepared.Registration.Route);CheckCommit(prepared.Registration.Berth);
            ClosePanel();_status.Text="公开航路和泊位已登记。仍在船上，请明确确认起航。";
        }));
        Button(flow,"预览前往 "+next+" 公开泊位",()=>Execute(async()=>await OpenRegionalVoyagePreview(voyage)));
    }

    private async ValueTask OpenRegionalVoyagePreview(RegionalVoyageService voyage)
    {
        var before=await _store.SnapshotAsync();var d=voyage.Definition;var flow=Panel("区域起航预览 · "+d.To,InputOwner.Confirmation);
        Label(flow,"目的地区："+d.To+"。使用当前微光号；起航后仅在公开泊位登陆。",22);
        if(!before.Entities.Any(e=>e.Id=="sea-route:"+d.From+":"+d.To)||!before.Entities.Any(e=>e.Id=="berth:"+d.To)){Label(flow,"请先到实际航图站位核对并登记这段航路。",20);return;}
        Button(flow,"明确起航并在 "+d.To+" 公开泊位登陆",()=>Execute(async()=>
        {
            if((await _store.SnapshotAsync()).Sequence!=before.Sequence)throw new InvalidOperationException("当前世界已改变，请重新核对起航。");
            CheckCommit(await voyage.SailAsync(d.To));CheckCommit(await voyage.RecordArrivalAsync());
            if(d.To=="R3")await _chapterThree!.InitializeRegionAsync();
            else if(d.To=="R4")await _chapterFour!.InitializeRegionAsync();
            else if(d.To=="R5")await _chapterFive!.InitializeRegionAsync();
            else if(d.To=="R6")await _chapterSix!.InitializeRegionAsync();
            else throw new InvalidOperationException("目的地区的实际登陆服务尚未加载。");
            CloseAllPanels();await _store.CreateCheckpointAsync("automatic");_status.Text="已实际抵达 "+d.To+" 公开泊位。请走近当地登记站位办理接待。";
        }));
    }
}
