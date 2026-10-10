using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterOne;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.VesselInterior;
using Mist.Core.Voyage;

namespace Mist.Application.Voyage;

// Issues a concrete source-bound public berth handoff. Travel retains actual relocation authority.
public sealed class RegionalVoyageService
{
    private readonly IAdventureRepository _repository;
    private readonly TravelService _travel;
    public RegionalVoyageDefinition Definition {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public RegionalVoyageService(IAdventureRepository repository,RuntimeCatalog catalog,RegionalVoyageDefinition definition,IReadOnlyDictionary<string,SceneGeometry> geometry)
    {
        RegionalVoyageRules.Validate(definition);_repository=repository;Definition=definition;Geometry=geometry.ToImmutableDictionary();
        if(geometry.Any(p=>p.Key!=p.Value.Id||!catalog.Scenes.Any(s=>s.Id==p.Key)))throw new InvalidDataException("Unregistered voyage collision geometry.");
        foreach(string scene in new[]{definition.From+"-S01",definition.To+"-S01","HUB-S02"})if(!Geometry.ContainsKey(scene))throw new InvalidDataException("The source, chart and destination geometry must actually exist.");
        VoyageRules.SafeBerth(Geometry[definition.To+"-S01"],definition.BerthSpot);_travel=new(repository,catalog,Geometry);
    }
    private static void Require(bool okay,string message){if(!okay)throw new InvalidOperationException(message);}
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("当前现场未加载。");
    private static bool Current(BranchSnapshot s,IEnumerable<EntityStamp> reads)=>reads.All(r=>BranchOperations.Entity(s,r.EntityId)?.Revision==r.Revision);
    private static void Guard(BranchSnapshot s)
    {
        BranchOperations.Safe(s);Require(!BranchOperations.Receipt(s,"Q-604"),"终章起航后外区航行暂停。");
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"请先交接前景工作。");
        Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(t=>t.CargoInTransit&&!t.Completed),"货物护送须先完成规定安全路段。");
    }
    private SharedVessel Ship(BranchSnapshot s)
    {
        var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("巡灯船尚未实际移交。");
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover")??throw new InvalidOperationException("实际使用权来源缺失。");
        var h=StrictJson.Deserialize<TidegateHandover>(receipt.ResultJson);
        Require(ship.Instance!=Guid.Empty&&h.VesselInstance==ship.Instance.ToString()&&h.VesselDefinition=="GLIMMER"&&ship.WatchOfficer=="N-DECKWATCH"&&h.WatchOfficer==ship.WatchOfficer&&ship.Spaces.SequenceEqual(VesselInteriorRules.Spaces)&&h.Spaces.SequenceEqual(ship.Spaces)&&s.Entities.Count(e=>e.Kind=="shared_vessel")==1,"船只、值守或空间来源不符。");return ship;
    }
    private VesselBerth RegisteredBerth(BranchSnapshot s,SharedVessel ship,string region)
    {
        var berth=BranchOperations.Read<VesselBerth>(s,"berth:"+region)??throw new InvalidOperationException("地区泊位尚未实际登记。");
        var source=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);
        Require(berth.Instance==ship.Instance&&berth.Region==region&&berth.Scene==region+"-S01"&&Current(s,berth.GateReads)&&StrictJson.Serialize(source with{AuthorityReceipt=berth.AuthorityReceipt})==StrictJson.Serialize(berth),"泊位实例、原始回执或当前安全版本不符。");
        Require(new NavigationGrid(Geometry[berth.Scene]).CanStand(berth.Landing,true)&&new NavigationGrid(Geometry[berth.Scene]).CanStand(berth.Boarding,true),"真实靠泊落点不可站立。");return berth;
    }
    private void Chart(BranchSnapshot s,SharedVessel ship)
    {
        var a=Actor(s);var station=Geometry["HUB-S02"].Spots.Single(p=>p.Id=="HUB-S02.chart");
        Require(a.Scene=="HUB-S02"&&a.Position.Distance(station.Stand)<=2.5&&new NavigationGrid(Geometry[a.Scene]).SegmentClear(a.Position,station.Stand,true),"请实际走到本船航图工位。");
        foreach(string scene in VesselInteriorRules.Spaces)
        {
            var space=BranchOperations.Read<VesselSpace>(s,"ship-space:"+scene)??throw new InvalidOperationException("三个船舱尚未实际登记。");
            var proof=StrictJson.Deserialize<VesselSpace>(BranchOperations.Evidence(s,space.AuthorityReceipt,"ship_space_verified").ResultJson);
            Require(space.Instance==ship.Instance&&space.Scene==scene&&Current(s,space.GateReads)&&StrictJson.Serialize(proof with{AuthorityReceipt=space.AuthorityReceipt})==StrictJson.Serialize(space),"当前船舱实际来源失效。");
            if(scene==a.Scene)Require(space.TransferStand==station.Stand,"航图工位来源不符。");
        }
    }
    private StoredReceipt Source(BranchSnapshot s)
    {
        Require(BranchOperations.Read<QuestState>(s,"quest:"+Definition.SourceQuest) is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed},"前一区域核查尚未实际收束。");
        return s.Receipts.SingleOrDefault(r=>r.SemanticKey==Definition.SourceQuest+":resolution"&&r.ActionType==Definition.SourceAction)??throw new InvalidOperationException("缺少本次方向的实际交接来源。");
    }
    private (string Commission,string HandoverHash) Commission(BranchSnapshot s)
    {
        Source(s);var c=BranchOperations.Entity(s,"commission:next-inquiry")??throw new InvalidOperationException("下一核查委托尚未签发。");using var doc=JsonDocument.Parse(c.Json);var j=doc.RootElement;
        Require(j.GetProperty("allowed").GetBoolean()&&j.GetProperty("region").GetString()==Definition.To&&j.GetProperty("purpose").GetString()==Definition.CommissionPurpose&&j.GetProperty("basis").GetString()==Definition.SourceQuest+":resolution"&&!j.GetProperty("arrival_occurred").GetBoolean(),"核查地区、用途或来源不符。");
        var hand=BranchOperations.Entity(s,Definition.From+":actual_handover")??throw new InvalidOperationException("来源地区的实际事实交接未提交。");using var hd=JsonDocument.Parse(hand.Json);
        if(Definition.From=="R2")
        {
            var facts=hd.RootElement.GetProperty("public_facts");Require(facts.GetProperty("mining_batch").GetString()=="MC-2"&&facts.GetProperty("dated_transfer").GetString()=="D-1_only"&&facts.GetProperty("historical_signature").GetString()=="scope_needs_verification"&&hd.RootElement.GetProperty("protected_original_custodian").GetString()=="N-JUNE","矿区方向不能扩展为当前导师状态或完整身份公开。");
        }
        else if(Definition.From=="R3")
        {
            var h=hd.RootElement;Require(h.GetProperty("tower").GetString()=="TWR-R3"&&h.GetProperty("destination").GetString()=="R4"&&h.GetProperty("batch").GetString()=="MC-2"&&h.GetProperty("sample_seal").GetString()=="R3-MC2-SAMPLE-01"&&h.GetProperty("recipient").GetString()=="R4-public-verification-desk"&&h.GetProperty("purpose").GetString()==Definition.CommissionPurpose&&h.GetProperty("causality").GetString()=="association_requires_verification"&&h.GetProperty("mara_current_status").GetString()=="unknown"&&hand.Json==Source(s).ResultJson,"矿区封样、来源版本和学术核验范围不符。");
            Require(s.Items.Any(i=>i.Id=="R3-MC2-SAMPLE-01"&&i.DefinitionId=="R3-SEALED-VERIFICATION-SAMPLE"&&i.OwnerKind=="player"&&i.OwnerId=="hero"),"实际唯一封样尚未随核验任务交接。");
        }
        else if(Definition.From=="R4")
        {
            var h=StrictJson.Deserialize<Mist.Core.ChapterFour.TechnicalHandover>(hand.Json);
            Require(hand.Json==Source(s).ResultJson&&h.Destination=="R5"&&h.Scope=="bounded_validated_alternative"&&h.Evidence=="E-06/E-07/E-11"&&h.Location=="old_city_north_station"&&h.MaraCurrentStatus=="unknown"&&h.PublicPreparation.SequenceEqual(new[]{"verified_scheme","six_tower_changes","transition_supply","common_protocol","field_trials"})&&h.PolicyDecision=="not_selected","学城的有限技术、北站核查和公开准备交接范围不符。");
        }
        else if(Definition.From=="R5")
        {
            var h=StrictJson.Deserialize<Mist.Core.ChapterFive.OldCityHandover>(hand.Json);
            Require(hand.Json==Source(s).ResultJson&&h.Destination=="R6"&&h.Tower=="TWR-R5"&&h.MaraCurrentStatus=="rescued_alive_directly_verified"&&h.ResponsibilityScope=="design_policy_execution_separately_verified"&&h.Custody is "public_archive_bounded" or "joint_sealed"&&h.Publication is "redacted_findings" or "redacted_findings_and_conditions"&&h.Supervisors.SequenceEqual(new[]{"ROLE-R5-OPERATOR","N-ADA","ROLE-R5-ARCHIVE-CUSTODIAN"})&&h.TowerConfiguration>=0&&h.Authority=="temporary_joint_emergency"&&h.RookStatus=="refused_transfer_entered_seventh_tower"&&h.PolicyDecision=="not_selected","旧城实际救援、分项责任、有限保管与临时联合交接范围不符。");
            var mara=BranchOperations.Read<Mist.Core.ChapterFive.MaraRescue>(s,"R5:mara");
            Require(mara is{Status:"received_alive"}&&s.Receipts.Any(r=>r.SemanticKey=="Q-502:resolution"),"联合核查方向需要实际释放、护送和接待来源。");
        }
        else throw new InvalidOperationException("出发地区的实际交接合同尚未登记。");
        return (c.Json,VoyageRules.Hash(hand.Json));
    }
    private string BindingId(SharedVessel ship)=>"voyage:regional:"+ship.Instance+":"+Definition.From+":"+Definition.To;
    private RegionalVoyageBinding Expected(BranchSnapshot s,SharedVessel ship)
    {
        var (commission,hand)=Commission(s);var a=Actor(s);
        return new(ship.Instance,Definition.From,Definition.To,Source(s).ReceiptId,commission,hand,VoyageRules.Hash(Geometry[Definition.From+"-S01"]),VoyageRules.Hash(Geometry[Definition.To+"-S01"]),VoyageRules.Hash(Geometry["HUB-S02"]),VoyageRules.Hash(Definition),Definition.Duty,Definition.AuthorityScope,a.Scene,a.Position,a.ActionClock,Definition.ReviewStatus);
    }
    private ImmutableArray<EntityStamp> Gates(BranchSnapshot s,SharedVessel ship)
    {
        var b=BranchOperations.Read<RegionalVoyageBinding>(s,BindingId(ship))??throw new InvalidOperationException("地方公开泊位交接尚未实际签发。");
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey==BindingId(ship)&&r.ActionType=="regional_voyage_handoff_issued")??throw new InvalidOperationException("地方交接来源缺失。");
        var chart=Geometry["HUB-S02"].Spots.Single(p=>p.Id=="HUB-S02.chart");
        Require(b.VesselInstance==ship.Instance&&b.From==Definition.From&&b.To==Definition.To&&b.SourceQuestReceipt==Source(s).ReceiptId&&b.DefinitionSha256==VoyageRules.Hash(Definition)&&b.Duty==Definition.Duty&&b.AuthorityScope==Definition.AuthorityScope&&b.ReviewStatus==Definition.ReviewStatus&&b.SourceHandoverSha256==VoyageRules.Hash(BranchOperations.Entity(s,Definition.From+":actual_handover")?.Json??"")&&b.SourceGeometrySha256==VoyageRules.Hash(Geometry[Definition.From+"-S01"])&&b.DestinationGeometrySha256==VoyageRules.Hash(Geometry[Definition.To+"-S01"])&&b.ChartGeometrySha256==VoyageRules.Hash(Geometry["HUB-S02"])&&b.CheckedScene=="HUB-S02"&&b.CheckedPosition.Distance(chart.Stand)<=2.5&&b.CheckedActionClock>0&&StrictJson.Deserialize<RegionalVoyageBinding>(receipt.ResultJson)==b,"当前实例、公开范围或安全几何与实际交接不符。");
        // Later inquiry commissions may advance. This original handoff retains its own immutable bounded source.
        using var c=JsonDocument.Parse(b.SourceCommissionJson);Require(c.RootElement.GetProperty("allowed").GetBoolean()&&!c.RootElement.GetProperty("arrival_occurred").GetBoolean()&&c.RootElement.GetProperty("region").GetString()==Definition.To&&c.RootElement.GetProperty("basis").GetString()==Definition.SourceQuest+":resolution"&&c.RootElement.GetProperty("purpose").GetString()==Definition.CommissionPurpose,"原始地方交接的用途来源不符。");
        return [new(BindingId(ship),BranchOperations.Entity(s,BindingId(ship))!.Revision),new("quest:"+Definition.SourceQuest,BranchOperations.Entity(s,"quest:"+Definition.SourceQuest)!.Revision)];
    }
    private async ValueTask<CommandResult> Commit(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object result)
    {
        var command=BranchOperations.Plan(_repository,s,key,action,changes,s.Entities.Select(e=>e.Id).Concat(new[]{"actor","ship:glimmer","berth:"+Definition.To,"sea-route:"+Definition.From+":"+Definition.To}).Distinct().ToArray(),result);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("航图核查需要原子前景占用。");string owner="regional-voyage:"+command.Context.Command.Value;
        Require(await leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground"]),"另一项前景工作尚未交接。");
        try{return await _repository.ExecuteAsync(command);}finally{await leases.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private static void Committed(CommandResult r)=>Require((r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted)&&r.Receipt is not null,"核查事务尚未可靠提交。");
    public async ValueTask<VoyagePreparation> VerifyAndRegisterAsync()
    {
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);Require(ship.DockRegion==Definition.From,"请在本次委托出发地区的船舱核对。");Chart(s,ship);RegisteredBerth(s,ship,Definition.From);
        var expected=Expected(s,ship);var old=BranchOperations.Read<RegionalVoyageBinding>(s,BindingId(ship));
        if(old is not null)Require(old==(expected with{CheckedScene=old.CheckedScene,CheckedPosition=old.CheckedPosition,CheckedActionClock=old.CheckedActionClock}),"已有地方交接版本不同，不能覆盖旧来源。");
        var hand=await Commit(s,BindingId(ship),"regional_voyage_handoff_issued",old is null?[BranchOperations.Change(s,BindingId(ship),"regional_voyage_binding",expected)]:[],old??expected);Committed(hand);
        s=await _repository.SnapshotAsync();Guard(s);Chart(s,Ship(s));var gates=Gates(s,ship);
        var route=await Commit(s,BindingId(ship)+":sea-route","sea_route_verified",[],new SeaRoute(Definition.From,Definition.To,gates,"actual bounded local public voyage handoff"));Committed(route);
        s=await _repository.SnapshotAsync();Guard(s);Chart(s,Ship(s));var stand=VoyageRules.SafeBerth(Geometry[Definition.To+"-S01"],Definition.BerthSpot);
        var berth=await Commit(s,BindingId(ship)+":berth","vessel_berth_verified",[],new VesselBerth(ship.Instance,Definition.To,Definition.To+"-S01",stand.Stand,stand.Stand,gates,"actual local public landing duty handoff"));Committed(berth);
        var routeRegistration=await _travel.RegisterSeaRouteAsync(route.Receipt!.Id.Value.ToString());Committed(routeRegistration);
        s=await _repository.SnapshotAsync();Guard(s);Chart(s,Ship(s));var berthRegistration=await _travel.RegisterBerthAsync(berth.Receipt!.Id.Value.ToString());Committed(berthRegistration);
        return new(hand,route,berth,new(routeRegistration,berthRegistration));
    }
    public async ValueTask<CommandResult> SailAsync(string destination)
    {
        Require(destination==Definition.From||destination==Definition.To,"该地方生产者只提供已核验的两端航路。");var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);Require(ship.DockRegion==Definition.From||ship.DockRegion==Definition.To,"当前船不在该已核查航段。");Chart(s,ship);Gates(s,ship);RegisteredBerth(s,ship,Definition.From);RegisteredBerth(s,ship,Definition.To);return await _travel.SailAsync(destination);
    }
    public async ValueTask<CommandResult> RecordArrivalAsync()
    {
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);var actor=Actor(s);Gates(s,ship);var berth=RegisteredBerth(s,ship,Definition.To);
        Require(ship.DockRegion==Definition.To&&actor.Scene==berth.Scene&&actor.Position.Distance(berth.Landing)<=2.5,"请在实际目的泊位核对登陆。");
        var sail=s.Receipts.Where(r=>r.ActionType=="sail_shared_glimmer").OrderByDescending(r=>r.Sequence).FirstOrDefault()??throw new InvalidOperationException("缺少实际航行来源。");using var doc=JsonDocument.Parse(sail.ResultJson);var j=doc.RootElement;
        Require(j.GetProperty("ship_instance").GetGuid()==ship.Instance&&j.GetProperty("from").GetString()==Definition.From&&j.GetProperty("to").GetString()==Definition.To,"其他船或其他航段不能代替本次抵达。");
        var route=BranchOperations.Read<SeaRoute>(s,"sea-route:"+Definition.From+":"+Definition.To)??throw new InvalidOperationException("航路未登记。");
        var originalRoute=StrictJson.Deserialize<SeaRoute>(BranchOperations.Evidence(s,route.AuthorityReceipt,"sea_route_verified").ResultJson);
        Require(route.A==Definition.From&&route.B==Definition.To&&Current(s,route.GateReads)&&route.GateReads.SequenceEqual(Gates(s,ship))&&StrictJson.Serialize(originalRoute with{AuthorityReceipt=route.AuthorityReceipt})==StrictJson.Serialize(route),"实际航路登记来源或当前版本不符。");
        var hand=s.Receipts.Single(r=>r.SemanticKey==BindingId(ship));Require(sail.Sequence>hand.Sequence,"航行必须晚于本次公开交接。");var proof=new VesselArrivalProof(ship.Instance,Definition.From,Definition.To,actor.Scene,actor.Position,actor.ActionClock,sail.ReceiptId,route.AuthorityReceipt,berth.AuthorityReceipt,hand.ReceiptId,VoyageRules.Hash(Geometry[actor.Scene]),Definition.Version,"public_landing_only_not_local_registration");
        return await Commit(s,BindingId(ship)+":arrival","actual_vessel_arrival",[BranchOperations.Change(s,BindingId(ship)+":arrival","vessel_arrival",proof)],proof);
    }
}
