using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Disclosure;
using Mist.Application.EventScheduler;
using Mist.Application.Inventory;
using Mist.Application.Knowledge;
using Mist.Application.Q102;
using Mist.Application.Relationships;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.NPC;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.NPC;

public static class NpcCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"npc-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    // Explicit fixtures issue real SQLite receipts. They are not played campaign steps or human approvals.
    public static CommandResult Fixture(ProfileStore store,string key,string action,object payload,ImmutableArray<EntityChange> entities=default,ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default)
    {
        var s=store.Session;var command=new CommitPlanCommand(new(s.Profile,s.Branch,s.Load,new(Guid.NewGuid()),s.ReleaseId,[]),new(key,action,entities.IsDefault?[]:entities,items.IsDefault?[]:items,balances.IsDefault?[]:balances,StrictJson.Serialize(payload)));var r=Done(store.ExecuteAsync(command));Success(r);return r;
    }
    public static EntityChange Entity(ProfileStore store,string id,string kind,object value)=>new(id,kind,Done(store.SnapshotAsync()).Entities.SingleOrDefault(e=>e.Id==id)?.Revision??-1,StrictJson.Serialize(value));
    public static void Register()
    {
        Add("CX-057","Each unresolved breach survives unrelated cooperation and requires its own accepted repair",()=>
        {
            var s=RelationshipState.Empty.Apply(new("breach1","N-PERA",RelationshipAction.ConfirmedBreach,"source1")).Apply(new("breach2","N-PERA",RelationshipAction.ConfirmedBreach,"source2"));s=s.Apply(new("repair1","N-PERA",RelationshipAction.AcceptedRepair,"repair-source1",RepairOf:"breach1"));Require(s.Work==WorkRelationship.RepairPending);s=s.Apply(new("new-work","N-PERA",RelationshipAction.MutualOngoing,"mutual-source"));Require(s.Work==WorkRelationship.RepairPending);s=s.Apply(new("repair2","N-PERA",RelationshipAction.AcceptedRepair,"repair-source2",RepairOf:"breach2"));Require(s.Work==WorkRelationship.Verified&&s.History.Count(h=>h.Action==RelationshipAction.ConfirmedBreach)==2);
        });
        Add("CX-055","NPC lease aliases and reserved foreground keys cannot create parallel identities",()=>
        {
            using var store=Store("event-alias");var scheduler=new EventScheduler(store,store);Throws<InvalidOperationException>(()=>Done(scheduler.QueueAsync(new("bad","v1","once",0,["NPC:N-HAVEL"],"desk",1))));Throws<InvalidOperationException>(()=>Done(scheduler.QueueAsync(new("bad","v1","once",0,["input:foreground"],"desk",1))));Require(Done(store.SnapshotAsync()).Entities.All(e=>e.Kind!="event"));
        });
        Add("CX-054","Thirteen identities, temporal evidence and neutral refusal never echo a guessed secret",()=>
        {
            var p=new KnowledgeProjection(Catalog);Require(p.Actors.Count==13);var fact=p.Facts["K-MARA-D1-ALIVE"];var e=new KnowledgeEvidence(fact.Id,fact.SourceRoot,"fixture-receipt",fact.TimeScope,fact.Kind);
            Require(p.Topics("N-JUNE",new([e]),new([]),new HashSet<string>()).Length==0);var topics=p.Topics("N-JUNE",new([e]),new([e]),new HashSet<string>());Require(topics.Single().TimeScope=="D-1_only");
            Require(KnowledgeProjection.Reply("Is Mara at the north station?",topics)==KnowledgeProjection.Reply("Does a protected address exist?",topics));Require(!KnowledgeProjection.NeutralReply.Contains("station",StringComparison.OrdinalIgnoreCase));
            var copy=e with{ReceiptId="copy-receipt"};Require(KnowledgeProjection.IndependentSources(new([e,copy]),fact.Id)==1);
        });
        Add("CX-054","Actual delivery alone grants knowledge; copy/replay has one source; chapter and load grant none",()=>
        {
            using var store=Store("knowledge");var k=new KnowledgeService(store,Catalog);Require(Done(k.TopicsAsync("N-JUNE")).Length==0);Fixture(store,"chapter:5","explicit_engineering_fixture",new{chapter=5});Require(Done(k.TopicsAsync("N-JUNE")).Length==0);
            var f=k.Projection.Facts["K-MARA-D1-ALIVE"];var r=Fixture(store,"fixture:fact:D1","verified_fact",new VerifiedFactRecord(f.Id,f.SourceRoot,f.TimeScope,f.Kind));string receipt=r.Receipt!.Id.Value.ToString();Success(Done(k.DeliverAsync("N-JUNE",receipt)));Require(Done(k.TopicsAsync("N-JUNE")).Length==0);Success(Done(k.DeliverAsync("player",receipt)));Require(Done(k.TopicsAsync("N-JUNE")).Single().TimeScope=="D-1_only");
            var replay=Done(k.DeliverAsync("N-JUNE",receipt));Require(replay.Status==CommitStatus.AlreadyCommitted);Done(store.ContinueAsync());Require(Done(k.TopicsAsync("N-JUNE")).Length==1&&Done(k.TopicsAsync("N-MARA")).Length==0);
        });
        Add("CX-055","All leases are atomic; a failed multi-resource request holds no partial resource",()=>
        {
            using var store=Store("leases");var load=store.Session.Load;Require(Done(store.TryLeasesAsync(load,"real-work-post",["N-HAVEL"])));Require(!Done(store.TryLeasesAsync(load,"candidate",["door","N-HAVEL","input:foreground"])));Require(Done(store.TryLeasesAsync(load,"other",["door","input:foreground"])));Done(store.ReleaseLeasesAsync(load,"other"));Require(!Done(store.TryLeaseAsync("N-HAVEL","next")));
        });
        Add("CX-055","Writing cannot be interrupted; pause/resume retains seed/node, foreground exclusion and work post",()=>
        {
            using var store=Store("scheduler");var scheduler=new EventScheduler(store,store);var def=new EventDefinition("hotel","v1","once",2,["N-HAVEL","scene:R1-S03"],"communications",171);var a=Done(scheduler.QueueAsync(def));var b=Done(scheduler.QueueAsync(def));Require(a.Instance==b.Instance);long seq=Done(store.SnapshotAsync()).Sequence;
            Require(Done(scheduler.StartNextAsync(ScheduleBoundary.Writing)) is null&&Done(store.SnapshotAsync()).Sequence==seq);Require(Done(scheduler.StartNextAsync(ScheduleBoundary.Safe))!.Instance==a.Instance);
            Done(store.TryLeasesAsync(store.Session.Load,"real-installation",["workpost:engineer"]));var c=Done(scheduler.QueueAsync(new("banter","v1","once",1,["CP-LINA"],"deck",27)));Require(Done(scheduler.StartNextAsync(ScheduleBoundary.Safe,c.Instance)) is null);
            Success(Done(scheduler.CheckpointAsync(a.Instance,"letter-written",pause:true)));Require(!Done(store.TryLeaseAsync("workpost:engineer","other")));var resumed=Done(scheduler.StartNextAsync(ScheduleBoundary.Safe,a.Instance));Require(resumed!.Node=="letter-written"&&resumed.Definition.Seed==171);
            Done(store.ContinueAsync());var recovered=Done(scheduler.StartNextAsync(ScheduleBoundary.Safe,a.Instance));Require(recovered!.Node=="letter-written"&&recovered.Instance==a.Instance);Success(Done(scheduler.CheckpointAsync(a.Instance,"done",complete:true)));Require(Done(scheduler.StartNextAsync(ScheduleBoundary.Safe,c.Instance)) is not null);
        });
        Add("CX-055","Declared expiry cancels only the relevant event and never advances on wall time",()=>
        {
            using var store=Store("expiry");var scheduler=new EventScheduler(store,store);var a=Done(scheduler.QueueAsync(new("visit","v1","once",1,["N-HAVEL"],"desk",5,"departure")));Done(scheduler.StartNextAsync(ScheduleBoundary.Safe));Done(scheduler.CleanStageAsync("unrelated"));Require(!Done(store.TryLeaseAsync("N-HAVEL","other")));Done(scheduler.CleanStageAsync("departure"));Require(Done(store.TryLeaseAsync("N-HAVEL","other")));var state=StrictJson.Deserialize<EventSession>(Done(store.SnapshotAsync()).Entities.Single(e=>e.Id=="event:"+a.Instance).Json);Require(state.State==EventLifecycle.Cancelled&&state.CancelReason=="declared_stage_boundary");
        });
        Add("CX-056","Lina cannot join at Q102, decline permits another invitation, two auxiliaries remain",()=>
        {
            using var store=Store("roster");Done(new Q102Service(store,Catalog).InitializeSliceAsync());var roster=new RosterService(store,Catalog);Throws<InvalidOperationException>(()=>Done(roster.InviteAsync("CP-LINA")));Require(roster.Helpers(Done(store.SnapshotAsync())).SequenceEqual(RosterService.Auxiliary));Fixture(store,"Q-104:resolution","explicit_engineering_fixture",new{scope="invitation gate fixture"});Success(Done(roster.InviteAsync("CP-LINA")));Success(Done(roster.DecideAsync("CP-LINA",false)));Success(Done(roster.InviteAsync("CP-LINA")));Success(Done(roster.DecideAsync("CP-LINA",true)));Success(Done(roster.SelectAsync(["CP-LINA"])));Require(roster.Helpers(Done(store.SnapshotAsync())).SequenceEqual(new[]{"CP-LINA","AUX-GUARD"}));
        });
        Add("CX-056","Late tactics use shared level; one borrowed relic returns as the same instance on safe departure",()=>
        {
            using var store=Store("relic");Done(new Q102Service(store,Catalog).InitializeSliceAsync());var party=new PartyService(store,Catalog);Done(party.InitializeStarterCollectionAsync());var roster=new RosterService(store,Catalog);Fixture(store,"Q-104:resolution","explicit_engineering_fixture",new{},items:[new("original-relic","RE-01","new","","player","hero",null)],balances:[new("experience",10000)]);Success(Done(roster.InviteAsync("CP-LINA")));Success(Done(roster.DecideAsync("CP-LINA",true)));Success(Done(roster.ChooseTacticsAsync("CP-LINA",ImmutableDictionary<int,string>.Empty.Add(12,"B").Add(20,"A"))));Throws<InvalidOperationException>(()=>Done(party.EquipAsync("original-relic","relic2","CP-LINA")));Success(Done(party.EquipAsync("original-relic","relic1","CP-LINA")));Success(Done(roster.RequestDepartureAsync("CP-LINA")));Success(Done(roster.FinishDepartureAsync("CP-LINA")));var item=Done(store.SnapshotAsync()).Items.Single(i=>i.Id=="original-relic");Require(item.OwnerKind=="player"&&item.OwnerId=="hero"&&Done(roster.ViewAsync()).Single(v=>v.Entry.Actor=="CP-LINA").SharedLevel==Mist.Core.Progression.ProgressionRules.Level(10340));
        });
        Add("CX-057","Issue disagreement, subtitles and grammar have no work penalty; repair adds history",()=>
        {
            var state=RelationshipState.Empty.Apply(new("work","N-PERA",RelationshipAction.VerifiedWork,"actual-work","alarm"));state=state.Apply(new("policy","N-PERA",RelationshipAction.IssueStatement,"actual-choice",Issue:"distributed",Position:IssuePosition.Oppose));Require(state.Work==WorkRelationship.Verified&&state.Issues["distributed"]==IssuePosition.Oppose);state=state.Apply(new("breach","N-PERA",RelationshipAction.ConfirmedBreach,"actual-breach","alarm"));state=state.Apply(new("repair","N-PERA",RelationshipAction.AcceptedRepair,"actual-accepted-repair",RepairOf:"breach"));Require(state.Work==WorkRelationship.Verified&&state.History.Any(e=>e.Id=="breach")&&state.History.Length==4);
        });
        Add("CX-057","Disclosure refuses arbitrary prose/meeting/revised recipient; confirmed field delivery occurs once",()=>
        {
            using var store=Store("disclosure");var material=new ScopedMaterial("W04-record","v1","N-W04","original-W04",ImmutableDictionary<string,string>.Empty.Add("time","D-2").Add("role","reviewer").Add("private_address","protected fixture field"));
            var auth=new DisclosureAuthority("N-W04","consent-1",material.Id,material.Version,["time","role"],["N-MIRA"],"review","CQ-MIRA-02:fixture-instance");var r=Fixture(store,"fixture:authority","disclosure_authority_verified",auth);var grant=new DisclosureGrant(auth.GrantId,auth.Subject,auth.Material,auth.Version,auth.Fields,auth.Recipients,auth.Purpose,auth.WorkSlot,true,r.Receipt!.Id.Value.ToString());Fixture(store,"fixture:material+grant","explicit_engineering_fixture",new{},[Entity(store,"scoped-material:"+material.Id,"scoped_material",material),Entity(store,"grant:"+grant.Id,"disclosure_grant",grant)]);
            var service=new DisclosureService(store);var choice=new DisclosureSelection(auth.WorkSlot,material.Id,"v1",["time","role"],"N-MIRA","review");Require(!Done(service.PreviewAsync(choice with{FreeBody="a secret may be in arbitrary prose"})).Ready);Require(!Done(service.PreviewAsync(choice with{Purpose="meeting"})).Ready);Require(!Done(service.PreviewAsync(choice with{Recipient="N-JUNE"})).Ready);Require(!Done(service.PreviewAsync(choice with{Fields=["private_address"]})).Ready);
            long before=Done(store.SnapshotAsync()).Sequence;var p=Done(service.PreviewAsync(choice));Require(p.Ready&&!p.Text.Contains("protected")&&Done(store.SnapshotAsync()).Sequence==before);Success(Done(service.ConfirmAsync(p.Token)));Require(Done(service.ConfirmAsync(p.Token)).Status==CommitStatus.AlreadyCommitted&&Done(store.SnapshotAsync()).Entities.Count(e=>e.Kind=="delivery")==1);
            var revoke=Fixture(store,"fixture:revoke","disclosure_revocation_verified",new{subject="N-W04",grant_id=grant.Id});Success(Done(service.RevokeAsync(grant.Id,revoke.Receipt!.Id.Value.ToString())));Require(Done(service.ConfirmAsync(p.Token)).Status==CommitStatus.AlreadyCommitted&&!Done(service.PreviewAsync(choice)).Ready&&Done(store.SnapshotAsync()).Entities.Any(e=>e.Kind=="delivery"));
        });
        Add("CX-057","Grant revoked before confirmation invalidates old preview, leaving the world unsent",()=>
        {
            using var store=Store("revoked-preview");var m=new ScopedMaterial("letter","v1","N-LINA-BROTHER","original-letter",ImmutableDictionary<string,string>.Empty.Add("contact","correspondence only"));var a=new DisclosureAuthority(m.Subject,"grant",m.Id,m.Version,["contact"],["CP-LINA"],"correspondence","CQ-LINA-03:fixture");var r=Fixture(store,"auth","disclosure_authority_verified",a);var g=new DisclosureGrant(a.GrantId,a.Subject,a.Material,a.Version,a.Fields,a.Recipients,a.Purpose,a.WorkSlot,true,r.Receipt!.Id.Value.ToString());Fixture(store,"scope","explicit_engineering_fixture",new{},[Entity(store,"scoped-material:"+m.Id,"scoped_material",m),Entity(store,"grant:grant","disclosure_grant",g)]);var service=new DisclosureService(store);var p=Done(service.PreviewAsync(new(a.WorkSlot,m.Id,m.Version,["contact"],"CP-LINA","correspondence")));Require(p.Ready);var rev=Fixture(store,"rev","disclosure_revocation_verified",new{subject=a.Subject,grant_id=a.GrantId});Success(Done(service.RevokeAsync(g.Id,rev.Receipt!.Id.Value.ToString())));Require(Done(service.ConfirmAsync(p.Token)).Status==CommitStatus.Rejected&&Done(store.SnapshotAsync()).Entities.All(e=>e.Kind!="delivery"));
        });
    }
}
