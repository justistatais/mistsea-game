using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.CompanionArcs;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.EventScheduler;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.LinaPersonalWorks;

namespace Mist.Application.LinaPersonalWorks;

public sealed class LinaPersonalWorkService(IAdventureRepository repository,RuntimeCatalog catalog,LinaWorkPackage package)
{
    public const string LetterTask="LINA-PERSONAL-RELAY-LETTER",ReplyId="lina-work:reply:received";
    public LinaWorkPackage Package=>package;
    private readonly Dictionary<Guid,(CommitPlanCommand Command,string Node,string Outcome)> _previews=[];
    private CompanionArcService Arcs()=>new(repository,catalog);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static T? Read<T>(BranchSnapshot s,string id) where T:class=>BranchOperations.Read<T>(s,id);
    private static string Id(string node)=>"lina-work:"+node;
    private static string InvitationId(string node)=>Id(node)+":invitation-source";
    private static string AccessId(string material)=>"lina-work:access:"+material;
    private PersonalNode Definition(string node)=>Arcs().Definitions.GetValueOrDefault(node) is{Actor:"CP-LINA"} d?d:throw new InvalidOperationException("未登记莉娜节点。");
    private void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);Require(BranchOperations.Entity(s,"final:preparation") is null,"起航准备已封存，本轮个人追加工作暂停。");
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"当前前景工作尚未交接，请先暂停或完成。");
        Require(!s.Entities.Any(e=>e.Id=="workpost:CP-LINA"||e.Id=="duty:CP-LINA"),"莉娜当前承担现场工作，先办理真实交接。");
    }
    private void At(BranchSnapshot s,string node)
    {
        var a=Read<ActorState>(s,"actor")??throw new InvalidOperationException("当前人物位置缺失。");
        (string Scene,Point2 Stand)[] stations=node switch
        {
            "CQ-LINA-01"=>[("R1-S08",new(23,75)),("HUB-S01",new(26,58))],
            "CQ-LINA-02"=>[("R2-S04",new(74,46)),("R2-O01",new(74,65)),("HUB-S02",new(57,35))],
            "CQ-LINA-03"=>[("R6-S02",new(26,65)),("HUB-S01",new(26,58))],_=>[]
        };
        Require(stations.Any(p=>p.Scene==a.Scene&&p.Stand.Distance(a.Position)<=2.5),"请先到本节点登记的档案或通信端：第一项为港口记录室副本/甲板值守点；第二项为修会范围台、回信台/船舱通信端；第三项为芬的通信台/甲板值守点。");
    }
    private LinaInvitationSource Invitation(BranchSnapshot s,string node)
    {
        var source=Read<LinaInvitationSource>(s,InvitationId(node));var invitation=Read<PrivateInvitation>(s,"private-invitation:"+node);
        Require(source is{Allowed:true}&&source.Version==package.Version&&invitation is{Allowed:true,Actor:"CP-LINA"}&&invitation.Node==node,"莉娜尚未许可或已撤回本节点的有限协助。");
        BranchOperations.Evidence(s,invitation!.AuthorityReceipt,"private_invitation_verified");return source!;
    }
    private LinaLocalNode State(BranchSnapshot s,string node,bool closed=false)
    {
        var st=Read<LinaLocalNode>(s,Id(node));var personal=Read<PersonalNodeState>(s,"personal:"+node);
        Require(st is not null&&personal is{Accepted:true}&&st.Instance==personal.Instance&&(closed||!personal.Completed),"当前个人节点尚未受理、实例不符或已收束。");return st!;
    }
    private void CurrentSource(BranchSnapshot s,string id)
    {
        var m=package.Materials.Single(x=>x.Id==id);var inv=Invitation(s,m.Node);var a=Read<LinaMaterialAccess>(s,AccessId(id));
        Require(a is not null&&a.Version==m.Version&&a.BodyHash==LinaWorkPackage.BodyHash(m)&&a.InvitationGeneration==inv.Generation,"当前原件尚未实际取得，或限定许可已变化。");
    }
    private CommitPlanCommand Plan(BranchSnapshot s,string key,string action,object result,ImmutableArray<EntityChange> changes,DraftStamp? draft=null,ImmutableArray<ItemChange> items=default)
        =>BranchOperations.Plan(repository,s,key,action,changes,s.Entities.Select(e=>e.Id).ToArray(),result,items:items,draft:draft);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private async ValueTask<string> ExecuteReceipt(CommitPlanCommand command)
    {
        var r=await repository.ExecuteAsync(command);Success(r);if(r.Receipt is{} receipt)return receipt.Id.Value.ToString();
        var s=await repository.SnapshotAsync();return s.Receipts.Single(x=>x.SemanticKey==command.Plan.SemanticKey).ReceiptId;
    }
    public async ValueTask<ImmutableArray<LinaLocalNode>> ViewAsync()=> (await repository.SnapshotAsync()).Entities.Where(e=>e.Kind=="lina_local_node").Select(e=>StrictJson.Deserialize<LinaLocalNode>(e.Json)).ToImmutableArray();
    public async ValueTask<CommandResult> InviteAsync(string node,bool allowed)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,node);var d=Definition(node);Require(BranchOperations.Receipt(s,d.MainGate)&&(d.Previous is null||BranchOperations.Receipt(s,d.Previous)),"先完成该节点的原公开门槛与前一项个人工作。");
        var old=Read<LinaInvitationSource>(s,InvitationId(node));long generation=(old?.Generation??0)+1;var next=new LinaInvitationSource(node,allowed,generation,package.Version,LinaWorkPackage.Hash(string.Join("\n",package.Materials.Where(m=>m.Node==node).Select(LinaWorkPackage.BodyHash))));
        string receipt=await ExecuteReceipt(Plan(s,Id(node)+":invitation:"+generation,"private_invitation_verified",new PrivateInvitationAuthority("CP-LINA",node,allowed),[BranchOperations.Change(s,InvitationId(node),"lina_invitation_source",next)]));
        return await Arcs().RegisterInvitationAsync(receipt);
    }
    public async ValueTask<CommandResult> AcceptAsync(string node)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,node);Invitation(s,node);Success(await Arcs().AcceptAsync(node));s=await repository.SnapshotAsync();var actual=Read<PersonalNodeState>(s,"personal:"+node)!;
        if(Read<LinaLocalNode>(s,Id(node)) is{} old){Require(old.Instance==actual.Instance,"旧节点不能移植到另一实例。");return new(CommitStatus.AlreadyCommitted,null,[]);}
        return await repository.ExecuteAsync(Plan(s,Id(node)+":accepted:"+actual.Instance,"lina_local_work_opened",new{node,instance=actual.Instance,recruited_required=false},[BranchOperations.Change(s,Id(node),"lina_local_node",new LinaLocalNode(node,actual.Instance,[]))]));
    }
    public async ValueTask<CommandResult> AcquireAsync(string material)
    {
        var s=await repository.SnapshotAsync();Safe(s);var m=package.Materials.Single(x=>x.Id==material);At(s,m.Node);var inv=Invitation(s,m.Node);var st=State(s,m.Node,true);
        Require(material!="LI02-DELIVERY"||st.LetterDispatched,"原询问信尚未实际转交，不能先取得送达回执。");
        Require(material!="LI03-MEETING"||st.ReplyVerified,"先核验本人回信，再明确询问本次见面可能性。");
        var next=st with{Acquired=st.Acquired.Contains(material)?st.Acquired:st.Acquired.Add(material),DeliveryAcknowledged=st.DeliveryAcknowledged||material=="LI02-DELIVERY"};
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,Id(m.Node),"lina_local_node",next));changes.Add(BranchOperations.Change(s,AccessId(material),"lina_material_access",new LinaMaterialAccess(material,m.Version,LinaWorkPackage.BodyHash(m),inv.Generation)));
        object result=new{material,version=m.Version,body_hash=LinaWorkPackage.BodyHash(m),purpose="same_private_personal_node_only",public_permission=false};string action="lina_personal_material_acquired",key=Id(m.Node)+":material:"+material+":"+inv.Generation;
        ImmutableArray<ItemChange> items=[];
        if(material=="LI02-DELIVERY")
        {
            string serial="LINA-LETTER:"+st.Instance;
            var item=s.Items.SingleOrDefault(i=>i.Id==serial);
            Require(item is{DefinitionId:"LINA-PRIVATE-LETTER",OwnerKind:"warehouse"}&&item.OwnerId is "LT17:protected-relay" or "LT17:sealed-recipient"&&st.LetterHash is not null&&st.RelayPath is not null,"当前有限原信的实际保管链缺失，不能取得另一封信的送达记录。");
            BranchOperations.Evidence(s,s.Receipts.Single(r=>r.SemanticKey==Id(st.Node)+":dispatch:"+st.Instance).ReceiptId,"lina_private_letter_dispatched");
            var acknowledgement=new LinaDeliveryAcknowledgement(st.Instance,serial,st.RelayPath!,st.LetterHash!,"sealed_LT17_recipient",m.Version,LinaWorkPackage.BodyHash(m));
            changes.Add(BranchOperations.Change(s,Id(st.Node)+":delivery","lina_delivery_acknowledgement",acknowledgement));
            items=[new(serial,item!.DefinitionId,"warehouse",item.OwnerId,"warehouse","LT17:sealed-recipient",null)];
            result=acknowledgement;action="lina_private_letter_delivery_acknowledged";
        }
        if(material=="LI03-REPLY")
        {
            var previous=Read<LinaLocalNode>(s,Id("CQ-LINA-02"));var delivery=Read<LinaDeliveryAcknowledgement>(s,Id("CQ-LINA-02")+":delivery");
            Require(previous is{DeliveryAcknowledged:true}&&delivery is not null&&delivery.NodeInstance==previous.Instance&&delivery.DraftHash==previous.LetterHash&&delivery.Path==previous.RelayPath&&s.Items.Any(i=>i.Id==delivery.Serial&&i.OwnerKind=="warehouse"&&i.OwnerId=="LT17:sealed-recipient"),"没有前一节点同一封原信的真实送达链。");
            items=[new(delivery!.Serial,"LINA-PRIVATE-LETTER","warehouse","LT17:sealed-recipient","warehouse","LT17:sealed-recipient",null)];
            key=Id(m.Node)+":reply-received:"+st.Instance+":"+inv.Generation;action="lina_personal_reply_received";var reply=new LinaReceivedReply(st.Instance,m.Id,m.Version,LinaWorkPackage.BodyHash(m),key,"N-LINA-BROTHER",["correspondence"]);changes.Add(BranchOperations.Change(s,ReplyId,"lina_received_reply",reply));result=reply;
        }
        return await repository.ExecuteAsync(Plan(s,key,action,result,changes.ToImmutable(),items:items));
    }
    public async ValueTask<LinaMaterial> OpenMaterialAsync(string id)
    {var s=await repository.SnapshotAsync();CurrentSource(s,id);return package.Materials.Single(m=>m.Id==id);}
    public async ValueTask<ImmutableArray<LinaMaterial>> AccessibleMaterialsAsync()
    {var s=await repository.SnapshotAsync();var result=ImmutableArray.CreateBuilder<LinaMaterial>();foreach(var m in package.Materials)try{CurrentSource(s,m.Id);result.Add(m);}catch(InvalidOperationException){}return result.ToImmutable();}
    public async ValueTask<CommandResult> VerifyTransferAsync(string transfer,string index,string timeScope,string currentStatus)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,"CQ-LINA-01");var st=State(s,"CQ-LINA-01");CurrentSource(s,"LI01-APPENDIX");CurrentSource(s,"LI01-INDEX");Require(transfer=="LT-17"&&index=="I-LT-17"&&timeScope=="historical_transfer_only"&&currentStatus=="unknown","当前两份原件只对应历史转移与索引；不能改写现址或当前存活。");
        return await repository.ExecuteAsync(Plan(s,Id(st.Node)+":comparison:"+st.Instance,"lina_transfer_appendix_compared",new{transfer,index,time_scope=timeScope,current_status=currentStatus},[BranchOperations.Change(s,Id(st.Node),"lina_local_node",st with{TransferChecked=true})]));
    }
    public async ValueTask<DraftSaved> SaveLetterAsync(DraftId? id,long revision,string body)
    {var s=await repository.SnapshotAsync();State(s,"CQ-LINA-02");var ss=repository.Session;return await repository.SaveAsync(new(ss.Profile,ss.Branch,ss.Load,LetterTask,id,revision,body,package.Version));}
    public async ValueTask<CommandResult> ConfirmLetterMeaningAsync(string recipient,string purpose,string address,string visit,bool assisted)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,"CQ-LINA-02");State(s,"CQ-LINA-02");CurrentSource(s,"LI02-RELAY");var draft=await repository.GetDraftAsync(LetterTask);Require(draft is{Deleted:false}&&!string.IsNullOrWhiteSpace(draft.Body),"请先保存本人的询问信正文。");
        Require(recipient=="sealed_LT17_recipient"&&purpose=="ask_current_contact_preference"&&address=="not_public"&&visit=="not_promised","询问信只询问当前联系意愿，不公开住址或承诺见面。");var meaning=new LinaLetterMeaning(recipient,purpose,address,visit,draft!.Hash!);
        return await repository.ExecuteAsync(Plan(s,Id("CQ-LINA-02")+":meaning:"+Guid.NewGuid(),"lina_letter_meaning_confirmed",new{meaning,structured_assistance=assisted,language_score=(decimal?)null},[BranchOperations.Change(s,Id("CQ-LINA-02")+":meaning","lina_letter_meaning",meaning)],new(LetterTask,draft.Id,draft.Revision,draft.Hash)));
    }
    public async ValueTask<CommandResult> DispatchLetterAsync(string path)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,"CQ-LINA-02");var st=State(s,"CQ-LINA-02");CurrentSource(s,"LI02-RELAY");Require(path is "custodian" or "direct","请明确选择有限中继或本人同意的直接书信。");if(path=="direct")CurrentSource(s,"LI02-DIRECT");var draft=await repository.GetDraftAsync(LetterTask);var meaning=Read<LinaLetterMeaning>(s,Id(st.Node)+":meaning");Require(draft is{Deleted:false}&&!string.IsNullOrWhiteSpace(draft.Body)&&meaning?.DraftHash==draft.Hash,"当前原稿修改后须重新确认意思，不发送旧稿。");
        if(st.LetterDispatched){Require(st.RelayPath==path&&st.LetterHash==draft!.Hash,"同一封实际送出的信不能在原节点中换路径或换稿重发。");return new(CommitStatus.AlreadyCommitted,null,[]);}
        string serial="LINA-LETTER:"+st.Instance;return await repository.ExecuteAsync(Plan(s,Id(st.Node)+":dispatch:"+st.Instance,"lina_private_letter_dispatched",new{node=st.Node,instance=st.Instance,serial,path,draft_hash=draft!.Hash,recipient="sealed_LT17_recipient",private_address_public=false},[BranchOperations.Change(s,Id(st.Node),"lina_local_node",st with{LetterDispatched=true,LetterHash=draft.Hash,RelayPath=path})],new(LetterTask,draft.Id,draft.Revision,draft.Hash),[new(serial,"LINA-PRIVATE-LETTER","new","","warehouse","LT17:protected-relay",null)]));
    }
    public async ValueTask<CommandResult> VerifyReplyAsync(string sender,string questionScope,bool addressPublic,bool meetingImplied)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,"CQ-LINA-03");var st=State(s,"CQ-LINA-03");CurrentSource(s,"LI03-REPLY");var reply=Read<LinaReceivedReply>(s,ReplyId);Require(reply?.NodeInstance==st.Instance&&sender=="N-LINA-BROTHER"&&questionScope=="current_correspondence_preference"&&!addressPublic&&!meetingImplied,"本人回复允许有限通信，不等于同意见面或公开其生活地址。");
        return await repository.ExecuteAsync(Plan(s,Id(st.Node)+":reply-verified:"+st.Instance,"lina_actual_reply_verified",new{node=st.Node,instance=st.Instance,source=reply!.ReceiptSemantic,sender,scope=questionScope,meeting=false},[BranchOperations.Change(s,Id(st.Node),"lina_local_node",st with{ReplyVerified=true})]));
    }
    public async ValueTask<CommandResult> ConfirmCurrentContactAsync(string actor,ContactPurpose purpose,bool pause=false)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,"CQ-LINA-03");var st=State(s,"CQ-LINA-03");Invitation(s,st.Node);Require(st.ReplyVerified&&actor is "CP-LINA" or "N-LINA-BROTHER"&&Enum.IsDefined(purpose),"先核验回信，联系意愿只能由这两位本人当前限定答复记录。");CurrentSource(s,"LI03-REPLY");if(purpose==ContactPurpose.Meeting)CurrentSource(s,"LI03-MEETING");
        var consent=new PersonalContactConsent(st.Node,st.Instance,actor,purpose,!pause);string receipt=await ExecuteReceipt(Plan(s,Id(st.Node)+":contact:"+actor+":"+purpose+":"+Guid.NewGuid(),purpose==ContactPurpose.Meeting?"meeting_consent_verified":"contact_consent_verified",consent,[]));return await Arcs().RegisterContactConsentAsync(receipt,purpose);
    }
    public async ValueTask<CommandResult> DecidePublicTopicAsync(string purpose,bool allowed)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,"CQ-LINA-03");var st=State(s,"CQ-LINA-03",true);Invitation(s,st.Node);Require(st.ReplyVerified&&purpose is "folding_only" or "keep_original_words","本人仅对折纸或保留原话这一有限话题作决定。");CurrentSource(s,"LI03-TOPICS");var reply=Read<LinaReceivedReply>(s,ReplyId)!;string receipt=s.Receipts.Single(r=>r.SemanticKey==reply.ReceiptSemantic&&r.ActionType=="lina_personal_reply_received").ReceiptId;string id="lina-work:public-topic:"+purpose;var old=Read<LinaPublicTopicScope>(s,id);var next=new LinaPublicTopicScope("CP-LINA",receipt,package.Version,allowed,purpose,purpose=="folding_only"?["patrol_technician"]:["patrol_technician","CP-MIRA"],(old?.Generation??0)+1);
        return await repository.ExecuteAsync(Plan(s,id+":"+next.Generation,"lina_public_topic_scope_decided",next,[BranchOperations.Change(s,id,"lina_public_topic_scope",next)]));
    }
    private ImmutableArray<string> CurrentContacts(BranchSnapshot s,LinaLocalNode st,ContactPurpose purpose)
    {
        var list=ImmutableArray.CreateBuilder<string>();foreach(string actor in new[]{"CP-LINA","N-LINA-BROTHER"}){var c=Read<CurrentContactConsent>(s,"personal-consent:"+st.Node+":"+actor+":"+purpose);Require(c is not null&&c.Consent is{Allowed:true}&&c.Consent.NodeInstance==st.Instance&&c.Consent.Purpose==purpose,"双方对当前用途的许可尚未分别记录或已撤回。");list.Add(c!.AuthorityReceipt);}return list.ToImmutable();
    }
    public async ValueTask<LinaWorkPreview> PreviewAsync(string node,string outcome)
    {
        var s=await repository.SnapshotAsync();Safe(s);At(s,node);var st=State(s,node);Invitation(s,node);Require(Definition(node).Outcomes.Contains(outcome),"该结局不属于原个人节点的有效结果。");Require(st.Outcome is null||st.Outcome==outcome,"已实际登记的个人工作不能换成另一种收束。");DraftStamp? stamp=null;
        if(node=="CQ-LINA-01"){CurrentSource(s,"LI01-APPENDIX");CurrentSource(s,"LI01-INDEX");Require(st.TransferChecked,"两份独立附页/索引的实际比较尚未完成。");}
        if(node=="CQ-LINA-02"){CurrentSource(s,"LI02-DELIVERY");Require(st.DeliveryAcknowledged&&st.LetterDispatched&&(outcome=="经保管人中继"?st.RelayPath=="custodian":st.RelayPath=="direct"),"实际转交路径与当前送达回执不符。");var d=await repository.GetDraftAsync(LetterTask);Require(d?.Hash==st.LetterHash,"送出后的当前稿不同，请保留原信记录而不是提交改稿。");stamp=new(LetterTask,d!.Id,d.Revision,d.Hash);}
        if(node=="CQ-LINA-03"){CurrentSource(s,"LI03-REPLY");Require(st.ReplyVerified,"当前本人回复还未核验。");if(outcome=="继续通信")CurrentContacts(s,st,ContactPurpose.Correspondence);if(outcome=="双方同意后见面"){CurrentSource(s,"LI03-MEETING");CurrentContacts(s,st,ContactPurpose.Meeting);}}
        ImmutableArray<ItemChange> held=[];
        if(node is "CQ-LINA-02" or "CQ-LINA-03")
        {
            var delivery=Read<LinaDeliveryAcknowledgement>(s,Id("CQ-LINA-02")+":delivery")??throw new InvalidOperationException("同一封实际原信的送达凭证缺失。");
            Require(s.Items.Any(i=>i.Id==delivery.Serial&&i.DefinitionId=="LINA-PRIVATE-LETTER"&&i.OwnerKind=="warehouse"&&i.OwnerId=="LT17:sealed-recipient"),"实际原信当前不在登记的受保护接收端。");
            held=[new(delivery.Serial,"LINA-PRIVATE-LETTER","warehouse","LT17:sealed-recipient","warehouse","LT17:sealed-recipient",null)];
        }
        var inv=Read<PrivateInvitation>(s,"private-invitation:"+node)!;var proof=new PersonalWorkEvidence(node,"CP-LINA",st.Instance,outcome,inv.AuthorityReceipt);string key=Id(node)+":work:"+st.Instance+":"+Invitation(s,node).Generation;var command=Plan(s,key,CompanionArcService.RequiredWorkAction(node),proof,[BranchOperations.Change(s,Id(node),"lina_local_node",st with{Outcome=outcome})],stamp,held);Guid token=Guid.NewGuid();_previews[token]=(command,node,outcome);return new(token,true,[],outcome);
    }
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)
    {
        if(!_previews.Remove(token,out var p))return new(CommitStatus.Rejected,null,[new("closed_preview","lina","预览已关闭，请按当前范围重新核对。")]);
        var committed=await repository.ExecuteAsync(p.Command);if(committed.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))return committed;
        var s=await repository.SnapshotAsync();var st=State(s,p.Node);var work=s.Receipts.Single(r=>r.SemanticKey==p.Command.Plan.SemanticKey).ReceiptId;var meeting=p.Outcome=="双方同意后见面"?CurrentContacts(s,st,ContactPurpose.Meeting):ImmutableArray<string>.Empty;var correspondence=p.Outcome=="继续通信"?CurrentContacts(s,st,ContactPurpose.Correspondence):ImmutableArray<string>.Empty;
        var result=new PersonalWorkResult(p.Node,"CP-LINA",p.Outcome,work,BothMeetingConsents:meeting.Length==2,MeetingConsentReceipts:meeting,NodeInstance:st.Instance,ContactConsentReceipts:correspondence);
        string receipt=await ExecuteReceipt(Plan(s,Id(p.Node)+":verified:"+st.Instance,"personal_work_verified",result,[BranchOperations.Change(s,Id(p.Node),"lina_local_node",st with{WorkReceipt=work})]));return await Arcs().ConsumeResultAsync(receipt);
    }
}
