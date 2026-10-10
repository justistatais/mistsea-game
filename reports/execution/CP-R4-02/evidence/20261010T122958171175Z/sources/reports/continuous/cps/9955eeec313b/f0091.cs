using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Devices;

namespace Mist.Application.ChapterFour;

public sealed record ExhibitionCreditView(BranchSnapshot Snapshot,ActorState Actor,ExhibitionCreditState? State,
    ImmutableArray<ExhibitionCreditMaterial> Materials);
public sealed record ExhibitionCreditPreview(Guid Token,ExhibitionCreditOperation Operation,bool Ready,
    ImmutableArray<RuleIssue> Issues,string? PublicText);

// Optional local work. No main quest, ending permission, language assessment or reward balance is changed.
public sealed partial class ExhibitionCreditService
{
    public const string StateId="R4:optional-credit",CardId="R4-EXHIBITION-CARD-ONE";
    private const string PermitId="R4:exhibition:permit",ComparisonId="R4:exhibition:comparison",ProposalId="R4:exhibition:proposal",PrintedId="R4:exhibition:printed-card";
    private readonly IAdventureRepository _repository;
    private readonly ChapterFourService _chapter;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public ExhibitionCreditPackage Package {get;}
    public ExhibitionCreditService(IAdventureRepository repository,ChapterFourService chapter,ExhibitionCreditPackage package)
    {ExhibitionCreditPackage.Validate(package);_repository=repository;_chapter=chapter;Package=package;}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"当前前景事件尚未交接。");
        Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<Mist.Application.Travel.TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"当前运输尚未交接。");
        Require(!s.Receipts.Any(r=>r.SemanticKey=="Q-604:resolution"||r.ActionType=="network_finale_departure"),"终章出发后不再执行新的学城可选工作。");
        var main=BranchOperations.Read<ChapterFourState>(s,ChapterFourService.StateId);
        Require(main is not null&&main.ContentVersion==_chapter.Package.Version&&s.Receipts.Any(r=>r.SemanticKey=="R4:initialize:"+_chapter.Package.Version&&r.ActionType=="glasshaven_region_initialized"),"需要本存档真实学城到达与初始化来源。");
    }
    private async ValueTask At(BranchSnapshot s,string spot)
    {
        var a=BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("实际角色位置缺失。");
        var geo=await _chapter.CurrentGeometryAsync();
        Require(a.Scene=="R4-O02"&&geo.TryGetValue(a.Scene,out var scene)&&scene.Spots.Any(p=>p.Id==spot&&p.Stand.Distance(a.Position)<=2.5)&&new NavigationGrid(scene).CanStand(a.Position,true),"请沿现有合法出口走到贡献展示室的真实操作站位。");
    }
    private bool Acquired(BranchSnapshot s,ExhibitionCreditMaterial m)=>BranchOperations.Read<ExhibitionCreditAccess>(s,"material:"+m.Id) is{} a&&a.Id==m.Id&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot;
    private bool AllSources(BranchSnapshot s)=>Package.Materials.All(m=>Acquired(s,m));
    private ImmutableDictionary<string,string> Versions()=>Package.Materials.ToImmutableDictionary(m=>m.Id,m=>m.Version+":"+m.Fingerprint);
    private ExhibitionCreditState State(BranchSnapshot s)=>BranchOperations.Read<ExhibitionCreditState>(s,StateId) is{} st&&st.ContentVersion==Package.Version?st:throw new InvalidOperationException("尚未实际领取这个有范围的展签工作。");
    private ExhibitionRevisionPermit Permit(BranchSnapshot s)
    {
        Require(AllSources(s),"当前公共原文取得许可或版本已改变，不能沿用旧工作来源。");
        var permit=BranchOperations.Read<ExhibitionRevisionPermit>(s,PermitId)??throw new InvalidOperationException("实际修订许可缺失。");
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="R4:exhibition:permit:"+Package.Version&&r.ActionType=="exhibition_revision_permit_issued");
        Require(receipt is not null&&StrictJson.Serialize(permit)==receipt.ResultJson&&permit.Instance==State(s).Instance&&permit.Version==Package.PermitVersion&&permit.Grantor==Package.Recipient&&
            permit.Recipient=="patrol_technician"&&permit.Purpose=="public_contribution_revision"&&permit.Fields.SequenceEqual(new[]{"construction_role","calibration_role_count","presentation_role"})&&
            permit.SourceVersions.Count==Versions().Count&&permit.SourceVersions.All(x=>Versions().GetValueOrDefault(x.Key)==x.Value),"当前展签许可范围、工作实例或来源版本不符。");return permit;
    }
    private string[] Reads(BranchSnapshot s)=>new[]{"actor",ChapterFourService.StateId,StateId,PermitId,ComparisonId,ProposalId,PrintedId,"R4:exhibition:accepted-label","R4:gate:public_exhibition"}
        .Concat(Package.Materials.Select(m=>"material:"+m.Id)).Concat(s.Entities.Where(e=>e.Kind is "event" or "transport_segment").Select(e=>e.Id)).ToArray();
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command)
    {
        var session=_repository.Session;
        if(command.Context.Profile!=session.Profile||command.Context.Branch!=session.Branch||command.Context.Load!=session.Load||command.Context.ReleaseId!=session.ReleaseId)
            return new(CommitStatus.Rejected,null,[new("stale_load",StateId,"分支或加载世代已改变，请重新预览。")]);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("展签工作需要原子前景占用接口。");
        string owner="r4-exhibition:"+command.Context.Command.Value;
        Require(await leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground","role:R4-exhibition-organiser"]),"其他前景工作占用展签岗位，请稍后重试。");
        try{return await _repository.ExecuteAsync(command);}finally{await leases.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private ValueTask<CommandResult> Commit(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object payload,ImmutableArray<ItemChange> items=default,DraftStamp? draft=null)
        =>Execute(BranchOperations.Plan(_repository,s,key,action,changes,Reads(s),payload,items,draft:draft));
    public async ValueTask<ExhibitionCreditView> ViewAsync()
    {
        var v=await _chapter.ViewAsync();var state=BranchOperations.Read<ExhibitionCreditState>(v.Snapshot,StateId);
        if(state is not null&&state.ContentVersion!=Package.Version)throw new InvalidOperationException("展签工作内容版本不符，保留原稿。");
        return new(v.Snapshot,v.Actor,state,Package.Materials.Where(m=>Acquired(v.Snapshot,m)).ToImmutableArray());
    }
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var m=Package.Materials.Single(m=>m.Id==id);await At(s,m.Spot);
        if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Commit(s,"material:"+id+":acquired","exhibition_public_source_acquired",[BranchOperations.Change(s,"material:"+id,"material_access",new ExhibitionCreditAccess(id,m.Version,m.Fingerprint,m.SourceRoot))],new{material=id,version=m.Version,fingerprint=m.Fingerprint,private_identities_included=false});
    }
    public async ValueTask<ExhibitionCreditMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("只可打开实际取得且版本匹配的公共原文。");
    public async ValueTask<CommandResult> AcceptCommissionAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-O02.revision");Require(AllSources(s),"先在原展位与工作台分别取得展签、贡献簿及修订许可说明。");
        if(BranchOperations.Entity(s,StateId) is not null){Permit(s);return new(CommitStatus.AlreadyCommitted,null,[]);}
        Require(!s.Items.Any(i=>i.Id==CardId),"本工作唯一空白卡片已存在，不能补造。");
        var state=new ExhibitionCreditState(Guid.NewGuid(),Package.Version,ExhibitionCardStage.DeskStock);
        var permit=new ExhibitionRevisionPermit(state.Instance,Package.PermitVersion,Package.Recipient,"patrol_technician","public_contribution_revision",["construction_role","calibration_role_count","presentation_role"],Versions());
        return await Commit(s,"R4:exhibition:permit:"+Package.Version,"exhibition_revision_permit_issued",
            [BranchOperations.Change(s,StateId,"exhibition_credit_state",state),BranchOperations.Change(s,PermitId,"exhibition_revision_permit",permit)],permit,
            [new(CardId,Package.CardDefinition,"new","","warehouse","R4-O02.revision",null)]);
    }
    public async ValueTask<CommandResult> TakeCardAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-O02.revision");Permit(s);var st=State(s);
        if(st.Stage!=ExhibitionCardStage.DeskStock)return new(CommitStatus.AlreadyCommitted,null,[]);
        Require(s.Items.Any(i=>i.Id==CardId&&i.DefinitionId==Package.CardDefinition&&i.OwnerKind=="warehouse"&&i.OwnerId=="R4-O02.revision"),"工作台的本份空白卡片缺失。");
        return await Commit(s,"R4:exhibition:card-collected:"+st.Instance,"exhibition_blank_card_collected",[BranchOperations.Change(s,StateId,"exhibition_credit_state",st with{Stage=ExhibitionCardStage.PlayerBlank})],new{card=CardId,work_stock_not_reward=true},[new(CardId,Package.CardDefinition,"warehouse","R4-O02.revision","player","hero",null)]);
    }
    public async ValueTask<CommandResult> CompareAsync(ExhibitionContributionComparison comparison)
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-O02.revision");Permit(s);var st=State(s);Require(st.Stage!=ExhibitionCardStage.Received,"已接收展签的来源保留，不重写历史。");Require(AllSources(s),"当前三个公共来源缺失。");
        Require(comparison.Construction=="workshop_team"&&comparison.Calibration=="student_reference_checks"&&comparison.Calibrators==2&&comparison.Presentation=="presenter"&&!comparison.SpeakerOnlyContributor&&!comparison.ChangesTrialOutput,"保留贡献簿三种实际岗位；介绍人不是唯一贡献者，展签不得改写试验输出。");
        return await Commit(s,"R4:exhibition:compare:"+Guid.NewGuid(),"exhibition_contributions_compared",[BranchOperations.Change(s,StateId,"exhibition_credit_state",st with{Compared=true}),BranchOperations.Change(s,ComparisonId,"exhibition_contribution_comparison",comparison)],comparison);
    }
    public ValueTask<DraftSaved> SaveDraftAsync(long expectedRevision,DraftId? id,string body)
    {var session=_repository.Session;return _repository.SaveAsync(new(session.Profile,session.Branch,session.Load,Package.DraftTask,id,expectedRevision,body,_chapter.Package.Version));}
    public async ValueTask<CommandResult> ConfirmMeaningAsync(ExhibitionCreditMeaning meaning,bool structuredAssistance)
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-O02.revision");Permit(s);Require(State(s).Stage!=ExhibitionCardStage.Received,"实际已接收工作不重写。");
        var draft=await _repository.GetDraftAsync(Package.DraftTask);Require(structuredAssistance||draft is{Deleted:false}&&!string.IsNullOrWhiteSpace(draft.Body),"空白正文须明确选择结构化辅助。");
        return await Commit(s,"R4:exhibition:proposal:"+Guid.NewGuid(),"exhibition_credit_meaning_confirmed",[BranchOperations.Change(s,ProposalId,"exhibition_credit_proposal",new ExhibitionCreditProposal(draft?.Hash,structuredAssistance,meaning))],new{meaning,structured_assistance=structuredAssistance,automatic_english_understanding=false,language_assessment="human_pending"},draft:new(Package.DraftTask,draft?.Id,draft?.Revision??0,draft?.Hash));
    }
    public async ValueTask<CommandResult> MountCardAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-O02.label");Permit(s);var st=State(s);Require(st.Stage==ExhibitionCardStage.Printed,"须先实际制成当前提议卡片；已挂或已接收不会重复安装。");
        var card=BranchOperations.Read<ExhibitionPrintedCard>(s,PrintedId)??throw new InvalidOperationException("实际制作记录缺失。");var draft=await _repository.GetDraftAsync(Package.DraftTask);Require(card.Instance==st.Instance&&card.Revision==st.CardRevision&&card.DraftHash==draft?.Hash,"制成卡片与当前稿件版本不同，请取回工作台重做。");
        Require(s.Items.Any(i=>i.Id==CardId&&i.OwnerKind=="player"&&i.OwnerId=="hero"),"只有持有的本份卡片可以挂到展位。");
        return await Commit(s,"R4:exhibition:mount:"+st.Instance+":"+st.CardRevision,"exhibition_candidate_card_mounted",[BranchOperations.Change(s,StateId,"exhibition_credit_state",st with{Stage=ExhibitionCardStage.Mounted,InspectionRevision=null})],new{card=CardId,revision=st.CardRevision,original_label_retained=true},[new(CardId,Package.CardDefinition,"player","hero","installed","R4-O02.label",null)]);
    }
    public async ValueTask<CommandResult> InspectMountedAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-O02.label");Permit(s);var st=State(s);Require(st.Stage==ExhibitionCardStage.Mounted&&AllSources(s),"须在实际展位核对已挂卡与留存来源。");Require(s.Items.Any(i=>i.Id==CardId&&i.OwnerKind=="installed"&&i.OwnerId=="R4-O02.label"),"本份卡片未实际安装。");
        return await Commit(s,"R4:exhibition:inspect:"+st.Instance+":"+st.CardRevision,"exhibition_mounted_card_checked",[BranchOperations.Change(s,StateId,"exhibition_credit_state",st with{InspectionRevision=st.CardRevision})],new{card=CardId,revision=st.CardRevision,checked_against=Versions(),trial_outputs_changed=false});
    }
    public async ValueTask<CommandResult> ReturnCardToDeskAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Permit(s);Require(st.Stage is ExhibitionCardStage.Printed or ExhibitionCardStage.Mounted,"只可取回本工作未接收的制成卡，不退还已接收展示。");await At(s,st.Stage==ExhibitionCardStage.Mounted?"R4-O02.label":"R4-O02.revision");
        string kind=st.Stage==ExhibitionCardStage.Mounted?"installed":"player",owner=kind=="installed"?"R4-O02.label":"hero";Require(s.Items.Any(i=>i.Id==CardId&&i.OwnerKind==kind&&i.OwnerId==owner),"本份卡片实际归属不符。");
        return await Commit(s,"R4:exhibition:return:"+st.Instance+":"+st.CardRevision,"exhibition_card_returned_for_revision",[BranchOperations.Change(s,StateId,"exhibition_credit_state",st with{Stage=ExhibitionCardStage.PlayerBlank,InspectionRevision=null,CardRevision=checked(st.CardRevision+1)})],new{card=CardId,same_card_reused=true,old_sources_preserved=true},kind=="installed"?[new(CardId,Package.CardDefinition,kind,owner,"player","hero",null)]:[]);
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?Execute(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",StateId,"预览尚未就绪或已关闭。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}
