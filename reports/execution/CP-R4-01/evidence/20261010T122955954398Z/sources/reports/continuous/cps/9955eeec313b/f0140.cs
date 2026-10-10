using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Application.ChapterThree;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterThree;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.ScopedPersonalWorks;

namespace Mist.Application.ScopedPersonalWorks;

public sealed partial class ScopedPersonalWorksService
{
    public const string IncomingOrenWorker="ROLE-R3-AUX-NEXT-WATCH";
    public async ValueTask<ScopedPersonalResponse> OpenOrenReceivingResponseAsync(string actor)
    {
        var s=await _repository.SnapshotAsync();Require(BranchOperations.Read<ChapterFiveState>(s,"chapter5") is{OrenInvited:true},"当前本人有限访问邀请已撤回。");Material(s,"R5-CQOREN03-R-DUTY");var r=BranchOperations.Read<OrenNamedReceiving>(s,"personal-detail:oren03-receiving")??throw new InvalidOperationException("具名接收尚未实际发生。");Require(r.NodeInstance==Quest(s,"CQ-OREN-03").Instance&&r.Version==Package.Version&&r.InvitationReceipt==PersonalProductionRules.CurrentInvitationReceipt(s,"CQ-OREN-03")&&s.Receipts.Any(x=>x.ActionType=="oren_named_incoming_instructions_received"&&x.ResultJson==StrictJson.Serialize(r)),"本次实际具名接收来源或邀请世代不符。");return Package.Responses.Single(x=>x.Actor==actor&&(actor is "ROLE-R3-LOCAL-MAINTENANCE" or "ROLE-R3-REMOTE-REVIEW")&&r.ResponseHashes.GetValueOrDefault(x.Id)==x.Fingerprint);
    }
    private static (AuxiliaryRepair Repair,StoredReceipt Review) OrenSource(BranchSnapshot s)
    {
        var state=BranchOperations.Read<ChapterThreeState>(s,"chapter3");var repair=BranchOperations.Read<AuxiliaryRepair>(s,"R3:aux_repair");var invitation=BranchOperations.Read<AlarmReviewInvitation>(s,"R3:alarm_review_invitation");
        var review=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="R3:alarm_independent_review"&&r.ActionType=="chapter_three_alarm_independently_reviewed");
        Require(state is{AlarmRepair:true,AlarmReviewed:true}&&repair is{TestPassed:true,Stage:"ready_for_independent_review"}&&repair.Configuration==state.AlarmRevision&&state.AlarmReviewRevision==state.AlarmRevision&&invitation is{Reviewer:"N-PERA"}&&invitation.Configuration==state.AlarmRevision&&review is not null&&Quest(s,"CQ-OREN-02").Lifecycle==Mist.Core.Quests.QuestLifecycle.Closed&&s.Receipts.Any(r=>r.SemanticKey=="CQ-OREN-02:resolution"&&r.ActionType=="chapter_three_auxiliary_review_received"),"真实同一辅助报警器、当前配置和独立复核缺失；前节点fixture不能替代维修。");
        Require(s.Items.Any(i=>i.Id==repair!.Kit&&i.OwnerKind=="installed"&&i.OwnerId=="R3:auxiliary_alarm"),"同一独立旁修套件不在原设备。");return(repair!,review!);
    }
    private OrenMaintenancePost OrenPost(BranchSnapshot s,bool invitation=true)
    {
        if(invitation){Current(s,"CQ-OREN-03");Material(s,"R5-CQOREN03-R-DUTY");}
        var p=BranchOperations.Read<OrenMaintenancePost>(s,"R5:oren_duty");Require(p is{Actor:"CP-OREN",Status:"accepted_current",Receiver:"ROLE-R3-LOCAL-MAINTENANCE",Supervisor:"ROLE-R3-REMOTE-REVIEW",Window:"next_safe_reassignment",OriginalResponsibilityPreserved:true}&&BranchOperations.Entity(s,"workpost:CP-OREN") is{Kind:"work_post"},"原有限岗位未实际接受或已交接。");return p!;
    }
    private static string GeometryHash(SceneGeometry g)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StrictJson.Serialize(g)))).ToLowerInvariant();
    // Every middle scene is searched from its actual entry to its exit. Missing or unsafe geometry rejects.
    private static ImmutableArray<OrenActualWalk> WalkRoute(ImmutableDictionary<string,SceneGeometry> geo,string start,Point2 from,string target,Point2 to)
    {
        Require(geo.ContainsKey(start)&&geo.ContainsKey(target),"奥伦当前出发/目的几何缺失。");
        var queue=new Queue<(string Scene,Point2 Entry,ImmutableArray<OrenActualWalk> Legs)>();queue.Enqueue((start,from,[]));var visited=new HashSet<string>();
        while(queue.TryDequeue(out var node))
        {
            string key=node.Scene+":"+node.Entry.X+":"+node.Entry.Y;if(!visited.Add(key))continue;var grid=new NavigationGrid(geo[node.Scene]);
            if(node.Scene==target){var last=grid.FindPath(node.Entry,to,true);if(last.Length>0)return node.Legs.Add(new(node.Scene,node.Entry,node.Scene,to,"","",last));}
            foreach(var port in geo[node.Scene].Ports)
            {
                if(!port.TargetScene.StartsWith("R3",StringComparison.Ordinal)&&!port.TargetScene.StartsWith("HUB",StringComparison.Ordinal)||!geo.TryGetValue(port.TargetScene,out var next))continue;
                var back=next.Ports.SingleOrDefault(p=>p.Id==port.TargetPort&&p.TargetScene==node.Scene&&p.TargetPort==port.Id);if(back is null)continue;
                var path=grid.FindPath(node.Entry,port.Stand,true);if(path.Length==0||!new NavigationGrid(next).CanStand(back.Stand,true))continue;
                queue.Enqueue((next.Id,back.Stand,node.Legs.Add(new(node.Scene,node.Entry,next.Id,back.Stand,port.Id,back.Id,path))));
            }
        }
        throw new InvalidOperationException("当前安全几何没有实际到场/返程路径；不将缺失区域猜成可达。");
    }
    private async ValueTask<OrenActualPosition> OrenLocalWalk(BranchSnapshot s,Guid instance,bool returning)
    {
        var geo=await _currentGeometry();var roster=BranchOperations.Read<RosterEntry>(s,"roster:CP-OREN")??throw new InvalidOperationException("当前实际名册缺失。");var original=BranchOperations.Read<RosterEntry>(s,"R5:oren_before_duty")??throw new InvalidOperationException("实际工作前名册缺失。");
        Require(new[]{"Q-301","Q-302","Q-303","Q-304"}.All(q=>Quest(s,q).Lifecycle==Mist.Core.Quests.QuestLifecycle.Closed)&&s.Receipts.Any(r=>r.SemanticKey=="Q-304:resolution"&&r.ActionType=="chapter_three_tower_handover_received"),"第三章当前合法地区路径还未全部核验，不为地方轮值绕过门禁。");
        var stand=geo["R3-S08"].Spots.Single(p=>p.Id=="R3-S08.auxiliary_receipt").Stand;string target=returning?original.Location:"R3-S08";string source=returning?"R3-S08":roster.Location;
        var legs=ImmutableArray.CreateBuilder<OrenActualWalk>();Point2 current=stand;string shipId="";
        if(!returning)
        {
            if(source=="R3-S03")current=geo[source].Spots.Single(p=>p.Id=="R3-S03.oren").Stand;
            else if(source.StartsWith("HUB",StringComparison.Ordinal))
            {
                var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("同船缺失。");var berth=BranchOperations.Read<VesselBerth>(s,"berth:R3");
                Require(original.Membership==Membership.Joined&&ship.DockRegion=="R3"&&berth is not null&&berth.Instance==ship.Instance&&BranchOperations.Entity(s,"vessel:interior:"+ship.Instance) is not null&&s.Receipts.Any(r=>r.ActionType=="accept_invitation"&&r.ResultJson.Contains("CP-OREN",StringComparison.Ordinal)),"同行源、同船实际R3泊位与空间绑定缺失，不能从船舱直接把奥伦写到R3。");shipId=ship.Instance.ToString();
                // A source spot must come from actual last presence, or the source-backed initial gangway placement.
                var presence=BranchOperations.Read<Mist.Core.VesselLife.BanterPresence>(s,"banter-presence:CP-OREN");
                current=presence is not null&&presence.Scene==source&&presence.VesselInstance==ship.Instance?presence.Position:source=="HUB-S01"?geo[source].Spots.Single(p=>p.Id=="HUB-S01.gangway").Stand:throw new InvalidOperationException("奥伦当前船舱缺实际站位来源。");
                var gangway=geo["HUB-S01"].Spots.Single(p=>p.Id=="HUB-S01.gangway").Stand;legs.AddRange(WalkRoute(geo,source,current,"HUB-S01",gangway));
                Require(geo.TryGetValue(berth!.Scene,out var land)&&new NavigationGrid(land).CanStand(berth.Landing,true),"同船泊位落点不安全。");legs.Add(new("HUB-S01",gangway,berth.Scene,berth.Landing,"same_vessel_gangway","actual_R3_berth",[]));source=berth.Scene;current=berth.Landing;
            }
            else throw new InvalidOperationException("未支持的实际奥伦出发位置不能被猜测。");
        }
        if(target.StartsWith("HUB",StringComparison.Ordinal))
        {
            var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer");var berth=BranchOperations.Read<VesselBerth>(s,"berth:R3");Require(ship is{DockRegion:"R3"}&&berth is not null&&berth.Instance==ship.Instance&&original.Membership==Membership.Joined,"同船须仍实际泊R3，才能归还原船舱名册位置。");shipId=ship!.Instance.ToString();
            legs.AddRange(WalkRoute(geo,source,current,berth!.Scene,berth.Boarding));var gangway=geo["HUB-S01"].Spots.Single(p=>p.Id=="HUB-S01.gangway").Stand;legs.Add(new(berth.Scene,berth.Boarding,"HUB-S01",gangway,"actual_R3_berth","same_vessel_gangway",[]));
            Point2 final=target=="HUB-S01"?gangway:geo[target].Spots.Single(p=>p.Id==(target=="HUB-S02"?"HUB-S02.archive":"HUB-S03.workbench")).Stand;legs.AddRange(WalkRoute(geo,"HUB-S01",gangway,target,final));current=final;
        }
        else{Point2 final=target=="R3-S03"?geo[target].Spots.Single(p=>p.Id=="R3-S03.oren").Stand:stand;legs.AddRange(WalkRoute(geo,source,current,target,final));current=final;}
        return new(instance,"CP-OREN",target,current,legs.ToImmutable(),shipId,StrictJson.Serialize(roster),legs.SelectMany(l=>new[]{l.FromScene,l.ToScene}).Distinct().ToImmutableDictionary(id=>id,id=>GeometryHash(geo[id])));
    }
    public async ValueTask<CommandResult> BeginActualOrenWorkAsync(string answer)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var p=OrenPost(s);await At(s,p.Mode=="rotation_local_maintenance"?"R3-S08.auxiliary_receipt":"R5-S07.oren_duty");var source=OrenSource(s);var q=Current(s,"CQ-OREN-03");
        Require(answer=="isolate_before_check_same_installed_kit"&&BranchOperations.Entity(s,"personal-detail:oren03-work") is null,"只接受本次同一已测套件、隔离再检查的有限技术回答；不能重开另一维修。");
        var w=new ActualOrenWork(q.Instance,p.Mode,source.Repair.Kit,source.Repair.Configuration,source.Review.ReceiptId,"inspect_same_kit",answer,p.Window,"MC2_bypass_signature_preserved");var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"personal-detail:oren03-work","oren_actual_work",w));
        if(p.Mode=="rotation_local_maintenance"){var pos=await OrenLocalWalk(s,q.Instance,false);changes.Add(BranchOperations.Change(s,"personal-detail:oren03-position","oren_actual_position",pos));var old=BranchOperations.Read<RosterEntry>(s,"roster:CP-OREN")!;changes.Add(BranchOperations.Change(s,"roster:CP-OREN","roster",old with{Location=pos.Scene}));}
        var check=new OrenDeviceCheck(q.Instance,w.Kit,w.DeviceConfiguration,false,false,false,"not_checked");changes.Add(BranchOperations.Change(s,"personal-detail:oren03-device","oren_device_check",check));return await Commit(s,"personal-detail:oren03:work-started","oren_current_auxiliary_work_started",changes.ToImmutable(),w,"CP-OREN");
    }
    public async ValueTask<CommandResult> CheckOrenDeviceAsync(string operation)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var p=OrenPost(s);await At(s,p.Mode=="rotation_local_maintenance"?"R3-S08.auxiliary_receipt":"R5-S07.oren_duty");var source=OrenSource(s);var w=BranchOperations.Read<ActualOrenWork>(s,"personal-detail:oren03-work")??throw new InvalidOperationException("当前有限维修/解答工作未开展。");var d=BranchOperations.Read<OrenDeviceCheck>(s,"personal-detail:oren03-device")!;
        Require(w.NodeInstance==Quest(s,"CQ-OREN-03").Instance&&w.Kit==source.Repair.Kit&&w.DeviceConfiguration==source.Repair.Configuration,"实际已测设备配置改变，旧说明失效。");
        string next=operation switch{"inspect_same_kit" when w.Stage==operation=>"isolate_input","isolate_input" when w.Stage==operation=>"test_passive_alarm","test_passive_alarm" when w.Stage==operation=>"restore_input","restore_input" when w.Stage==operation=>"ready_for_named_receiving",_=>throw new InvalidOperationException("实际检查同一套件、隔离下一检查输入、被动回路测试和恢复须依次操作。")};
        if(operation=="isolate_input")d=d with{InputIsolated=true};
        if(operation=="test_passive_alarm"){Require(d.InputIsolated&&source.Repair.TestPassed,"未隔离或没有原独立配置测试；不得发出危险信号。");d=d with{PassiveTestPassed=true,TestIndicator="same_tested_auxiliary_passive_continuity"};}
        if(operation=="restore_input"){Require(d.InputIsolated&&d.PassiveTestPassed,"实际被动检查尚未通过。");d=d with{InputIsolated=false,Restored=true};}
        var nw=w with{Stage=next};return await Commit(s,"personal-detail:oren03:device:"+operation,"oren_current_device_operation",[BranchOperations.Change(s,"personal-detail:oren03-work","oren_actual_work",nw),BranchOperations.Change(s,"personal-detail:oren03-device","oren_device_check",d)],new{operation,check=d,remote=p.Mode=="supervised_remote_answers",physical_installation_claimed=false},"CP-OREN");
    }
    public async ValueTask<CommandResult> ReceiveOrenInstructionsAsync(string receiver,string supervisor,string incoming)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var p=OrenPost(s);await At(s,p.Mode=="rotation_local_maintenance"?"R3-S08.auxiliary_receipt":"R5-S07.oren_duty");var source=OrenSource(s);var w=BranchOperations.Read<ActualOrenWork>(s,"personal-detail:oren03-work");var d=BranchOperations.Read<OrenDeviceCheck>(s,"personal-detail:oren03-device");
        Require(w is{Stage:"ready_for_named_receiving"}&&d is{Restored:true,InputIsolated:false,PassiveTestPassed:true}&&w.DeviceConfiguration==source.Repair.Configuration&&receiver==p.Receiver&&supervisor==p.Supervisor&&incoming==IncomingOrenWorker&&incoming!="CP-OREN","当前有限工作未恢复安全配置或接收方、监督、独立接替人员不符。");
        var workReceipt=s.Receipts.Single(r=>r.SemanticKey=="personal-detail:oren03:device:restore_input").ReceiptId;
        string invitation=PersonalProductionRules.CurrentInvitationReceipt(s,"CQ-OREN-03")??throw new InvalidOperationException("当前实际私人邀请回执缺失。");var prior=BranchOperations.Read<OrenNamedReceiving>(s,"personal-detail:oren03-receiving");var hashes=Package.Responses.Where(x=>x.Actor==receiver||x.Actor==supervisor).ToImmutableDictionary(x=>x.Id,x=>x.Fingerprint);
        var r=new OrenNamedReceiving(w!.NodeInstance,receiver,supervisor,incoming,w.Kit,w.DeviceConfiguration,workReceipt,p.Window,true,Package.Version,hashes,invitation,(prior?.Generation??0)+1);
        return await Commit(s,"personal-detail:oren03:named-receiving:"+r.Generation,"oren_named_incoming_instructions_received",[BranchOperations.Change(s,"personal-detail:oren03-receiving","oren_named_receiving",r)],r,"CP-OREN");
    }
    public async ValueTask<CommandResult> PrepareActualOrenHandoverAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);var p=OrenPost(s,false);await At(s,p.Mode=="rotation_local_maintenance"?"R3-S08.auxiliary_receipt":"R5-S07.oren_duty");Require(PersonalProductionRules.Ready(s,"CQ-OREN-03",false),"真实同配置工作与独立接收凭证未成立。");var w=BranchOperations.Read<ActualOrenWork>(s,"personal-detail:oren03-work")!;var r=BranchOperations.Read<OrenNamedReceiving>(s,"personal-detail:oren03-receiving")!;var changes=ImmutableArray.CreateBuilder<EntityChange>();
        if(p.Mode=="rotation_local_maintenance"){var pos=await OrenLocalWalk(s,w.NodeInstance,true);changes.Add(BranchOperations.Change(s,"personal-detail:oren03-position","oren_actual_position",pos));var roster=BranchOperations.Read<RosterEntry>(s,"roster:CP-OREN")!;changes.Add(BranchOperations.Change(s,"roster:CP-OREN","roster",roster with{Location=pos.Scene}));}
        var h=new OrenActualHandover(w.NodeInstance,"CP-OREN",r.IncomingWorker,r.Receiver,r.Supervisor,w.Kit,w.DeviceConfiguration,p.Window,w.OriginalResponsibility,false);changes.Add(BranchOperations.Change(s,"personal-detail:oren03-handover","oren_actual_handover",h));
        return await Commit(s,"personal-detail:oren03:actual-handover","oren_actual_independent_replacement_received",changes.ToImmutable(),h,"CP-OREN");
    }
}
