using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.ChapterOne;
using Mist.Core.Combat;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;

namespace Mist.Application.ChapterOne;

public sealed record ChapterOnePreview(Guid Token,bool Ready,string[] Issues,string Description);
public sealed partial class ChapterOneService(IAdventureRepository repository,RuntimeCatalog catalog,ChapterOnePackage package,
    ImmutableDictionary<string,SceneGeometry>? sharedGeometry=null,Func<string,ValueTask<string>>? protectEntry=null)
{
    public const string FindingDraft="Q-103:accident-finding";
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public ChapterOnePackage Package {get;}=Validated(package);
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}=Merge(repository,catalog,package,sharedGeometry);
    private static ChapterOnePackage Validated(ChapterOnePackage p){ChapterOneRules.Validate(p);return p;}
    private static ImmutableDictionary<string,SceneGeometry> Merge(IAdventureRepository r,RuntimeCatalog c,ChapterOnePackage p,ImmutableDictionary<string,SceneGeometry>? supplied)
    {
        ChapterOneRules.Validate(p);var scenes=new Mist.Application.Q102.Q102Service(r,c).Geometry.ToBuilder();
        if(supplied is not null)foreach(var scene in supplied.Values)
        {
            if(scenes.TryGetValue(scene.Id,out var old))scenes[scene.Id]=scene with{Ports=old.Ports.Concat(scene.Ports).DistinctBy(x=>x.Id).ToImmutableArray(),Spots=old.Spots.Concat(scene.Spots).DistinctBy(x=>x.Id).ToImmutableArray()};else scenes[scene.Id]=scene;
        }
        foreach(var scene in p.Scenes)scenes[scene.Id]=scene;
        if(scenes.TryGetValue("R1-S01",out var dock)&&!dock.Spots.Any(x=>x.Id=="R1-S01.glimmer_handover"))scenes[dock.Id]=dock with{Spots=dock.Spots.Add(new("R1-S01.glimmer_handover",new(68,62),"巡灯船实际使用权与跨区委托移交"))};
        return scenes.ToImmutable();
    }
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("角色现场未加载。");
    private static QuestState? Quest(BranchSnapshot s,string id)=>BranchOperations.Read<QuestState>(s,"quest:"+id);
    private static string Hash(string body)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    private void At(BranchSnapshot s,string spot)
    {
        var actor=Actor(s);if(!Geometry.TryGetValue(actor.Scene,out var scene)||scene.Spots.SingleOrDefault(x=>x.Id==spot) is not{} work||actor.Position.Distance(work.Stand)>2.5)throw new InvalidOperationException("请先走到该材料或工作现场的登记站位。");
    }
    private static void Permitted(BranchSnapshot s,string id)
    {
        if(!Allowed(s,id))throw new InvalidOperationException("当前实际工作授权尚未取得或已经不可用。");
    }
    private static bool Allowed(BranchSnapshot s,string id)
    {var e=BranchOperations.Entity(s,id);if(e is null)return false;using var d=System.Text.Json.JsonDocument.Parse(e.Json);return d.RootElement.GetProperty("allowed").GetBoolean();}
    private static bool Acquired(BranchSnapshot s,Mist.Core.Campaign.CampaignMaterial m)=>BranchOperations.Read<ChapterMaterialAccess>(s,"material:"+m.Id) is{} a&&a.Version==m.Version&&a.BodySha256==Hash(m.English);
    private async ValueTask<CommandResult> Commit(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,string[] reads,object result,ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default,DraftStamp? draft=null)
        =>await repository.ExecuteAsync(BranchOperations.Plan(repository,s,key,action,changes,reads,result,items,balances,draft:draft));
    public async ValueTask<BranchSnapshot> SnapshotAsync()=>await repository.SnapshotAsync();
    public async ValueTask MoveAsync(ImmutableArray<Point2> path)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);if(path.IsDefaultOrEmpty)return;var actor=Actor(s);var g=Geometry[actor.Scene];var nav=new NavigationGrid(g);var from=actor.Position;
        foreach(var p in path){if(!nav.SegmentClear(from,p,actor.Scene=="R1-S06"&&BranchOperations.Read<WorkshopState>(s,"workshop") is not{FrontSafe:true}))throw new InvalidOperationException("道路有墙体或尚未处理的危险。");from=p;}
        var result=await Commit(s,"chapter-move:"+Guid.NewGuid(),"move",[BranchOperations.Change(s,"actor","actor",actor with{Position=path[^1]})],["actor","workshop"],new{scope=ChapterOneRules.Scope});if(result.Status!=CommitStatus.Committed)throw new InvalidOperationException("现场位置未提交。");
    }
    public string? PortIssue(BranchSnapshot s,Port port)
    {
        if(!Geometry.ContainsKey(port.TargetScene))return "当前候选未制作该地点的实际碰撞与入口。";
        var actor=Actor(s);if((actor.Scene=="R1-S08"||port.TargetScene=="R1-S08")&&!Allowed(s,"permission:Q-103"))return "请先取得当前有效的记录调阅委托。";
        string edge=Travel.TravelService.ConnectionKey(actor.Scene,port.TargetScene);var workshop=BranchOperations.Read<WorkshopState>(s,"workshop");
        if((actor.Scene=="R1-S04"||port.TargetScene=="R1-S04")&&BranchOperations.Read<FerryState>(s,"ferry") is not{Docked:true})return "民用渡轮已经离港，舷梯关闭。";
        if(edge==Travel.TravelService.ConnectionKey("R1-S02","R1-S06")&&workshop is not{LatchOpen:true})return "工坊内侧横闩尚未打开。";
        if(edge==Travel.TravelService.ConnectionKey("R1-S07","R1-S06")&&workshop is not{SideOpen:true})return "工坊侧门联锁仍关闭。";
        if(edge==Travel.TravelService.ConnectionKey("R1-S05","R1-S06")&&workshop is not{FrontSafe:true})return "工坊正面扫描仍有危险，需先处理对应遭遇或本地隔离。";
        if(port.TargetScene=="R1-S10")
        {
            if(!Allowed(s,"permission:Q-104"))return "当前塔站现场工作许可尚未取得。";
            if(BranchOperations.Read<TidegateLocalState>(s,"tower:local") is not{Prepared:true})return "先在塔基实际检查当前局部供给。";
            if(!Acquired(s,Package.Materials.Single(m=>m.Id=="Q104-R-CONTROLS")))return "请先查看两阀与隔离的现场说明。";
        }
        return null;
    }
    public async ValueTask<CommandResult> EnterPortAsync(string portId)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var actor=Actor(s);var port=Geometry[actor.Scene].Ports.Single(p=>p.Id==portId);if(actor.Position.Distance(port.Stand)>2.5)throw new InvalidOperationException("请先走到出口站位。");string? issue=PortIssue(s,port);if(issue is not null)throw new InvalidOperationException(issue);
        var target=Geometry[port.TargetScene].Ports.SingleOrDefault(p=>p.Id==port.TargetPort&&p.TargetScene==actor.Scene&&p.TargetPort==port.Id)??throw new InvalidOperationException("配对入口尚未制作，保留原现场位置。");if(!new NavigationGrid(Geometry[port.TargetScene]).CanStand(target.Stand))throw new InvalidOperationException("目标落点不可站立。");
        return await Commit(s,"chapter-exit:"+Guid.NewGuid(),"scene_transition",[BranchOperations.Change(s,"actor","actor",actor with{Scene=port.TargetScene,Position=target.Stand,ActionClock=checked(actor.ActionClock+1)})],["actor","permission:Q-103","permission:Q-104","tower:local","workshop","ferry"],new{from=actor.Scene,to=port.TargetScene,from_port=port.Id,to_port=target.Id,scope=ChapterOneRules.Scope});
    }
    public async ValueTask<CommandResult> AcceptInvestigationAsync()
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-S03.havel");if(!BranchOperations.Receipt(s,"Q-101"))throw new InvalidOperationException("请先完成港口合法检修委托。");if(Quest(s,"Q-103") is{Lifecycle:QuestLifecycle.Active or QuestLifecycle.Closed})return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Commit(s,"Q103:investigation:accepted","accept_accident_investigation",[BranchOperations.Change(s,"quest:Q-103","quest",new QuestState("Q-103",Guid.NewGuid(),QuestLifecycle.Active,null)),BranchOperations.Change(s,"permission:Q-103","permission",new{allowed=true,scope="original_harbour_records",issued_by="N-HAVEL"})],["actor","quest:Q-103","permission:Q-103"],new{commission="Q-101",records_only=true});
    }
    public async ValueTask<CommandResult> CollectAsync(string materialId)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var m=Package.Materials.Single(x=>x.Id==materialId);At(s,m.Spot);Permitted(s,m.Id.StartsWith("Q103",StringComparison.Ordinal)?"permission:Q-103":"permission:Q-104");
        if(m.Id=="Q103-R-CLOCK")
        {
            var original=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="material:QP03-R-RECORDER:acquired"&&r.ActionType=="collect_coast_record")??throw new InvalidOperationException("请先取得实际海岸独立记录器，档案封套不能凭空生成原件。");using var d=System.Text.Json.JsonDocument.Parse(original.ResultJson);if(d.RootElement.GetProperty("source_root").GetString()!="coast-independent-recorder")throw new InvalidOperationException("独立记录器来源不符。");
        }
        if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Commit(s,"material:"+m.Id+":"+m.Version+":acquired","collect_chapter_one_record",[BranchOperations.Change(s,"material:"+m.Id,"material_access",new ChapterMaterialAccess(m.Id,m.Version,Hash(m.English),m.Id=="Q103-R-CLOCK"?"coast-independent-recorder":"harbour-original:"+m.Id))],["actor","permission:Q-103","permission:Q-104","material:"+m.Id,"material:QP03-R-RECORDER"],new{material=m.Id,version=m.Version,current_mara_status="unknown",language_assessment="not_evaluated"});
    }
    public async ValueTask<CommandResult> ConfirmFindingMeaningAsync(AccidentFindingMeaning meaning,bool structuredAssistance)
    {
        var s=await repository.SnapshotAsync();var draft=await repository.GetDraftAsync(FindingDraft);if(!structuredAssistance&&(draft is null||draft.Deleted||string.IsNullOrWhiteSpace(draft.Body)))throw new InvalidOperationException("空白正文需明确使用结构化辅助，不能计作独立英文表达。");
        if(Quest(s,"Q-103") is not{Lifecycle:QuestLifecycle.Active})throw new InvalidOperationException("事故核查委托尚未接受或已经结算。");var old=BranchOperations.Read<AccidentProposal>(s,"proposal:Q-103");var p=new AccidentProposal((old?.Version??0)+1,true,draft?.Hash,structuredAssistance,meaning);
        return await Commit(s,"Q103:meaning:"+Guid.NewGuid(),"confirm_accident_finding_meaning",[BranchOperations.Change(s,"proposal:Q-103","proposal",p)],["proposal:Q-103","quest:Q-103"],new{structured_assistance=structuredAssistance,free_english_understanding="not_claimed"},draft:new(FindingDraft,draft?.Id,draft?.Revision??0,draft?.Hash));
    }
    public async ValueTask<ChapterOnePreview> PreviewFindingAsync()
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var draft=await repository.GetDraftAsync(FindingDraft);var p=BranchOperations.Read<AccidentProposal>(s,"proposal:Q-103");var issues=new List<string>();
        if(!BranchOperations.Receipt(s,"Q-102"))issues.Add("先完成实际渡轮撤离，再结合撤离后的记录结算。");if(!Allowed(s,"permission:Q-103"))issues.Add("当前记录核验许可不可用。");if(Quest(s,"Q-103") is not{Lifecycle:QuestLifecycle.Active})issues.Add("委托尚未接受或已经结算。");if(ChapterOneRules.Evidence.Any(id=>!Acquired(s,Package.Materials.Single(m=>m.Id==id))))issues.Add("四种原始来源及版本尚未齐备。");if(p is not{Confirmed:true}||p.DraftHash!=draft?.Hash||!ChapterOneRules.Admissible(p.Meaning))issues.Add("请确认有证据支持的结论及其限度；正文改动需要重新确认。");try{At(s,"R1-S03.havel");}catch(InvalidOperationException){issues.Add("请回到哈文的实际核查站位提交。");}
        if(issues.Count>0)return new(Guid.Empty,false,issues.ToArray(),"事故结论尚不可提交。");var quest=Quest(s,"Q-103")!;var reward=catalog.Quests.Single(q=>q.Id=="Q-103").Reward.Bundle;var cmd=BranchOperations.Plan(repository,s,"Q-103:resolution","record_planned_interruption_finding",[BranchOperations.Change(s,"quest:Q-103","quest",quest.Close(QuestOutcome.Completed)),BranchOperations.Change(s,"finding:E-01","verified_finding",new{planned_test=true,second_check_removed=true,notice_alone_insufficient=true,higher_intent="not_established",current_mara_status="unknown",source_materials=ChapterOneRules.Evidence})],new[]{"actor","quest:Q-103","proposal:Q-103","permission:Q-103"}.Concat(ChapterOneRules.Evidence.Select(id=>"material:"+id)).ToArray(),new{fact="E-01",scope="planned_interruption_with_omitted_protection",current_mara_status="unknown"},balances:[new("experience",reward.Experience),new("marks",reward.Marks),new("parts",reward.Parts)],draft:new(FindingDraft,draft?.Id,draft?.Revision??0,draft?.Hash));Guid token=Guid.NewGuid();_previews[token]=cmd;return new(token,true,[],"提交断供测试的核验结论与局限，结算本次核查奖励。");
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?repository.ExecuteAsync(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed","chapter-one","预览已关闭，请重新核对。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}
