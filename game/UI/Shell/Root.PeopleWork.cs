using Godot;
using System.Collections.Immutable;
using Mist.Application.UI;
using Mist.Application.Roster;
using Mist.Application.Knowledge;
using Mist.Application.Relationships;
using Mist.Application.Factions;
using Mist.Application.WorkOrders;
using Mist.Application.CompanionArcs;
using Mist.Application.Storage;
using Mist.Core.NPC;
using Mist.Core.Contracts;
using Mist.Core.Engineering;

public partial class Root
{
    private RosterService Roster()=>new(_store,_catalog);
    private async ValueTask OpenPeople()
    {
        var flow=Panel("人物与同行安排",InputOwner.Workspace);var projection=new KnowledgeProjection(_catalog);var snapshot=await _store.SnapshotAsync();
        var known=projection.Actors.Values.Where(p=>p.HomeScene[..2]==_view.Actor.Scene[..2]||snapshot.Entities.Any(e=>e.Id=="roster:"+p.Id||e.Id=="knowledge:"+p.Id)).OrderBy(p=>p.Id).ToArray();
        foreach(var person in known)Button(flow,person.Name,()=>Execute(async()=>await OpenPerson(person.Id)));
        var roster=Roster();var formation=roster.Helpers(snapshot);Label(flow,"当前两席："+string.Join("、",formation.Select(HelperName)),22);var selection=new Dictionary<string,CheckBox>();
        foreach(string id in RosterService.Auxiliary.Concat((await roster.ViewAsync()).Where(v=>v.Entry.Membership==Membership.Joined&&v.Entry.Availability==ActorAvailability.ShipStandby).Select(v=>v.Entry.Actor)))
        {
            var check=new CheckBox{Text=HelperName(id),ButtonPressed=formation.Contains(id),AccessibilityName="下次遭遇同行选择："+HelperName(id)};flow.AddChild(check);selection[id]=check;
        }
        Button(flow,"确认下次遭遇的两席安排",()=>Execute(async()=>{CheckCommit(await roster.SelectAsync(selection.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray()));ClosePanel();await OpenPeople();}));
        Label(flow,"名册和装备在战斗中保持只读。空席由地方援勤补足。",18);
    }
    private string HelperName(string id)=>id switch{"AUX-GUARD"=>"地方护卫","AUX-SUPPORT"=>"地方支援",_=>new KnowledgeProjection(_catalog).Actors.GetValueOrDefault(id)?.Name??id};
    private async ValueTask OpenPerson(string id)
    {
        var service=new KnowledgeService(_store,_catalog);var identity=service.Projection.Actors[id];var flow=Panel(identity.Name,InputOwner.Dialogue);
        var topics=await service.TopicsAsync(id);if(topics.Length==0)Label(flow,KnowledgeProjection.NeutralReply,20);
        foreach(var topic in topics){Label(flow,topic.Text,20);Label(flow,"时间范围："+topic.TimeScope+" · 来源数："+topic.SourceRoots.Length,16);}
        var relationship=await new RelationshipService(_store,_catalog).ViewAsync(id);Label(flow,"工作合作："+(relationship.Work switch{WorkRelationship.Unestablished=>"尚未建立",WorkRelationship.Verified=>"工作已核实",WorkRelationship.Ongoing=>"持续协作",_=>"待修复"}),20);
        Label(flow,"私人交流："+(relationship.Private switch{PrivateRelationship.NotInvited=>"未邀约",PrivateRelationship.Invited=>"愿意谈",_=>"暂不谈"}),20);
        foreach(var issue in relationship.Issues)Label(flow,issue.Key+"："+issue.Value,18);
        if(id=="CP-LINA")Button(flow,"莉娜当前受邀个人工作",()=>Execute(OpenLinaWorkList));
        if(identity.Role!="companion")return;var roster=Roster();var view=(await roster.ViewAsync()).Single(v=>v.Entry.Actor==id);
        Label(flow,"同行身份："+(view.Entry.Membership switch{Membership.Joined=>"已加入",Membership.Invited=>"收到邀请",_=>"地方联系人"})+" · 当前共享等级 "+view.SharedLevel,20);
        if(view.InvitationAvailable&&view.Entry.Membership==Membership.Contact)Button(flow,"提出正式同行邀请",()=>Execute(async()=>{CheckCommit(await roster.InviteAsync(id));ClosePanel();await OpenPerson(id);}));
        if(view.Entry.Membership==Membership.Invited)
        {
            Button(flow,"确认加入同行名册",()=>Execute(async()=>{CheckCommit(await roster.DecideAsync(id,true));ClosePanel();await OpenPerson(id);}));Button(flow,"暂不加入，保留再邀请入口",()=>Execute(async()=>{CheckCommit(await roster.DecideAsync(id,false));ClosePanel();await OpenPerson(id);}));
        }
        if(view.Entry.Membership==Membership.Joined)
        {
            foreach(int level in view.AvailableTactics)
            {
                var row=new HFlowContainer();flow.AddChild(row);Label(row,level+"级战术："+view.Entry.Tactics.GetValueOrDefault(level,"尚未选择"),18);foreach(string option in new[]{"A","B"})Button(row,"确认 "+option,()=>Execute(async()=>{CheckCommit(await roster.ChooseTacticsAsync(id,view.Entry.Tactics.SetItem(level,option)));ClosePanel();await OpenPerson(id);}));
            }
            Button(flow,"申请下个安全点离队",()=>Execute(async()=>{CheckCommit(await roster.RequestDepartureAsync(id));ClosePanel();await OpenPerson(id);}));
            if(view.Entry.Availability==ActorAvailability.PendingSafeDeparture)Button(flow,"确认安全交接并归还收藏遗物",()=>Execute(async()=>{CheckCommit(await roster.FinishDepartureAsync(id));ClosePanel();await OpenPerson(id);}));
        }
        var arcs=new CompanionArcService(_store,_catalog);var states=(await arcs.StatesAsync()).Where(n=>arcs.Definitions[n.Node].Actor==id).ToArray();
        foreach(var node in states)Label(flow,arcs.Definitions[node.Node].Boundary+"\n状态："+(node.Completed?"已取得实际收束":"已承担，可暂停"),18);
    }
    private async ValueTask OpenWorkOrders()
    {
        var flow=Panel("工作、协议与准备",InputOwner.Workspace);var snapshot=await _store.SnapshotAsync();var factions=new FactionService(_store,_catalog);
        Label(flow,"合作请求按授权、实际回执和容量核对。",20);foreach(var view in await factions.CooperationAsync())
        {
            string name=_catalog.Definitions["factions"].Single(f=>f.Id==view.Faction).Name;Label(flow,name+"："+(view.Ready?"当前范围可用":"条件待补齐"),20);
        }
        if(!snapshot.Receipts.Any(r=>r.SemanticKey=="Q-404:resolution")){Label(flow,"六塔准备清单会在相应调查与技术核验后开放。当前可继续港口委托。",20);return;}
        var service=new WorkOrderService(_store,_catalog);foreach(var tower in await service.ReadinessAsync())
        {
            Label(flow,tower.Tower+" · 现场安全 "+(tower.Safe?"成立":"未核验")+" · 基础保全 "+(tower.Baseline?"已核验":"待工作回执"),22);
            Label(flow,"增强配置："+(tower.EnhancedVersion??"未安装")+" · 过渡供给："+(tower.Transition?"已交付":"未交付")+" · 试运行："+(tower.Trial?"当前版本有效":"尚未有效"),18);
        }
        foreach(var entity in snapshot.Entities.Where(e=>e.Kind=="work_order"))
        {
            var work=StrictJson.Deserialize<TowerWork>(entity.Json);Label(flow,work.Tower+" · "+work.Type+" · "+work.Stage+"\n"+string.Join("、",work.Resources.Select(r=>r.Key+" × "+r.Value)),20);
            if(work.Stage is WorkStage.Reserved or WorkStage.InTransit or WorkStage.Delivered or WorkStage.Installed)Button(flow,"明确执行下一工作步骤",()=>Execute(async()=>{CheckCommit(await service.NextStepAsync(work.Tower,work.Type));ClosePanel();await OpenWorkOrders();}));
            if(work.Stage==WorkStage.Reserved)Button(flow,"取消尚未执行的预留",()=>Execute(async()=>{CheckCommit(await service.CancelAsync(work.Tower,work.Type));ClosePanel();await OpenWorkOrders();}));
        }
        foreach(var region in await factions.AgreementsAsync())Label(flow,region.Region+" · 操作方 "+(region.Operator is null?"未确认":"已确认")+" / 代表方 "+(region.Representative is null?"未确认":"已确认")+" · "+region.ProtocolVersion,20);
    }
}
