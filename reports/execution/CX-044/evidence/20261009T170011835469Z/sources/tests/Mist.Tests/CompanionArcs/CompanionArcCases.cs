using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.CompanionArcs;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.Quests;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.CompanionArcs;

public static class CompanionArcCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"personal-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static string Receipt(ProfileStore s,string action,object payload)=>Fixture(s,"explicit-personal-fixture:"+Guid.NewGuid(),action,payload).Receipt!.Id.Value.ToString();
    private static PersonalNodeState State(ProfileStore s,string id)=>StrictJson.Deserialize<PersonalNodeState>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id=="personal:"+id).Json);
    private static PrivateInvitation Invitation(ProfileStore s,string id)=>StrictJson.Deserialize<PrivateInvitation>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id=="private-invitation:"+id).Json);
    private static string Invite(ProfileStore s,CompanionArcService service,string id,bool allowed=true)
    {
        var node=service.Definitions[id];string r=Receipt(s,"private_invitation_verified",new PrivateInvitationAuthority(node.Actor,id,allowed));Success(Done(service.RegisterInvitationAsync(r)));return r;
    }
    // All setup records are explicit engineering fixtures, not authored campaign play or approvals.
    private static PersonalNodeState Prepare(ProfileStore s,CompanionArcService service,string id)
    {
        var node=service.Definitions[id];var snapshot=Done(s.SnapshotAsync());
        if(!snapshot.Receipts.Any(r=>r.SemanticKey==node.MainGate+":resolution"))Fixture(s,node.MainGate+":resolution","explicit_engineering_fixture",new{scope="personal node gate fixture"});
        if(node.Previous is not null&&!snapshot.Receipts.Any(r=>r.SemanticKey==node.Previous+":resolution"))Fixture(s,node.Previous+":resolution","explicit_engineering_fixture",new{scope="previous personal result fixture"});
        Invite(s,service,id);Success(Done(service.AcceptAsync(id)));return State(s,id);
    }
    private static string Consent(ProfileStore s,CompanionArcService service,PersonalNodeState state,string actor,ContactPurpose purpose,bool allowed=true)
    {
        string r=Receipt(s,purpose==ContactPurpose.Meeting?"meeting_consent_verified":"contact_consent_verified",new PersonalContactConsent(state.Node,state.Instance,actor,purpose,allowed));Success(Done(service.RegisterContactConsentAsync(r,purpose)));return r;
    }
    private static string Work(ProfileStore s,CompanionArcService service,PersonalNodeState state,string outcome,string? transport=null,PersonalWorkEvidence? replacement=null,string? action=null)
    {
        var node=service.Definitions[state.Node];var e=replacement??new PersonalWorkEvidence(state.Node,node.Actor,state.Instance,outcome,Invitation(s,state.Node).AuthorityReceipt,transport);
        return Receipt(s,action??CompanionArcService.RequiredWorkAction(state.Node),e);
    }
    private static string Result(ProfileStore s,CompanionArcService service,PersonalNodeState state,string outcome,string? work=null,string? transport=null,ImmutableArray<string> meeting=default,ImmutableArray<string> contact=default)
    {
        string actual=work??Work(s,service,state,outcome,transport);var node=service.Definitions[state.Node];
        return Receipt(s,"personal_work_verified",new PersonalWorkResult(state.Node,node.Actor,outcome,actual,!meeting.IsDefaultOrEmpty,transport,meeting.IsDefault?[]:meeting,state.Instance,contact.IsDefault?[]:contact));
    }
    private static (QuestState Quest,string Receipt) Transport(ProfileStore s,string outcome="面板先到",EscortSegmentResult? replacement=null)
    {
        var quest=new QuestState("Q-603",Guid.NewGuid(),QuestLifecycle.Closed,QuestOutcome.Completed);
        Fixture(s,"explicit-q603-instance:"+Guid.NewGuid(),"explicit_engineering_fixture",new{},[Entity(s,"quest:Q-603","quest",quest)]);
        return(quest,Receipt(s,"escort_segment_result",replacement??new("Q-603",quest.Instance,"PZ-Q603-LOAD",Guid.NewGuid(),outcome,true)));
    }
    private static void RefusedWithoutMutation(ProfileStore s,Action action)
    {
        var before=Done(s.SnapshotAsync());Throws<InvalidOperationException>(action);var after=Done(s.SnapshotAsync());Require(before.Sequence==after.Sequence&&before.Balances.SequenceEqual(after.Balances)&&before.Entities.SequenceEqual(after.Entities));
    }
    private sealed class BeforeCommitRepository(ProfileStore store,Action beforeCommit):IAdventureRepository
    {
        private Action? _before=beforeCommit;
        public SessionView Session=>store.Session;
        public ValueTask<BranchSnapshot> SnapshotAsync(CancellationToken cancellationToken=default)=>store.SnapshotAsync(cancellationToken);
        public ValueTask<DraftView?> GetDraftAsync(string taskId)=>store.GetDraftAsync(taskId);
        public ValueTask EnsureInventorySchemaAsync()=>store.EnsureInventorySchemaAsync();
        public ValueTask<DraftSaved> SaveAsync(SaveDraft command,CancellationToken cancellationToken=default)=>store.SaveAsync(command,cancellationToken);
        public ValueTask<WorldReceipt?> FindReceiptAsync(BranchId branch,CommandId command,CancellationToken cancellationToken=default)=>store.FindReceiptAsync(branch,command,cancellationToken);
        public ValueTask<CommandResult> ExecuteAsync(IWorldCommand command,CancellationToken cancellationToken=default)
        {
            var action=_before;_before=null;action?.Invoke();return store.ExecuteAsync(command,cancellationToken);
        }
    }
    public static void Register()
    {
        Add("CX-063","Twelve scoped milestones award exactly 350XP/275marks/2parts once; reload preserves four decorative souvenirs",()=>
        {
            using var s=Store("all-nodes");var service=new CompanionArcService(s,Catalog);Require(service.Definitions.Count==12&&service.Definitions.Values.Sum(n=>n.Xp)==350&&service.Definitions.Values.Sum(n=>n.Marks)==275&&service.Definitions.Values.Sum(n=>n.Parts)==2);
            Fixture(s,"explicit-public-state","explicit_engineering_fixture",new{},[Entity(s,"public-responsibility","responsibility",new{actor="CP-OREN",verified_signature=true}),Entity(s,"ending-qualification","ending_qualification",new{public_conditions_only=true})]);var original=Done(s.SnapshotAsync());
            foreach(var node in service.Definitions.Values.OrderBy(n=>n.Id,StringComparer.Ordinal))
            {
                var state=Prepare(s,service,node.Id);string? transport=null;ImmutableArray<string> contact=[];
                if(node.Id=="CQ-TESS-02")transport=Transport(s).Receipt;
                if(node.Id=="CQ-LINA-03")contact=[Consent(s,service,state,"CP-LINA",ContactPurpose.Correspondence),Consent(s,service,state,"N-LINA-BROTHER",ContactPurpose.Correspondence)];
                string result=Result(s,service,state,node.Outcomes[0],transport:transport,contact:contact);Success(Done(service.ConsumeResultAsync(result)));long seq=Done(s.SnapshotAsync()).Sequence;Require(Done(service.ConsumeResultAsync(result)).Status==CommitStatus.AlreadyCommitted&&Done(s.SnapshotAsync()).Sequence==seq);Done(s.ContinueAsync());Require(State(s,node.Id).Instance==state.Instance&&Done(service.ConsumeResultAsync(result)).Status==CommitStatus.AlreadyCommitted);
            }
            var after=Done(s.SnapshotAsync());Require(after.Balances["experience"]==350&&after.Balances["marks"]==275&&after.Balances["parts"]==2);Require(after.Entities.Count(e=>e.Kind=="souvenir")==4&&after.Items.Length==0&&after.Entities.All(e=>e.Kind!="roster"));
            foreach(var souvenir in after.Entities.Where(e=>e.Kind=="souvenir")){using var doc=JsonDocument.Parse(souvenir.Json);Require(doc.RootElement.GetProperty("decorative_only").GetBoolean());}
            Require(original.Entities.All(e=>after.Entities.Contains(e)));Require(after.Receipts.Count(r=>r.ActionType=="resolve_personal_work")==12&&after.Receipts.All(r=>!r.SemanticKey.StartsWith("ARC-",StringComparison.Ordinal)));
            string checkpoint=Done(s.CreateCheckpointAsync("manual",0));Done(s.LoadCheckpointAsync(checkpoint));Require(Done(s.SnapshotAsync()).Balances["experience"]==350&&Done(service.StatesAsync()).All(n=>n.Completed));
        });
        Add("CX-063","Late local/communication resume uses the same accepted instance without recruitment or reward",()=>
        {
            using var s=Store("resume");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-LINA-01");var before=Done(s.SnapshotAsync());Done(s.ContinueAsync());Success(Done(service.AcceptAsync(state.Node)));Require(State(s,state.Node).Instance==state.Instance&&Done(s.SnapshotAsync()).Balances.Count==0&&Done(s.SnapshotAsync()).Entities.All(e=>e.Kind!="roster"));
            string checkpoint=Done(s.CreateCheckpointAsync("manual",0));Done(s.LoadCheckpointAsync(checkpoint));Success(Done(service.AcceptAsync(state.Node)));Require(State(s,state.Node).Instance==state.Instance&&Done(s.SnapshotAsync()).Receipts.Count(r=>r.ActionType=="accept_private_work")==1&&Done(s.SnapshotAsync()).Entities.Contains(before.Entities.Single(e=>e.Kind=="personal_node")));
        });
        Add("CX-063","Missing gate/previous result or a relay person's invitation cannot start private work",()=>
        {
            using var s=Store("dependencies");var service=new CompanionArcService(s,Catalog);Invite(s,service,"CQ-LINA-01");RefusedWithoutMutation(s,()=>Done(service.AcceptAsync("CQ-LINA-01")));Fixture(s,"Q-202:resolution","explicit_engineering_fixture",new{});Invite(s,service,"CQ-LINA-02");RefusedWithoutMutation(s,()=>Done(service.AcceptAsync("CQ-LINA-02")));
            string forged=Receipt(s,"private_invitation_verified",new PrivateInvitationAuthority("N-JUNE","CQ-LINA-01",true));RefusedWithoutMutation(s,()=>Done(service.RegisterInvitationAsync(forged)));
        });
        Add("CX-063","Invitation withdrawal before confirmation blocks reward; new permission resumes the same work instance",()=>
        {
            using var s=Store("withdraw");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-MIRA-01");string result=Result(s,service,state,service.Definitions[state.Node].Outcomes[0]);Invite(s,service,state.Node,false);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(result)));RefusedWithoutMutation(s,()=>Done(service.AcceptAsync(state.Node)));Invite(s,service,state.Node,true);Success(Done(service.AcceptAsync(state.Node)));Require(State(s,state.Node).Instance==state.Instance);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(result)));string newResult=Result(s,service,state,service.Definitions[state.Node].Outcomes[0]);Success(Done(service.ConsumeResultAsync(newResult)));Invite(s,service,state.Node,false);Require(Done(service.ConsumeResultAsync(newResult)).Status==CommitStatus.AlreadyCommitted&&Done(s.SnapshotAsync()).Balances["experience"]==25);
        });
        Add("CX-063","A withdrawal between validation and SQLite commit invalidates the invitation read stamp",()=>
        {
            using var s=Store("withdraw-race");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-MIRA-01");string result=Result(s,service,state,"保管人中继");var race=new CompanionArcService(new BeforeCommitRepository(s,()=>Invite(s,service,state.Node,false)),Catalog);var commit=Done(race.ConsumeResultAsync(result));Require(commit.Status==CommitStatus.Rejected&&!State(s,state.Node).Completed&&Done(s.SnapshotAsync()).Balances.Count==0&&Done(s.SnapshotAsync()).Receipts.All(r=>r.SemanticKey!=state.Node+":resolution"));
        });
        Add("CX-063","Earlier unrelated work, wrong node, wrong instance and altered outcomes cannot serve as completion evidence",()=>
        {
            using var s=Store("scoped-source");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-MIRA-01");string outcome=service.Definitions[state.Node].Outcomes[0];var evidence=new PersonalWorkEvidence(state.Node,"CP-MIRA",state.Instance,outcome,Invitation(s,state.Node).AuthorityReceipt);
            foreach(var pair in new[]{("explicit_engineering_fixture",evidence),(CompanionArcService.RequiredWorkAction(state.Node),evidence with{Node="CQ-MIRA-02"}),(CompanionArcService.RequiredWorkAction(state.Node),evidence with{NodeInstance=Guid.NewGuid()}),(CompanionArcService.RequiredWorkAction(state.Node),evidence with{Outcome=service.Definitions[state.Node].Outcomes[1]})})
            {
                string work=Work(s,service,state,outcome,replacement:pair.Item2,action:pair.Item1);string result=Result(s,service,state,outcome,work);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(result)));
            }
            string valid=Result(s,service,state,outcome);Success(Done(service.ConsumeResultAsync(valid)));string rewritten=Result(s,service,state,service.Definitions[state.Node].Outcomes[1]);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(rewritten)));Require(State(s,state.Node).WorkReceipt==valid&&Done(s.SnapshotAsync()).Balances["experience"]==25);
        });
        Add("CX-063","Lina correspondence is accepted without meeting authority; correspondence authority cannot authorize meeting",()=>
        {
            using var s=Store("correspondence");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-LINA-03");ImmutableArray<string> contacts=[Consent(s,service,state,"CP-LINA",ContactPurpose.Correspondence),Consent(s,service,state,"N-LINA-BROTHER",ContactPurpose.Correspondence)];string meeting=Result(s,service,state,"双方同意后见面",meeting:contacts);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(meeting)));string correspondence=Result(s,service,state,"继续通信",contact:contacts);Success(Done(service.ConsumeResultAsync(correspondence)));Require(State(s,state.Node).Outcome=="继续通信"&&Done(s.SnapshotAsync()).Balances["experience"]==40);
        });
        Add("CX-063","Meeting requires both current personal consents; a withdrawn consent invalidates already prepared work",()=>
        {
            using var s=Store("meeting");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-LINA-03");string lina=Consent(s,service,state,"CP-LINA",ContactPurpose.Meeting);string brother=Consent(s,service,state,"N-LINA-BROTHER",ContactPurpose.Meeting);string duplicate=Result(s,service,state,"双方同意后见面",meeting:[lina,lina]);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(duplicate)));string result=Result(s,service,state,"双方同意后见面",meeting:[lina,brother]);Consent(s,service,state,"N-LINA-BROTHER",ContactPurpose.Meeting,false);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(result)));string renewed=Consent(s,service,state,"N-LINA-BROTHER",ContactPurpose.Meeting);string current=Result(s,service,state,"双方同意后见面",meeting:[lina,renewed]);Success(Done(service.ConsumeResultAsync(current)));Require(State(s,state.Node).Outcome=="双方同意后见面");
        });
        Add("CX-063","Meeting consent from a relay, another node or another instance is refused; explicit defer is a valid result",()=>
        {
            using var s=Store("contact-source");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-LINA-03");var valid=new PersonalContactConsent(state.Node,state.Instance,"N-LINA-BROTHER",ContactPurpose.Meeting,true);
            foreach(var c in new[]{valid with{Actor="N-JUNE"},valid with{Node="CQ-LINA-02"},valid with{NodeInstance=Guid.NewGuid()}}){string r=Receipt(s,"meeting_consent_verified",c);RefusedWithoutMutation(s,()=>Done(service.RegisterContactConsentAsync(r,ContactPurpose.Meeting)));}
            string result=Result(s,service,state,"明确暂缓联系并保存转交方式");Success(Done(service.ConsumeResultAsync(result)));Require(State(s,state.Node).Completed&&Done(s.SnapshotAsync()).Balances["experience"]==40);
        });
        Add("CX-063","Tess reuses Q603 delivery once without transport or parent rewards and rejects unrelated transport instances",()=>
        {
            using var s=Store("transport");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-TESS-02");var actual=Transport(s);var basis=new EscortSegmentResult("Q-603",actual.Quest.Instance,"PZ-Q603-LOAD",Guid.NewGuid(),"面板先到",true);
            foreach(var t in new[]{basis with{Quest="Q-602"},basis with{QuestInstance=Guid.NewGuid()},basis with{Puzzle="PZ-Q602-TRANSITION"},basis with{SegmentInstance=Guid.Empty},basis with{Completed=false},basis with{Outcome="档案先到"}})
            {
                string source=Receipt(s,"escort_segment_result",t);string result=Result(s,service,state,"面板先到",transport:source);RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(result)));
            }
            string valid=Result(s,service,state,"面板先到",transport:actual.Receipt);Success(Done(service.ConsumeResultAsync(valid)));Done(s.ContinueAsync());Require(Done(service.ConsumeResultAsync(valid)).Status==CommitStatus.AlreadyCommitted&&Done(s.SnapshotAsync()).Balances.Count==0&&State(s,state.Node).Completed&&Done(s.SnapshotAsync()).Entities.Single(e=>e.Id=="quest:Q-603").Json==StrictJson.Serialize(actual.Quest));
        });
        Add("CX-063","Unaccepted supplemental transport records no fabricated delivery and still permits the next local handover",()=>
        {
            using var s=Store("no-supplement");var service=new CompanionArcService(s,Catalog);var state=Prepare(s,service,"CQ-TESS-02");string missing=Result(s,service,state,"面板先到");RefusedWithoutMutation(s,()=>Done(service.ConsumeResultAsync(missing)));string noWork=Result(s,service,state,"未承担补充运输");Success(Done(service.ConsumeResultAsync(noWork)));Require(Done(s.SnapshotAsync()).Receipts.All(r=>r.ActionType!="escort_segment_result")&&Done(s.SnapshotAsync()).Balances.Count==0);var next=Prepare(s,service,"CQ-TESS-03");Success(Done(service.ConsumeResultAsync(Result(s,service,next,"具名本地交接并保持联络"))));Require(Done(s.SnapshotAsync()).Balances["experience"]==40&&Done(s.SnapshotAsync()).Entities.All(e=>e.Kind is not("regional_signature" or "transport_segment")));
        });
    }
}
