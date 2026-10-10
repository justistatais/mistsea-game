using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Quests;

namespace Mist.Application.CompanionArcs;

public sealed record PersonalNode(string Id,string Actor,string MainGate,string? Previous,int Xp,long Marks,int Parts,ImmutableArray<string> Scenes,ImmutableArray<string> Outcomes,string Boundary);
public sealed record PrivateInvitation(string Actor,string Node,bool Allowed,string AuthorityReceipt);
public sealed record PrivateInvitationAuthority(string Actor,string Node,bool Allowed);
public enum ContactPurpose { Correspondence,Meeting }
public sealed record PersonalContactConsent(string Node,Guid NodeInstance,string Actor,ContactPurpose Purpose,bool Allowed);
public sealed record CurrentContactConsent(PersonalContactConsent Consent,string AuthorityReceipt);
public sealed record PersonalWorkEvidence(string Node,string Actor,Guid NodeInstance,string Outcome,string InvitationReceipt,string? TransportResultReceipt=null);
public sealed record PersonalWorkResult(string Node,string Actor,string Outcome,string WorkReceipt,bool BothMeetingConsents=false,string? TransportResultReceipt=null,ImmutableArray<string> MeetingConsentReceipts=default,Guid NodeInstance=default,ImmutableArray<string> ContactConsentReceipts=default);
public sealed record PersonalNodeState(string Node,Guid Instance,bool Accepted,bool Completed,string? Outcome=null,string? WorkReceipt=null);
public sealed record EscortSegmentResult(string Quest,Guid QuestInstance,string Puzzle,Guid SegmentInstance,string Outcome,bool Completed);

