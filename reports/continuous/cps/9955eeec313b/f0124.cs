using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterTwo;
using Mist.Core.ChapterThree;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.OptionalLocalWorks;

namespace Mist.Application.OptionalLocalWorks;

public sealed record MusicBoxWorkView(BranchSnapshot Snapshot,MusicBoxWorkState? State,ChapterMaterial? Specification);
public sealed record MusicBoxPreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string? DraftHash);
public sealed partial class MusicBoxRepairService
{
    public const string StateId="R3-O01:musicbox-work",Owner="ROLE-R3-MUSICBOX-OWNER",Box="R3-O01:MUSIC-BOX-01",Wheel12="R3-O01:WHEEL-12-01",Wheel14="R3-O01:WHEEL-14-01",Bench="R3-O01:owner-bench",PartsStore="R3-O01:parts",Task="R3-O01-W-REPAIRNOTE",Material="R3-O01-R-MUSIC",Completion="R3-O01:musicbox-repair";
    private readonly IAdventureRepository _repository;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    private readonly SceneGeometry _geometry;
    public ChapterMaterial Specification {get;}
    public ChapterWritingTask WritingTask {get;}
    public MusicBoxRepairService(IAdventureRepository repository,RuntimeCatalog catalog,ChapterThreePackage package)
    {
        ChapterThreePackage.Validate(package);_repository=repository;
        Require(catalog.ReleaseId==repository.Session.ReleaseId,"发布版本与当前档案不一致。");
        Specification=package.Materials.Single(m=>m.Id==Material);WritingTask=package.WritingTasks.Single(t=>t.Id==Task);_geometry=package.Scenes.Single(s=>s.Geometry.Id=="R3-O01").Geometry;
        Require(Specification.Version=="r3-author-candidate-1"&&Specification.Quest=="R3-O01"&&Specification.SourceRoot=="r3-owner-musicbox-repair-note"&&Specification.AccessClass=="individual_permission"&&Specification.Spot=="R3-O01.parts"&&Specification.English.Contains("MBOX-12",StringComparison.Ordinal)&&Specification.English.Contains("fourteen",StringComparison.Ordinal),"音乐盒具体原件合同未绑定，不能猜造规格或许可。");
        Require(WritingTask.Spot=="R3-O01.note"&&new[]{"owner","parts","musicbox","note"}.All(x=>_geometry.Spots.Any(s=>s.Id=="R3-O01."+x&&new NavigationGrid(_geometry).CanStand(s.Stand,true))),"音乐盒真实工作站位缺失。");
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static T? Read<T>(BranchSnapshot s,string id) where T:class=>BranchOperations.Read<T>(s,id);
    private MusicBoxWorkState State(BranchSnapshot s)=>Read<MusicBoxWorkState>(s,StateId) is{} st&&st.ContentVersion==Specification.Version&&st.SourceFingerprint==Specification.Fingerprint?st:throw new InvalidOperationException("音乐盒邀请未建立或原件已变更，请重新核对。");
    private void At(BranchSnapshot s,string suffix)
    {
        var a=Read<ActorState>(s,"actor")??throw new InvalidOperationException("角色现场未加载。");var spot=_geometry.Spots.Single(x=>x.Id=="R3-O01."+suffix);
        Require(a.Scene==_geometry.Id&&a.Position.Distance(spot.Stand)<=2.5&&new NavigationGrid(_geometry).CanStand(a.Position,true),"请先实际走到音乐盒对应安全站位。");
        Require(Read<ChapterThreeState>(s,"chapter3") is{Landing:true} chapter&&chapter.ContentVersion==Specification.Version&&s.Receipts.Any(r=>r.ActionType=="chapter_three_region_initialized"),"必须从实际矿区登陆登记后进入本地修理铺。");
    }
    private static void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"请先交接当前前景事件。");
        Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"请先交接实际护送或货物运输。");
        Require(!BranchOperations.Receipt(s,"Q-604"),"最终起航后外区旁修暂停。");
    }
    private MusicBoxOwnerPermission Permit(BranchSnapshot s)
    {
        var st=State(s);var p=Read<MusicBoxOwnerPermission>(s,StateId+":permission");
        Require(!st.Completed&&st.Invitation=="accept"&&p is not null&&p.WorkInstance==st.Instance&&p.Owner==Owner&&p.Scope=="same_box_mechanism_only"&&p.Version==st.PermissionVersion&&p.ReadSpecification&&p.RepairMechanism&&!p.PublishMemory&&p.SourceFingerprint==Specification.Fingerprint,"主人当前许可未成立或已经撤回；维修许可不包括公开私人回忆。");return p!;
    }
    private bool Acquired(BranchSnapshot s)=>Read<ChapterMaterialAccess>(s,"material:"+Material) is{} m&&m.Id==Material&&m.Version==Specification.Version&&m.TranslationVersion==Specification.TranslationVersion&&m.Fingerprint==Specification.Fingerprint&&m.SourceRoot==Specification.SourceRoot;
    private static StoredItem Item(BranchSnapshot s,string id,string definition,string ownerKind,string ownerId)
    {var item=s.Items.SingleOrDefault(i=>i.Id==id);Require(item is not null&&item.DefinitionId==definition&&item.OwnerKind==ownerKind&&item.OwnerId==ownerId,"原同一实物、规格或当前保管地不符，不能借用另一件或复制奖励。");return item!;}
    private string[] Reads(BranchSnapshot s)=>s.Entities.Select(e=>e.Id).Concat(new[]{StateId,StateId+":permission",StateId+":approval",StateId+":test",StateId+":meaning","material:"+Material}).Distinct().ToArray();
    private CommitPlanCommand Plan(BranchSnapshot s,string action,MusicBoxWorkState st,object result,ImmutableArray<EntityChange> extra=default,ImmutableArray<ItemChange> items=default,string? semantic=null,DraftStamp? draft=null)
        =>BranchOperations.Plan(_repository,s,semantic??StateId+":action:"+Guid.NewGuid(),action,[BranchOperations.Change(s,StateId,"optional_musicbox_work",st),..(extra.IsDefault?ImmutableArray<EntityChange>.Empty:extra)],Reads(s),result,items:items,draft:draft);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand c)
    {
        var session=_repository.Session;if(c.Context.Profile!=session.Profile||c.Context.Branch!=session.Branch||c.Context.Load!=session.Load||c.Context.ReleaseId!=session.ReleaseId)return new(CommitStatus.Rejected,null,[new("stale_load",StateId,"当前读取世代已改变，请重新核对旁修。")]);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("旁修需要实际原子前景占用接口。");string owner="musicbox:"+c.Context.Command.Value;
        Require(await leases.TryLeasesAsync(c.Context.Load,owner,["input:foreground"]),"另一项前景工作尚未交接。");try{return await _repository.ExecuteAsync(c);}finally{await leases.ReleaseLeasesAsync(c.Context.Load,owner);}
    }
    public async ValueTask<MusicBoxWorkView> ViewAsync()
    {var s=await _repository.SnapshotAsync();var st=Read<MusicBoxWorkState>(s,StateId);var p=Read<MusicBoxOwnerPermission>(s,StateId+":permission");return new(s,st,st is not null&&p is{ReadSpecification:true}&&p.Version==st.PermissionVersion&&p.SourceFingerprint==Specification.Fingerprint&&Acquired(s)?Specification:null);}
    public async ValueTask<CommandResult> DecideInvitationAsync(string decision)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"owner");Require(decision is "accept" or "defer" or "decline" or "withdraw","请明确接受、暂缓、拒绝或撤回本次主人邀请。");
        var old=Read<MusicBoxWorkState>(s,StateId);Require(old is null||!old.Completed||decision=="withdraw","同一音乐盒已完成，不以重新邀请复制结算；阅读许可仍可撤回。");if(old is not null)State(s);
        var st=old??new MusicBoxWorkState{ContentVersion=Specification.Version,SourceFingerprint=Specification.Fingerprint,Instance=Guid.NewGuid()};if(st.Invitation==decision)return new(CommitStatus.AlreadyCommitted,null,[]);
        st=st with{Invitation=decision,PermissionVersion=checked(st.PermissionVersion+1),ApprovedPermission=null,TestedConfiguration=null};var permission=new MusicBoxOwnerPermission(st.Instance,Owner,"same_box_mechanism_only",st.PermissionVersion,decision=="accept",decision=="accept",false,Specification.Fingerprint);
        ImmutableArray<ItemChange> items=[];if(old is null&&decision=="accept"||decision=="accept"&&!s.Items.Any(i=>i.Id==Box))
        {Require(!s.Items.Any(i=>i.Id==Box||i.Id==Wheel12||i.Id==Wheel14),"主人维修库存序号已存在且不完整，不能补造。");items=[new(Box,"R3-MUSIC-BOX","new","","warehouse",Owner,null),new(Wheel12,"MBOX-12","new","","warehouse",PartsStore,null),new(Wheel14,"MBOX-14","new","","warehouse",PartsStore,null)];}
        return await Execute(Plan(s,"musicbox_owner_invitation_decided",st,new{owner=Owner,decision,scope=permission.Scope,permission_version=permission.Version,memory_publication=false,rewards_registered=false},[BranchOperations.Change(s,StateId+":permission","private_local_permission",permission)],items));
    }
    public async ValueTask<CommandResult> DiscoverSpecificationAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"parts");Permit(s);if(Acquired(s))return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Execute(Plan(s,"musicbox_specification_acquired",State(s),new{material=Material,source_root=Specification.SourceRoot,version=Specification.Version,memory_publication=false},[BranchOperations.Change(s,"material:"+Material,"material_access",new ChapterMaterialAccess(Material,Specification.Version,Specification.TranslationVersion,Specification.Fingerprint,Specification.SourceRoot,Specification.Spot))],semantic:"material:"+Material+":acquired"));
    }
    public async ValueTask<ChapterMaterial> OpenSpecificationAsync()=>(await ViewAsync()).Specification??throw new InvalidOperationException("原件须实际取得且主人当前阅读许可有效。");
    public async ValueTask<CommandResult> InspectMechanismAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"musicbox");Permit(s);var st=State(s);Require(Acquired(s),"请先实际读取原规格。");Item(s,Box,"R3-MUSIC-BOX",st.Fitted&&!st.Returned?"installed":"warehouse",st.Fitted&&!st.Returned?Bench:Owner);
        return await Execute(Plan(s,"musicbox_mechanism_inspected",st with{Inspected=true},new{box=Box,scope="mechanism_only",private_memory_not_copied=true}));
    }
    public async ValueTask<CommandResult> SelectWheelAsync(string definition,int teeth)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"parts");Permit(s);var st=State(s);Require(st.Inspected&&Acquired(s)&&!st.Fitted,"须先检查同盒机制与原规格；已安装部件不能无操作换件。");Require(definition=="MBOX-12"&&teeth==12,"原盒需要MBOX-12十二齿，MBOX-14十四齿属于另一装置，不能按外观代替。");Item(s,Wheel12,definition,"warehouse",PartsStore);
        return await Execute(Plan(s,"musicbox_wheel_specification_compared",st with{SelectedPart=definition,ApprovedPermission=null},new{selected=definition,required_teeth=12,compared_other="MBOX-14",other_teeth=14,source_fingerprint=Specification.Fingerprint}));
    }
    public async ValueTask<CommandResult> RequestOwnerApprovalAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"owner");Permit(s);var st=State(s);Require(st.Inspected&&Acquired(s)&&st.SelectedPart=="MBOX-12","请先凭实际规格比较向主人提出十二齿件。");Item(s,Wheel12,"MBOX-12",st.Fitted?"installed":"warehouse",st.Fitted?Box:PartsStore);
        var approval=new MusicBoxApproval(st.Instance,Owner,"MBOX-12",Wheel12,st.PermissionVersion,Specification.Fingerprint);
        return await Execute(Plan(s,"musicbox_owner_part_approved",st with{ApprovedPermission=st.PermissionVersion},approval,[BranchOperations.Change(s,StateId+":approval","local_part_approval",approval)]));
    }
    private MusicBoxApproval Approval(BranchSnapshot s,MusicBoxWorkState st)
    {var approval=Read<MusicBoxApproval>(s,StateId+":approval");Require(st.ApprovedPermission==st.PermissionVersion&&approval is not null&&approval.WorkInstance==st.Instance&&approval.Owner==Owner&&approval.Part=="MBOX-12"&&approval.PartSerial==Wheel12&&approval.PermissionVersion==st.PermissionVersion&&approval.SourceFingerprint==Specification.Fingerprint,"主人选件审批已失效，请重新向主人核对。");return approval!;}
    public async ValueTask<CommandResult> IsolateMechanismAsync()
    {var s=await _repository.SnapshotAsync();Safe(s);At(s,"musicbox");Permit(s);var st=State(s);Approval(s,st);Require(!st.Returned,"盒子已归还，不能继续操作。");return await Execute(Plan(s,"musicbox_winding_stopped",st with{Isolated=true,Configuration=checked(st.Configuration+1),TestedConfiguration=null},new{box=Box,operation="stop_winding",configuration=st.Configuration+1}));}
    public async ValueTask<CommandResult> FitApprovedWheelAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"musicbox");Permit(s);var st=State(s);Approval(s,st);Require(st.Isolated&&!st.Fitted&&!st.Returned,"须停止上弦并保留同一未归还音乐盒，不能重复装件。");Item(s,Box,"R3-MUSIC-BOX","warehouse",Owner);Item(s,Wheel12,"MBOX-12","warehouse",PartsStore);
        return await Execute(Plan(s,"musicbox_approved_wheel_fitted",st with{Fitted=true,Configuration=checked(st.Configuration+1),TestedConfiguration=null},new{box=Box,part=Wheel12,configuration=st.Configuration+1,owner_retains_title=true},items:[new(Box,"R3-MUSIC-BOX","warehouse",Owner,"installed",Bench,null),new(Wheel12,"MBOX-12","warehouse",PartsStore,"installed",Box,null)]));
    }
    public async ValueTask<CommandResult> CheckTurningAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"musicbox");Permit(s);var st=State(s);Approval(s,st);Require(st.Fitted&&st.Isolated&&!st.Returned,"须实际安装获批件并在停止上弦的同盒上检查。");var part=Item(s,Wheel12,"MBOX-12","installed",Box);Item(s,Box,"R3-MUSIC-BOX","installed",Bench);
        int actual=part.DefinitionId=="MBOX-12"?12:14;var check=new MusicBoxTurningCheck(st.Instance,Box,part.Id,actual,12,st.Configuration,"turning_mechanism_only",actual==12);Require(check.Compatible,"当前实物与原规格不兼容。");
        return await Execute(Plan(s,"musicbox_turning_mechanism_checked",st with{TestedConfiguration=st.Configuration},check,[BranchOperations.Change(s,StateId+":test","local_mechanism_check",check)]));
    }
    public async ValueTask<CommandResult> ReturnSameBoxAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"owner");Permit(s);var st=State(s);Approval(s,st);Require(st.Fitted&&st.TestedConfiguration==st.Configuration&&!st.Returned,"请先检查当前配置再归还同一盒子。");Item(s,Box,"R3-MUSIC-BOX","installed",Bench);Item(s,Wheel12,"MBOX-12","installed",Box);
        return await Execute(Plan(s,"musicbox_same_box_returned",st with{Returned=true},new{box=Box,owner=Owner,part=Wheel12,outcome="returned_to_owner",memory_publication=false},items:[new(Box,"R3-MUSIC-BOX","installed",Bench,"warehouse",Owner,null)]));
    }
    public async ValueTask<CommandResult> ReturnPausedBoxAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"owner");var st=State(s);Require(st.Invitation!="accept"&&!st.Completed,"暂停或撤回后才能只作安全归还，不结算维修。");var box=s.Items.SingleOrDefault(i=>i.Id==Box);Require(box is not null&&box.DefinitionId=="R3-MUSIC-BOX"&&((box.OwnerKind=="installed"&&box.OwnerId==Bench)||(box.OwnerKind=="warehouse"&&box.OwnerId==Owner)),"同盒不在主人或本地维修台。");
        ImmutableArray<ItemChange> items=box!.OwnerKind=="installed"?[new(Box,"R3-MUSIC-BOX","installed",Bench,"warehouse",Owner,null)]:[];
        return await Execute(Plan(s,"musicbox_paused_box_returned",st with{Returned=true,TestedConfiguration=null},new{box=Box,owner=Owner,repair_completed=false,physical_configuration_preserved=st.Configuration,memory_publication=false},items:items));
    }
    public async ValueTask<CommandResult> ResumeSameBoxAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"owner");Permit(s);var st=State(s);Approval(s,st);Require(st.Returned&&!st.Completed,"须由主人重新交回已暂停归还的同盒，不重开已完成维修。");Item(s,Box,"R3-MUSIC-BOX","warehouse",Owner);
        ImmutableArray<ItemChange> items=st.Fitted?[new(Box,"R3-MUSIC-BOX","warehouse",Owner,"installed",Bench,null)]:[];
        return await Execute(Plan(s,"musicbox_same_box_resumed",st with{Returned=false,TestedConfiguration=null,Configuration=checked(st.Configuration+1)},new{box=Box,owner=Owner,current_permission=st.PermissionVersion,configuration=st.Configuration+1,no_new_items=true},items:items));
    }
}
