using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;

namespace Mist.Application.Q102;

public enum RouteChoice { A, B, C }
public sealed record PlanMeaning(RouteChoice? Route, string? RejectA, string? RejectB, string? Repair, string? Power, string? Generator, bool? TestBeforeDeparture);
public sealed record ProposalState(long Version, bool Confirmed, string? DraftHash, bool StructuredAssistance, PlanMeaning? Meaning);
public sealed record Q102View(BranchSnapshot Snapshot, ActorState Actor, WorkshopState Workshop, FerryState Ferry, QuestState Quest, ProposalState Proposal)
{
    public StoredEntity Entity(string id) => Snapshot.Entities.Single(e => e.Id == id);
}
public sealed record DeparturePreview(Guid Token, ActionPreview Preview, PlanMeaning? Meaning);
public sealed class Q102Service(IAdventureRepository repository, RuntimeCatalog catalog)
{
    public const string BracketId = "Q102-BRACKET-ONE", BatteryId = "Q102-BATTERY-ONE";
    public const string DraftTask = "Q-102:departure-plan";
    private readonly Dictionary<Guid, (RelevantReadSet Reads, DraftStamp Draft)> _previews = new();
    public IAdventureRepository Repository => repository;
    public ImmutableDictionary<string, SceneGeometry> Geometry { get; } = ReadGeometry(catalog.Q102GeometryJson);
    private static ImmutableDictionary<string, SceneGeometry> ReadGeometry(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Bounds Rect(JsonElement r) => new(r[0].GetDouble(), r[1].GetDouble(), r[2].GetDouble(), r[3].GetDouble());
        Point2 Point(JsonElement p) => new(p[0].GetDouble(), p[1].GetDouble());
        return doc.RootElement.EnumerateObject().ToImmutableDictionary(p => p.Name, p =>
        {
            var e = p.Value;
            return new SceneGeometry(p.Name, Rect(e.GetProperty("walk_rect")), e.GetProperty("blocked_rects").EnumerateArray().Select(Rect).ToImmutableArray(), e.TryGetProperty("danger_rects", out var danger) ? danger.EnumerateArray().Select(Rect).ToImmutableArray() : [],
                e.GetProperty("ports").EnumerateArray().Select(x => new Port(x.GetProperty("id").GetString()!, x.GetProperty("target_scene").GetString()!, x.GetProperty("target_port").GetString()!, Point(x.GetProperty("stand_xy")))).ToImmutableArray(),
                e.GetProperty("spots").EnumerateArray().Select(x => new Spot(x.GetProperty("id").GetString()!, Point(x.GetProperty("stand_xy")), x.GetProperty("purpose").GetString()!)).ToImmutableArray());
        });
    }
    public async ValueTask InitializeSliceAsync()
    {
        var snapshot = await repository.SnapshotAsync();
        if (snapshot.Entities.Any(e => e.Id == "actor")) return;
        var entities = new EntityChange[] {
            new("actor","actor",-1,StrictJson.Serialize(new ActorState("R1-S02",new(48,90),0))),
            new("workshop","device",-1,StrictJson.Serialize(new WorkshopState(WorkshopMode.Active,false))),
            new("ferry","device",-1,StrictJson.Serialize(new FerryState(true,false,false,true,0,null,false))),
            new("quest:Q-102","quest",-1,StrictJson.Serialize(new QuestState("Q-102",Guid.NewGuid(),QuestLifecycle.Unaccepted,null))),
            new("proposal:Q-102","proposal",-1,StrictJson.Serialize(new ProposalState(0,false,null,false,null))),
            new("permission:Q-102","permission",-1,"{\"allowed\":true,\"source\":\"design_q102_internal_slice_seed\"}"),
            new("campaign","campaign",-1,"{\"scope\":\"internal_q102_slice\",\"prologue_played\":false,\"q104_closed\":false,\"glimmer_owned\":false}") };
        var plan = new ValidatedPlan("internal:q102-slice:seed", "internal_slice_initialization", entities.ToImmutableArray(),
            [new(BracketId,"Q102-BRACKET","new","","warehouse","R1-S06",null),new(BatteryId,"Q102-BATTERY","new","","vessel","CIVIL-FERRY",null)],
            [new("experience",340)], "{\"scope\":\"design_slice_fixture_not_played_prologue\"}");
        var result = await Commit(plan, []);
        if (result.Status is not (CommitStatus.Committed or CommitStatus.AlreadyCommitted)) throw new InvalidOperationException("Slice initialization failed.");
    }
    public async ValueTask<Q102View> ViewAsync()
    {
        var snapshot = await repository.SnapshotAsync();
        T Entity<T>(string id) => StrictJson.Deserialize<T>(snapshot.Entities.Single(e => e.Id == id).Json);
        return new(snapshot, Entity<ActorState>("actor"), Entity<WorkshopState>("workshop"), Entity<FerryState>("ferry"), Entity<QuestState>("quest:Q-102"), Entity<ProposalState>("proposal:Q-102"));
    }
    private async ValueTask<CommandResult> Commit(ValidatedPlan plan, ImmutableArray<EntityStamp> expected, DraftStamp? draftStamp = null)
    {
        var snapshot=await repository.SnapshotAsync();var combat=snapshot.Entities.SingleOrDefault(e=>e.Id==Mist.Application.CombatSessions.EncounterService.EntityId);
        if(combat is not null&&plan.ActionType is not("material_acquired" or "confirm_plan_meaning"))
        {
            var encounter=StrictJson.Deserialize<Mist.Application.CombatSessions.EncounterState>(combat.Json);if(encounter.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat)throw new InvalidOperationException("本次遭遇尚未结束；先继续战斗或明确撤回入口。");
        }
        if(!expected.Any(e=>e.EntityId==Mist.Application.CombatSessions.EncounterService.EntityId))expected=expected.Add(new(Mist.Application.CombatSessions.EncounterService.EntityId,combat?.Revision??-1));
        var session = repository.Session;
        return await repository.ExecuteAsync(new CommitPlanCommand(new(session.Profile,session.Branch,session.Load,new(Guid.NewGuid()),session.ReleaseId,expected,draftStamp), plan));
    }
    private static EntityChange Change<T>(Q102View view, string id, T value) => new(id, view.Entity(id).Kind, view.Entity(id).Revision, StrictJson.Serialize(value));
    private ValueTask<CommandResult> Action(Q102View view, string name, ImmutableArray<EntityChange> changes, ImmutableArray<ItemChange> items = default, string[]? related = null,object? result=null)
    {
        var actualItems = items.IsDefault ? ImmutableArray<ItemChange>.Empty : items;
        var stamps = changes.Select(c => c.Id).Concat(related ?? ["actor"]).Distinct().Select(id => new EntityStamp(id, view.Entity(id).Revision)).ToImmutableArray();
        return Commit(new("Q102:action:" + Guid.NewGuid(), name, changes, actualItems, [], result is null?"{}":StrictJson.Serialize(result)), stamps);
    }
    public async ValueTask MoveAsync(Point2 position)
        => await TraverseAsync([position]);
    public async ValueTask TraverseAsync(ImmutableArray<Point2> positions)
    {
        var view = await ViewAsync();
        if (positions.Length == 0) return;
        if (!Geometry.TryGetValue(view.Actor.Scene, out var geometry)) throw new InvalidOperationException("场景不可用。");
        var navigation = new NavigationGrid(geometry); Point2 from = view.Actor.Position;
        foreach (var position in positions)
        {
            if (!navigation.SegmentClear(from,position,view.Actor.Scene=="R1-S06"&&!view.Workshop.FrontSafe)) throw new InvalidOperationException("道路被阻挡；不能穿越扫描区或墙体。");
            from = position;
        }
        var result = await Action(view,"move",[Change(view,"actor",view.Actor with { Position = positions[^1] })]);
        if (result.Status != CommitStatus.Committed) throw new InvalidOperationException("位置变更失败。");
    }
    private void At(Q102View view, string spot)
    {
        var definition = Geometry[view.Actor.Scene].Spots.SingleOrDefault(s => s.Id == spot);
        if (definition is null || view.Actor.Position.Distance(definition.Stand) > 2.5) throw new InvalidOperationException("请先走到操作站位。");
    }
    public async ValueTask<CommandResult> AcceptAsync()
    {
        var view = await ViewAsync(); At(view,view.Actor.Scene=="R1-S02"?"R1-S02.quay_contact":"R1-S03.havel");
        return await Action(view,"accept_work_order",[Change(view,"quest:Q-102",view.Quest.Accept())]);
    }
    public async ValueTask<CommandResult> DiscoverAsync(string materialId, string spotId)
    {
        var view=await ViewAsync(); At(view,spotId);
        string[] allowed = spotId switch {
            "R1-S02.bulletin" => ["Q102-R-NOTICE"], "R1-S03.route_b_rule" => ["Q102-R-B"], "R1-S03.dispatch_audio" => ["Q102-L-REVISION"],
            "R1-S04.vessel_spec" => ["Q102-R-VESSEL"], "R1-S04.reserve_battery" => ["Q102-R-BATTERY"],
            "R1-S07.maintenance_note" or "R1-S07.local_controller" => ["PZ102-R-WORKSHOP"], "R1-S07.main_generator_tag" => ["PZ102-R-PANEL"], _ => [] };
        if (!allowed.Contains(materialId) || !catalog.Materials.Any(m=>m.Id==materialId)) throw new InvalidOperationException("材料不属于该站位或未加载。");
        string entity="material:"+materialId;
        if (view.Snapshot.Entities.Any(e=>e.Id==entity)) return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Commit(new("material:"+materialId+":acquired","material_acquired",[new(entity,"material_access",-1,"{\"acquired\":true}")],[],[],"{}"),[new("actor",view.Entity("actor").Revision)]);
    }
    public async ValueTask<CommandResult> WorkshopModeAsync(WorkshopMode mode)
    {
        var view = await ViewAsync(); At(view,"R1-S07.local_controller");
        if (mode == WorkshopMode.Disabled) throw new InvalidOperationException("停用必须来自真实战斗结算。");
        return await Action(view,"workshop_mode",[Change(view,"workshop",view.Workshop.Change(mode))]);
    }
    public async ValueTask<CommandResult> PickBracketAsync()
    {
        var view = await ViewAsync(); At(view,"R1-S06.spare_bracket");
        var item = view.Snapshot.Items.Single(i => i.Id == BracketId);
        if (item.OwnerKind != "warehouse" || item.OwnerId != "R1-S06") throw new InvalidOperationException("这份支架已取走或已安装。");
        return await Commit(new("Q102:bracket:lawfully-acquired", "pick_bracket", [], [new(BracketId,item.DefinitionId,"warehouse","R1-S06","player","hero",null)], [], "{}"), [new("workshop",view.Entity("workshop").Revision),new("actor",view.Entity("actor").Revision)]);
    }
    public async ValueTask<CommandResult> OpenLatchAsync()
    {
        var view = await ViewAsync(); At(view,"R1-S06.shortcut_latch");
        return await Action(view,"open_inner_latch",[Change(view,"workshop",view.Workshop with { LatchOpen = true })]);
    }
    public async ValueTask<CommandResult> InstallBracketAsync()
    {
        var view = await ViewAsync(); At(view,"R1-S04.beacon");
        var item = view.Snapshot.Items.Single(i => i.Id == BracketId);
        if (view.Ferry.BracketInstalled || item.OwnerKind != "player") throw new InvalidOperationException("需要已合法持有的支架。");
        return await Action(view,"install_bracket",[Change(view,"ferry",view.Ferry.Configure(bracket:true))],[new(BracketId,item.DefinitionId,"player","hero","installed","CIVIL-FERRY","bracket")]);
    }
    public async ValueTask<CommandResult> ConnectBatteryAsync(bool connected)
    {
        var view = await ViewAsync(); At(view,"R1-S04.reserve_battery");
        if (!view.Ferry.Docked || view.Ferry.BatteryConnected == connected) throw new InvalidOperationException("配置没有可应用的改变。");
        var item = view.Snapshot.Items.Single(i => i.Id == BatteryId);
        return await Action(view,"battery_connection",[Change(view,"ferry",view.Ferry.Configure(connected:connected))],
            [new(BatteryId,item.DefinitionId,item.OwnerKind,item.OwnerId,connected?"installed":"vessel","CIVIL-FERRY",connected?"battery":null)]);
    }
    public async ValueTask<CommandResult> TestBeaconAsync()
    {
        var view = await ViewAsync(); At(view,"R1-S04.beacon");
        return await Action(view,"beacon_current_test",[Change(view,"ferry",view.Ferry.Test())]);
    }
    public string? PortIssue(Q102View view, Port port)
    {
        if (!Geometry.ContainsKey(port.TargetScene)) return "当前内部样章边界，完整章节入口尚未接入。";
        string a = view.Actor.Scene, b = port.TargetScene;
        if ((a == "R1-S04" || b == "R1-S04") && !view.Ferry.Docked) return "民用渡轮已经离港，舷梯关闭。";
        if ((a == "R1-S06" && b == "R1-S02" || a == "R1-S02" && b == "R1-S06") && !view.Workshop.LatchOpen) return "回程门内闩尚未从工坊内侧打开。";
        if ((a == "R1-S07" && b == "R1-S06" || a == "R1-S06" && b == "R1-S07") && !view.Workshop.SideOpen) return "侧门联锁关闭；请查看本地检修控制说明。";
        if ((a == "R1-S05" && b == "R1-S06" || a == "R1-S06" && b == "R1-S05") && !view.Workshop.FrontSafe) return "前门扫描区仍有危险，需要明确进入战斗；也可寻找其他进入方法。";
        return null;
    }
    public async ValueTask<CommandResult> EnterPortAsync(string portId)
    {
        var view = await ViewAsync(); var port = Geometry[view.Actor.Scene].Ports.Single(p => p.Id == portId);
        if (view.Actor.Position.Distance(port.Stand) > 2.5) throw new InvalidOperationException("请先走到出口站位。");
        string? issue = PortIssue(view,port); if (issue is not null) throw new InvalidOperationException(issue);
        var target = Geometry[port.TargetScene].Ports.SingleOrDefault(p => p.Id == port.TargetPort) ?? throw new InvalidOperationException("配对入口未加载，留在原安全场景。");
        if (!new NavigationGrid(Geometry[port.TargetScene]).CanStand(target.Stand)) throw new InvalidOperationException("登记入口不可用，留在原场景等待恢复。");
        var connection=catalog.Connections.Single(c=>c.A==view.Actor.Scene&&c.B==port.TargetScene||c.A==port.TargetScene&&c.B==view.Actor.Scene);var gateIds=new List<string>{"permission:Q-102"};if(view.Actor.Scene=="R1-S04"||port.TargetScene=="R1-S04")gateIds.Add("ferry");if(view.Actor.Scene=="R1-S06"||port.TargetScene=="R1-S06")gateIds.Add("workshop");
        var proof=new Mist.Application.Travel.TraversedConnection(view.Actor.Scene,port.TargetScene,port.Id,target.Id,view.Actor.Position,target.Stand,checked(view.Actor.ActionClock+1),Mist.Application.Travel.TravelService.Contract(connection),gateIds.Select(id=>new EntityStamp(id,view.Entity(id).Revision)).ToImmutableArray(),true);
        return await Action(view,"scene_transition",[Change(view,"actor",view.Actor with { Scene = port.TargetScene, Position = target.Stand, ActionClock = proof.ActionClock })],related:["workshop","ferry","permission:Q-102"],result:proof);
    }
    public async ValueTask<CommandResult> ConfirmMeaningAsync(PlanMeaning meaning, bool structuredAssistance)
    {
        var view = await ViewAsync(); var draft = await repository.GetDraftAsync(DraftTask);
        if (!structuredAssistance && (draft is null || draft.Deleted || string.IsNullOrWhiteSpace(draft.Body))) throw new InvalidOperationException("空白正文需要明确使用结构化辅助，不能记为独立写作。");
        var proposal = new ProposalState(view.Proposal.Version+1,true,draft?.Hash,structuredAssistance,meaning);
        return await Action(view,"confirm_plan_meaning",[Change(view,"proposal:Q-102",proposal)]);
    }
    private static ImmutableArray<RuleIssue> MeaningIssues(PlanMeaning? meaning)
    {
        if (meaning is null) return [new("needs_clarification","proposal","尚未确认行动意图。")];
        var issues = ImmutableArray.CreateBuilder<RuleIssue>();
        void Check(bool good,string field) { if (!good) issues.Add(new("meaning_not_admissible",field,"请核对该项意图与现行资料；系统不会自动改写路线。")); }
        Check(meaning.Route == RouteChoice.C,"route"); Check(meaning.RejectA == "closed","reject_a"); Check(meaning.RejectB == "draught_exceeds_limit","reject_b");
        Check(meaning.Repair == "replace_bracket","repair"); Check(meaning.Power == "charged_backup_battery","power"); Check(meaning.Generator == "keep_off_until_isolation_confirmed","generator"); Check(meaning.TestBeforeDeparture == true,"test_before_departure");
        return issues.ToImmutable();
    }
    public async ValueTask<DeparturePreview> PreviewDepartureAsync()
    {
        var view = await ViewAsync(); var draft = await repository.GetDraftAsync(DraftTask);
        var issues = MeaningIssues(view.Proposal.Meaning).ToBuilder();
        if (!view.Proposal.Confirmed || view.Proposal.DraftHash != draft?.Hash) issues.Add(new("proposal_changed","proposal","正文修改后需要重新确认意图。"));
        if (view.Quest.Lifecycle != QuestLifecycle.Active) issues.Add(new("quest_not_active","quest","委托尚未接受或已经结算。"));
        if (!view.Ferry.Ready) issues.Add(new("device_not_ready","ferry","导航灯必须完成当前配置的安装、供电与测试。"));
        using (var permission = JsonDocument.Parse(view.Entity("permission:Q-102").Json))
            if (!permission.RootElement.GetProperty("allowed").GetBoolean()) issues.Add(new("permission_denied","permission:Q-102","当前检修授权不可用于撤离。"));
        if (view.Actor.Scene != "R1-S04" || view.Actor.Position.Distance(new(26,76))>2.5) issues.Add(new("not_at_submit_spot","actor","请走到渡轮写作台的登记站位。"));
        var revisions = view.Snapshot.Entities.ToImmutableDictionary(e=>e.Id,e=>e.Revision);
        var preview = PreviewFactory.Create("Q102:departure",repository.Session.Load,revisions,["quest:Q-102","ferry","proposal:Q-102","permission:Q-102","actor"],draft?.Body??"",view.Proposal.StructuredAssistance,issues.ToImmutable());
        Guid token=Guid.NewGuid(); _previews[token]=(preview.ReadSet,new(DraftTask,draft?.Id,draft?.Revision??0,draft?.Hash)); return new(token,preview,view.Proposal.Meaning);
    }
    public void ClosePreview(Guid token) => _previews.Remove(token);
    public async ValueTask<CommandResult> DepartAsync(Guid token)
    {
        var view=await ViewAsync();
        if (!_previews.Remove(token,out var captured) || !captured.Reads.Matches(repository.Session.Load,view.Snapshot.Entities.ToImmutableDictionary(e=>e.Id,e=>e.Revision))) throw new InvalidOperationException("预览已关闭或状态过期，请重新预览。");
        var fresh=await PreviewDepartureAsync(); ClosePreview(fresh.Token);
        if (fresh.Preview.Status!=PreviewStatus.Ready) throw new InvalidOperationException("当前状态不满足离港条件。");
        var reward=catalog.Quests.Single(q=>q.Id=="Q-102").Reward;
        var items=view.Snapshot.Items.Where(i=>i.Id is BracketId or BatteryId).Select(i=>new ItemChange(i.Id,i.DefinitionId,i.OwnerKind,i.OwnerId,"vessel","CIVIL-FERRY",i.Slot)).ToImmutableArray().Add(new("RE01:Q102", "RE-01", "new", "", "player", "hero", null));
        var plan=new ValidatedPlan("Q-102:resolution","civil_ferry_departure",[Change(view,"quest:Q-102",view.Quest.Close(QuestOutcome.Completed)),Change(view,"ferry",view.Ferry with { Docked=false }),Change(view,"actor",view.Actor with { Scene="R1-S02",Position=new(79,64),ActionClock=checked(view.Actor.ActionClock+1) })],items,
            [new("experience",reward.Bundle.Experience),new("marks",reward.Bundle.Marks),new("parts",reward.Bundle.Parts)],"{\"player_stays_in_harbour\":true,\"civil_ferry_departed\":true,\"glimmer_not_transferred\":true}");
        return await Commit(plan,captured.Reads.Stamps,captured.Draft);
    }
}