// This consumes scoped, verified work. Narrative scenes and physical work producers must supply
// their own actual receipts; accepting a node or opening its view never manufactures completion.
public sealed class CompanionArcService(IAdventureRepository repository,RuntimeCatalog catalog)
{
    public ImmutableDictionary<string,PersonalNode> Definitions {get;}=catalog.Definitions["personal_nodes"].Select(d=>
    {
        using var doc=JsonDocument.Parse(d.PayloadJson);var j=doc.RootElement;return new PersonalNode(d.Id,j.GetProperty("companion").GetString()!,j.GetProperty("main_gate").GetString()!,j.GetProperty("personal_prerequisite").GetString(),j.GetProperty("xp").GetInt32(),j.GetProperty("marks").GetInt64(),j.GetProperty("parts").GetInt32(),j.GetProperty("scenes").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray(),j.GetProperty("accepted_outcomes").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray(),j.GetProperty("fixed_boundary").GetString()!);
    }).ToImmutableDictionary(n=>n.Id);
    private static readonly ImmutableDictionary<string,string> WorkActions=new Dictionary<string,string>
    {
        ["CQ-LINA-01"]="personal_transfer_appendix_verified",["CQ-LINA-02"]="personal_relay_delivered",["CQ-LINA-03"]="personal_contact_arrangement_verified",
        ["CQ-MIRA-01"]="personal_access_list_verified",["CQ-MIRA-02"]="personal_review_delivery_verified",["CQ-MIRA-03"]="personal_custody_drill_verified",
        ["CQ-OREN-01"]="personal_alarm_scope_verified",["CQ-OREN-02"]="personal_alarm_repair_verified",["CQ-OREN-03"]="personal_maintenance_handover_verified",
        ["CQ-TESS-01"]="personal_receiving_contacts_verified",["CQ-TESS-02"]="personal_transport_response_verified",["CQ-TESS-03"]="personal_notification_handover_verified"
    }.ToImmutableDictionary(StringComparer.Ordinal);
    public static string RequiredWorkAction(string node)=>WorkActions.TryGetValue(node,out var action)?action:throw new InvalidOperationException("该个人工作没有登记有效工作类型。");
    private PersonalNode Node(string id)=>Definitions.TryGetValue(id,out var node)?node:throw new InvalidOperationException("该个人节点不属于当前内容。");
    private static string ConsentId(string node,string actor,ContactPurpose purpose)=>"personal-consent:"+node+":"+actor+":"+purpose;
    private static string ConsentAction(ContactPurpose purpose)=>purpose==ContactPurpose.Meeting?"meeting_consent_verified":"contact_consent_verified";
    private static PrivateInvitation Invitation(BranchSnapshot s,PersonalNode node)
    {
        var invitation=BranchOperations.Read<PrivateInvitation>(s,"private-invitation:"+node.Id);
        if(invitation is not{Allowed:true}||invitation.Actor!=node.Actor||invitation.Node!=node.Id)throw new InvalidOperationException("需要本人当前愿意开展这项私人工作的实际许可。");
        var source=BranchOperations.Evidence(s,invitation.AuthorityReceipt,"private_invitation_verified");var authority=StrictJson.Deserialize<PrivateInvitationAuthority>(source.ResultJson);
        if(authority.Actor!=node.Actor||authority.Node!=node.Id||!authority.Allowed)throw new InvalidOperationException("该邀约不能由中继人代签，也不能沿用已撤回的许可。");
        return invitation;
    }
    public async ValueTask<ImmutableArray<PersonalNodeState>> StatesAsync(){var s=await repository.SnapshotAsync();return s.Entities.Where(e=>e.Kind=="personal_node").Select(e=>StrictJson.Deserialize<PersonalNodeState>(e.Json)).ToImmutableArray();}
    public async ValueTask<CommandResult> RegisterInvitationAsync(string authorityReceipt)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var r=BranchOperations.Evidence(s,authorityReceipt,"private_invitation_verified");var a=StrictJson.Deserialize<PrivateInvitationAuthority>(r.ResultJson);var node=Node(a.Node);
        if(a.Actor!=node.Actor)throw new InvalidOperationException("私人邀约与撤回均须来自本人。");
        var invitation=new PrivateInvitation(a.Actor,a.Node,a.Allowed,authorityReceipt);
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"personal-invitation:"+authorityReceipt,"record_private_invitation",[BranchOperations.Change(s,"private-invitation:"+node.Id,"private_invitation",invitation)],["private-invitation:"+node.Id],new{node=node.Id,allowed=a.Allowed,authority_receipt=authorityReceipt}));
    }
    public async ValueTask<CommandResult> RegisterContactConsentAsync(string authorityReceipt,ContactPurpose purpose)
    {
        if(!Enum.IsDefined(purpose))throw new InvalidOperationException("未登记的联系许可类型。");
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var r=BranchOperations.Evidence(s,authorityReceipt,ConsentAction(purpose));var consent=StrictJson.Deserialize<PersonalContactConsent>(r.ResultJson);
        var node=Node(consent.Node);Invitation(s,node);var state=BranchOperations.Read<PersonalNodeState>(s,"personal:"+node.Id);
        if(node.Id!="CQ-LINA-03"||state is not{Accepted:true,Completed:false}||state.Instance!=consent.NodeInstance||consent.Purpose!=purpose||consent.Actor is not("CP-LINA" or "N-LINA-BROTHER"))throw new InvalidOperationException("联系许可必须指向当前实际节点与其中一位本人。");
        string id=ConsentId(node.Id,consent.Actor,purpose);
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"personal-consent:"+authorityReceipt,"record_personal_contact_consent",[BranchOperations.Change(s,id,"personal_contact_consent",new CurrentContactConsent(consent,authorityReceipt))],["personal:"+node.Id,"private-invitation:"+node.Id,id],new{node=node.Id,actor=consent.Actor,purpose,allowed=consent.Allowed,authority_receipt=authorityReceipt}));
    }
    public async ValueTask<CommandResult> AcceptAsync(string nodeId)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var node=Node(nodeId);var old=BranchOperations.Read<PersonalNodeState>(s,"personal:"+nodeId);
        if(old?.Completed==true)return new(CommitStatus.AlreadyCommitted,null,[]);
        if(!BranchOperations.Receipt(s,node.MainGate)||node.Previous is not null&&!BranchOperations.Receipt(s,node.Previous+":resolution"))throw new InvalidOperationException("该个人工作所需的公开节点与前项结果尚未成立。");
        Invitation(s,node);var next=old??new(nodeId,Guid.NewGuid(),true,false);
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"personal:"+nodeId+":accept","accept_private_work",[BranchOperations.Change(s,"personal:"+nodeId,"personal_node",next)],["personal:"+nodeId,"private-invitation:"+nodeId],new{node=nodeId,instance=next.Instance}));
    }
    private static void ValidateContact(BranchSnapshot s,PersonalNodeState state,ImmutableArray<string> receiptIds,ContactPurpose purpose,long workSequence,List<string> reads)
    {
        if(receiptIds.IsDefault||receiptIds.Length!=2||receiptIds.Distinct(StringComparer.Ordinal).Count()!=2)throw new InvalidOperationException("须分别记录双方本人对当前联系形式的实际同意。");
        var people=new HashSet<string>(StringComparer.Ordinal);
        foreach(string id in receiptIds)
        {
            var source=BranchOperations.Evidence(s,id,ConsentAction(purpose));var consent=StrictJson.Deserialize<PersonalContactConsent>(source.ResultJson);string entityId=ConsentId(state.Node,consent.Actor,purpose);var current=BranchOperations.Read<CurrentContactConsent>(s,entityId);
            if(source.Sequence>=workSequence||consent.Node!=state.Node||consent.NodeInstance!=state.Instance||consent.Purpose!=purpose||!consent.Allowed||current is null||current.AuthorityReceipt!=id||current.Consent!=consent)throw new InvalidOperationException("许可已撤回、过期或未覆盖当前实际工作。");
            people.Add(consent.Actor);reads.Add(entityId);
        }
        if(!people.SetEquals(new[]{"CP-LINA","N-LINA-BROTHER"}))throw new InvalidOperationException("中继人不能代替双方同意通信或见面。");
    }
    public async ValueTask<CommandResult> ConsumeResultAsync(string resultReceipt)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var r=BranchOperations.Evidence(s,resultReceipt,"personal_work_verified");var work=StrictJson.Deserialize<PersonalWorkResult>(r.ResultJson);var node=Node(work.Node);var old=BranchOperations.Read<PersonalNodeState>(s,"personal:"+work.Node)??throw new InvalidOperationException("当前个人工作尚未被接受。");
        if(!old.Accepted||old.Instance==Guid.Empty||work.NodeInstance!=old.Instance||work.Actor!=node.Actor||!node.Outcomes.Contains(work.Outcome))throw new InvalidOperationException("本人、节点实例或有效收束不符。");
        if(old.Completed)
        {
            if(old.WorkReceipt!=resultReceipt)throw new InvalidOperationException("已收束的个人工作不能改用另一结果重写。");
            return new(CommitStatus.AlreadyCommitted,null,[]);
        }
        var invitation=Invitation(s,node);var source=BranchOperations.Evidence(s,work.WorkReceipt,RequiredWorkAction(node.Id));var evidence=StrictJson.Deserialize<PersonalWorkEvidence>(source.ResultJson);
        if(source.Sequence>=r.Sequence||BranchOperations.Evidence(s,invitation.AuthorityReceipt,"private_invitation_verified").Sequence>=source.Sequence||evidence.Node!=node.Id||evidence.Actor!=node.Actor||evidence.NodeInstance!=old.Instance||evidence.Outcome!=work.Outcome||evidence.InvitationReceipt!=invitation.AuthorityReceipt||evidence.TransportResultReceipt!=work.TransportResultReceipt)throw new InvalidOperationException("实际工作来源未覆盖当前节点、收束和本人许可。");
        var reads=new List<string>{"personal:"+node.Id,"private-invitation:"+node.Id};
        if(node.Id=="CQ-LINA-03")
        {
            if(work.Outcome=="双方同意后见面")
            {
                if(!work.BothMeetingConsents)throw new InvalidOperationException("通信许可不能替代双方的实际见面同意。");
                ValidateContact(s,old,work.MeetingConsentReceipts,ContactPurpose.Meeting,source.Sequence,reads);
            }
            else if(work.Outcome=="继续通信")ValidateContact(s,old,work.ContactConsentReceipts,ContactPurpose.Correspondence,source.Sequence,reads);
        }
        if(node.Id=="CQ-TESS-02")
        {
            if(work.Outcome=="未承担补充运输"&&work.TransportResultReceipt is not null||work.Outcome!="未承担补充运输"&&work.TransportResultReceipt is null)throw new InvalidOperationException("补充运输收束须符合真实的承担与交付记录。");
            if(work.TransportResultReceipt is not null)
            {
                var transport=BranchOperations.Evidence(s,work.TransportResultReceipt,"escort_segment_result");var result=StrictJson.Deserialize<EscortSegmentResult>(transport.ResultJson);var quest=BranchOperations.Read<QuestState>(s,"quest:Q-603");
                if(quest is null||quest.Id!="Q-603"||quest.Instance==Guid.Empty||quest.Lifecycle!=QuestLifecycle.Closed||quest.Outcome is not(QuestOutcome.Completed or QuestOutcome.CompletedWithConsequence)||result.Quest!="Q-603"||result.QuestInstance!=quest.Instance||result.Puzzle!="PZ-Q603-LOAD"||result.SegmentInstance==Guid.Empty||!result.Completed||result.Outcome!=work.Outcome||transport.Sequence>=source.Sequence)throw new InvalidOperationException("已有运输回执未覆盖当前Q-603实际实例和收货顺序。");
                reads.Add("quest:Q-603");
            }
        }
        var next=old with{Completed=true,Outcome=work.Outcome,WorkReceipt=resultReceipt};var balances=new[]{new BalanceChange("experience",node.Xp),new BalanceChange("marks",node.Marks),new BalanceChange("parts",node.Parts)}.Where(b=>b.Delta!=0).ToImmutableArray();
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"personal:"+node.Id,"personal_node",next));
        if(node.Id.EndsWith("03",StringComparison.Ordinal))changes.Add(BranchOperations.Change(s,"souvenir:"+node.Actor,"souvenir",new{actor=node.Actor,source_node=node.Id,decorative_only=true}));
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,node.Id+":resolution","resolve_personal_work",changes.ToImmutable(),reads.ToArray(),new{node=node.Id,instance=old.Instance,outcome=work.Outcome,work_receipt=resultReceipt},balances:balances));
    }
}
