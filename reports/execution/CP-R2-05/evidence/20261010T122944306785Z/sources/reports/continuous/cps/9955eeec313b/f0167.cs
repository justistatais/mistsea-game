using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.ChapterTwo;
using Mist.Application.Inventory;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.R2Life;
using Mist.Core.ChapterOne;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R2Life;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.R2Life;

// Only ActualEntry starts from zero XP. Other cases explicitly fixture prior Q104/route evidence,
// then execute real SQLite sailing, landing, privacy, planting and draft transactions.
// No fixture or typed-meaning selection is a human play/English assessment claim.
public static class R2LifeCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static R2LifeContract Contract()=>R2LifeContract.Parse(File.ReadAllText(Path.Combine(Root,"content/source/r2_life/learner_contract.json")));
    private static ProfileStore Store()=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"r2-life-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static ActorState Actor(ProfileStore s)=>Read<ActorState>(s,"actor");
    public static R2LifeService ActualEntry(ProfileStore s)
    {
        var(_,interior)=Voyage.VoyageCases.NormalChart(s);
        var voyage=new Mist.Application.Voyage.VoyageService(s,Catalog,Voyage.VoyageCases.Package(),ChapterTwo.ChapterTwoCases.Package(),interior.Geometry);
        var verified=Done(voyage.VerifyAndRegisterAsync(Mist.Core.Voyage.VoyageRules.R2));Success(verified.RouteProof);Success(verified.BerthProof);Success(verified.Registration.Route);Success(verified.Registration.Berth);Success(Done(voyage.SailAsync("R2")));Success(Done(voyage.RecordArrivalAsync()));
        var main=new ChapterTwoService(s,Catalog,ChapterTwo.ChapterTwoCases.Package());Done(main.InitializeRegionAsync());Landing(s,main);
        var life=new R2LifeService(s,s,main,Contract());Success(Done(life.InitializeAsync()));return life;
    }
    private static void Landing(ProfileStore s,ChapterTwoService main)
    {
        var a=Actor(s);var m=main.Package.Materials.Single(x=>x.Id=="R2-Q201-R-LANDING");var nav=new NavigationGrid(main.CurrentGeometry(Done(s.SnapshotAsync()))[a.Scene]);Done(main.TraverseAsync(nav.FindPath(a.Position,main.Geometry[a.Scene].Spots.Single(x=>x.Id==m.Spot).Stand,true)));Success(Done(main.DiscoverAsync(m.Id)));
        var station=main.Package.Stations.Single(x=>x.Action=="register_landing");a=Actor(s);Done(main.TraverseAsync(nav.FindPath(a.Position,main.Geometry[a.Scene].Spots.Single(x=>x.Id==station.Spot).Stand,true)));Success(Done(main.ExecuteStationAsync("register_landing")));
    }
    private static Run FixtureRun(ProfileStore s)
    {
        Done(new PartyService(s,Catalog).InitializeStarterCollectionAsync());var ship=new SharedVessel(Guid.NewGuid(),"R1","N-DECKWATCH",["HUB-S01","HUB-S02","HUB-S03"]);
        Fixture(s,"Q-104:resolution","tidegate_vessel_handover",new TidegateHandover(ship.Instance.ToString(),"GLIMMER","N-DECKWATCH",ship.Spaces.ToArray(),"R2","E-01"),
            [Entity(s,"quest:Q-104","quest",new QuestState("Q-104",Guid.NewGuid(),QuestLifecycle.Closed,QuestOutcome.Completed)),Entity(s,"ship:glimmer","shared_vessel",ship),Entity(s,"commission:cross-region","permission",new{allowed=true,next_region="R2",basis="Q-104:resolution",purpose="receiving_record_cross_check",mara_current_status="unknown"}),Entity(s,"actor","actor",new ActorState("R1-S01",new(48,90),0))],balances:[new("experience",900)]);
        var main=new ChapterTwoService(s,Catalog,ChapterTwo.ChapterTwoCases.Package());var geo=main.Geometry.Add("R1-S01",new SceneGeometry("R1-S01",new(5,20,95,95),[],[],[],[]));var travel=new TravelService(s,Catalog,geo);
        foreach(string region in new[]{"R1","R2"})
        {
            string receipt=Fixture(s,"fixture:r2-life-berth:"+region,"vessel_berth_verified",new VesselBerth(ship.Instance,region,region+"-S01",new(48,90),new(48,90),[],"explicit prior Q104 berth fixture")).Receipt!.Id.Value.ToString();Success(Done(travel.RegisterBerthAsync(receipt)));
        }
        string route=Fixture(s,"fixture:r2-life-route","sea_route_verified",new SeaRoute("R1","R2",[],"explicit prior Q104 route fixture")).Receipt!.Id.Value.ToString();Success(Done(travel.RegisterSeaRouteAsync(route)));Success(Done(travel.SailAsync("R2")));Done(main.InitializeRegionAsync());Landing(s,main);
        var life=new R2LifeService(s,s,main,Contract());Success(Done(life.InitializeAsync()));return new(s,main,life);
    }
    public sealed class Run(ProfileStore store,ChapterTwoService main,R2LifeService service)
    {
        public R2LifeService Service {get;}=service;
        public R2LifeView View=>Done(Service.ViewAsync());
        public void Walk(Point2 to){var a=Actor(store);var path=new NavigationGrid(main.CurrentGeometry(Done(store.SnapshotAsync()))[a.Scene]).FindPath(a.Position,to,true);Require(path.Length>0,"R2 life safe path missing "+a.Scene);Done(Service.TraverseAsync(path));}
        public void Spot(string id){var a=Actor(store);Walk(Service.Geometry[a.Scene].Spots.Single(x=>x.Id==id).Stand);}
        public void Exit(string target){var a=Actor(store);var port=Service.Geometry[a.Scene].Ports.Single(p=>p.TargetScene==target);Walk(port.Stand);Success(Done(Service.EnterPortAsync(port.Id)));}
        public void Material(string id){var m=Service.Materials.Single(m=>m.Id==id);Spot(m.Spot);Success(Done(Service.DiscoverAsync(id)));Require(Done(Service.OpenMaterialAsync(id)).Fingerprint==m.Fingerprint);}
        public void PublicLetter(){Exit("R2-S02");Exit("R2-S03");Spot("R2-S03.address");}
        public void Scope()=>Success(Done(Service.RequestLetterScopeAsync(["current_draft","correspondence_intent"],"patrol_technician","help_this_reply",Service.Materials.Single(m=>m.Id==R2LifeRules.LetterMaterial).Version)));
        public void Letter(){PublicLetter();Scope();Exit("R2-O01");Spot("R2-O01.reply");Success(Done(Service.AcceptAsync(R2LifeRules.Reply)));Material(R2LifeRules.LetterMaterial);Spot("R2-O01.reply");}
        public void Garden(){Exit("R2-S02");Exit("R2-S05");Exit("R2-O02");Spot("R2-O02.allocation");Success(Done(Service.AcceptAsync(R2LifeRules.Seeds)));Material(R2LifeRules.SeedMaterial);Spot("R2-O02.allocation");}
        public ImmutableArray<BedAssignment> Layout(int leaves)=>Service.Contract.BedIds.Select((id,i)=>new BedAssignment(id,i<leaves?TrialCrop.QuickLeaves:TrialCrop.StorageRoots)).ToImmutableArray();
        public void PrepareGarden(int leaves=4){foreach(string bed in Service.Contract.BedIds)Success(Done(Service.InspectBedAsync(bed)));Success(Done(Service.SetLayoutAsync(Layout(leaves))));Success(Done(Service.RequestGrowerApprovalAsync()));}
        public void Plant(int count=6){foreach(string bed in Service.Contract.BedIds.Take(count))Success(Done(Service.PlantBedAsync(bed)));}
        public void Save(string quest,string body="I propose a short, source-bound local note. This test does not evaluate English.")
        {var task=Service.Writing.Single(w=>w.Quest==quest);Spot(task.Spot);var d=Done(store.GetDraftAsync(task.Id));Done(Service.SaveDraftAsync(quest,d?.Id,d?.Revision??0,body));}
        public void Meaning(string quest)=>Success(Done(Service.ConfirmMeaningAsync(quest,true,quest==R2LifeRules.Reply?ValidMeaning():null)));
        public R2LifePreview Preview(string quest){var p=Done(Service.PreviewSubmissionAsync(quest));Require(p.Ready,string.Join(";",p.Issues));return p;}
        public void FinishLetter(ReplyDecision decision=ReplyDecision.SendShortAcknowledgement){Save(R2LifeRules.Reply);Meaning(R2LifeRules.Reply);Success(Done(Service.RequestAuthorReviewAsync(decision)));Success(Done(Service.ConfirmPreviewAsync(Preview(R2LifeRules.Reply).Token)));}
        public void FinishGarden(){Save(R2LifeRules.Seeds);Meaning(R2LifeRules.Seeds);Success(Done(Service.ConfirmPreviewAsync(Preview(R2LifeRules.Seeds).Token)));}
    }
    private static PrivateReplyMeaning ValidMeaning()=>new("correspondence_only","not_promised","not_public","custodian_existing_room_code");
    public static void Register()
    {
        Add("CP-R2-03","r2_optional_contract_preserves_original_complete_sources_and_ports_with_pending_human_gates",()=>
        {
            var c=Contract();var p=ChapterTwo.ChapterTwoCases.Package();Require(c.BedIds.Length==6&&c.Paragraphs.Length==4&&c.HumanReview=="human_pending");
            Require(p.Materials.Single(m=>m.Id==R2LifeRules.LetterMaterial).Paragraphs.Length==4&&p.Materials.Single(m=>m.Id==R2LifeRules.SeedMaterial).Paragraphs.Length==4);
            Throws<InvalidDataException>(()=>R2LifeContract.Validate(c with{HumanReview="passed"}));Throws<InvalidDataException>(()=>R2LifeContract.Validate(c with{BedIds=c.BedIds.SetItem(5,c.BedIds[0])}));
        });
        Add("CP-R2-03","actual_zero_XP_R2_optional_entry_uses_same_GLIMMER_and_landing_without_mainline_or_local_awards",()=>
        {
            string output=Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.Combine(Path.GetTempPath(),"MistSea-R2-Life-Entry-"+Guid.NewGuid());using var s=new ProfileStore(Path.Combine(output,"normal-r2-life-native-entry"),Catalog.ReleaseId);var service=ActualEntry(s);var v=Done(service.ViewAsync());
            Require(v.Snapshot.Balances["experience"]==900&&v.Actor.Scene=="R2-S01"&&v.Quests.All(q=>q.Lifecycle==QuestLifecycle.Unaccepted)&&v.Snapshot.Entities.Count(e=>e.Kind=="shared_vessel")==1);
            Require(v.Snapshot.Receipts.All(r=>!r.ActionType.Contains("fixture",StringComparison.Ordinal)&&r.ActionType!="internal_slice_initialization")&&!v.Snapshot.Receipts.Any(r=>r.SemanticKey=="Q-201:resolution"));
            Done(s.BackupAsync());Done(s.CreateCheckpointAsync("manual"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"entry_evidence.json"),StrictJson.Serialize(new{scope="actual zero-XP through Q104, same-vessel normal R1R2 voyage and landing; local requests unaccepted",experience=900,receipts=v.Snapshot.Receipts,human_play="pending",language_calibration="pending"}));
        });
        Add("CP-R2-03","bare_R2_actor_cannot_fabricate_optional_entry_or_local_quest_instances",()=>
        {using var s=Store();Fixture(s,"fixture:invalid-bare-r2","explicit_engineering_fixture",new{scope="invalid actor only"},[Entity(s,"actor","actor",new ActorState("R2-S01",new(48,90),0))]);var main=new ChapterTwoService(s,Catalog,ChapterTwo.ChapterTwoCases.Package());var service=new R2LifeService(s,s,main,Contract());long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(service.InitializeAsync()));Require(s.Session.Sequence==seq&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id==R2LifeService.StateId));});
        Add("CP-R2-03","private_reply_requires_exact_current_person_scope_and_does_not_leak_unread_sources",()=>
        {using var s=Store();var r=FixtureRun(s);r.PublicLetter();var p=r.Service.Geometry[r.View.Actor.Scene].Ports.Single(p=>p.TargetScene=="R2-O01");r.Walk(p.Stand);long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(r.Service.EnterPortAsync(p.Id)));Require(s.Session.Sequence==seq&&r.View.Materials.IsEmpty);Throws<InvalidOperationException>(()=>Done(r.Service.OpenMaterialAsync(R2LifeRules.LetterMaterial)));r.Spot("R2-S03.address");Throws<InvalidOperationException>(()=>Done(r.Service.RequestLetterScopeAsync(["current_draft","private_address"],"patrol_technician","help_this_reply",r.Service.Materials[0].Version)));Throws<InvalidOperationException>(()=>Done(r.Service.RequestLetterScopeAsync(["current_draft","correspondence_intent"],"public_board","publish",r.Service.Materials[0].Version)));Throws<InvalidOperationException>(()=>Done(r.Service.RequestLetterScopeAsync(["current_draft","correspondence_intent"],"patrol_technician","help_this_reply","wrong-version")));Require(!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id==R2LifeService.ScopeId));});
        Add("CP-R2-03","withdrawal_preserves_saved_private_draft_blocks_new_work_and_keeps_safe_exit_open",()=>
        {using var s=Store();var r=FixtureRun(s);r.Letter();r.Save(R2LifeRules.Reply,"Keep this private revision for later.");var task=r.Service.Writing.Single(w=>w.Quest==R2LifeRules.Reply);var draft=Done(s.GetDraftAsync(task.Id))!;Success(Done(r.Service.WithdrawLetterScopeAsync()));Require(r.View.Materials.IsEmpty&&Done(s.GetDraftAsync(task.Id))!.Hash==draft.Hash);Throws<InvalidOperationException>(()=>Done(r.Service.OpenMaterialAsync(R2LifeRules.LetterMaterial)));Throws<InvalidOperationException>(()=>Done(r.Service.SaveDraftAsync(R2LifeRules.Reply,draft.Id,draft.Revision,"unallowed change")));Throws<InvalidOperationException>(()=>r.Meaning(R2LifeRules.Reply));Require(!Done(r.Service.PreviewSubmissionAsync(R2LifeRules.Reply)).Ready);r.Exit("R2-S03");Require(r.View.Actor.Scene=="R2-S03");});
        Add("CP-R2-03","meaning_confirmation_cannot_replace_writer_current_draft_review_or_promise_a_visit",()=>
        {using var s=Store();var r=FixtureRun(s);r.Letter();r.Save(R2LifeRules.Reply);Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmMeaningAsync(R2LifeRules.Reply,true,ValidMeaning()with{Visit="promised"})));r.Meaning(R2LifeRules.Reply);Require(!Done(r.Service.PreviewSubmissionAsync(R2LifeRules.Reply)).Ready&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="r2-life:post-custody"));});
        Add("CP-R2-03","old_writer_review_cannot_send_a_new_saved_draft_and_new_review_binds_exact_hash",()=>
        {using var s=Store();var r=FixtureRun(s);r.Letter();r.Save(R2LifeRules.Reply);r.Meaning(R2LifeRules.Reply);Success(Done(r.Service.RequestAuthorReviewAsync(ReplyDecision.SendShortAcknowledgement)));var old=r.View.WriterReview!;var p=r.Preview(R2LifeRules.Reply);r.Save(R2LifeRules.Reply,"A changed revision asks for correspondence only.");Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);r.Meaning(R2LifeRules.Reply);Require(!Done(r.Service.PreviewSubmissionAsync(R2LifeRules.Reply)).Ready);Success(Done(r.Service.RequestAuthorReviewAsync(ReplyDecision.SendShortAcknowledgement)));Require(r.View.WriterReview!.DraftHash!=old.DraftHash);Success(Done(r.Service.ConfirmPreviewAsync(r.Preview(R2LifeRules.Reply).Token)));});
        Add("CP-R2-03","withdrawal_before_ready_confirm_rejects_atomic_private_delivery_without_closing_quest",()=>
        {using var s=Store();var r=FixtureRun(s);r.Letter();r.Save(R2LifeRules.Reply);r.Meaning(R2LifeRules.Reply);Success(Done(r.Service.RequestAuthorReviewAsync(ReplyDecision.SendShortAcknowledgement)));var p=r.Preview(R2LifeRules.Reply);Success(Done(r.Service.WithdrawLetterScopeAsync()));long seq=s.Session.Sequence;Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&s.Session.Sequence==seq&&r.View.Quests.Single(q=>q.Id==R2LifeRules.Reply).Lifecycle==QuestLifecycle.Active&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="r2-life:post-custody"));});
        Add("CP-R2-03","busy_local_writer_and_post_custodian_do_not_clone_roles_or_half_commit_a_letter",()=>
        {using var s=Store();var r=FixtureRun(s);r.PublicLetter();Require(Done(s.TryLeasesAsync(s.Session.Load,"explicit-busy-writer",[r.Service.Contract.Roles.Writer])));long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>r.Scope());Require(s.Session.Sequence==seq);Done(s.ReleaseLeasesAsync(s.Session.Load,"explicit-busy-writer"));r.Scope();r.Exit("R2-O01");r.Spot("R2-O01.reply");Success(Done(r.Service.AcceptAsync(R2LifeRules.Reply)));r.Material(R2LifeRules.LetterMaterial);r.Save(R2LifeRules.Reply);r.Meaning(R2LifeRules.Reply);Success(Done(r.Service.RequestAuthorReviewAsync(ReplyDecision.SendShortAcknowledgement)));var p=r.Preview(R2LifeRules.Reply);Require(Done(s.TryLeasesAsync(s.Session.Load,"explicit-busy-post",[r.Service.Contract.Roles.Custodian])));seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmPreviewAsync(p.Token)));Require(s.Session.Sequence==seq&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="r2-life:post-custody"));Done(s.ReleaseLeasesAsync(s.Session.Load,"explicit-busy-post"));Success(Done(r.Service.ConfirmPreviewAsync(r.Preview(R2LifeRules.Reply).Token)));});
        Add("CP-R2-03","pause_and_continue_preserve_private_instance_and_require_new_foreground_current_review",()=>
        {using var s=Store();var r=FixtureRun(s);r.Letter();Guid instance=r.View.Quests.Single(q=>q.Id==R2LifeRules.Reply).Instance;r.Save(R2LifeRules.Reply);Success(Done(r.Service.SetPausedAsync(R2LifeRules.Reply,true)));Done(s.ContinueAsync());Require(r.View.Quests.Single(q=>q.Id==R2LifeRules.Reply).Instance==instance);Throws<InvalidOperationException>(()=>r.Meaning(R2LifeRules.Reply));Success(Done(r.Service.SetPausedAsync(R2LifeRules.Reply,false)));r.FinishLetter(ReplyDecision.KeepRevision);Require(r.View.Quests.Single(q=>q.Id==R2LifeRules.Reply).Instance==instance);});
        foreach(var decision in Enum.GetValues<ReplyDecision>())
        {
            var chosen=decision;Add("CP-R2-03","private_reply_"+chosen+"_is_once_only_zero_reward_and_separates_custody_from_keep",()=>
            {using var s=Store();var r=FixtureRun(s);var before=r.View.Snapshot;r.Letter();r.FinishLetter(chosen);var final=r.View.Snapshot;Require(final.Balances.SequenceEqual(before.Balances)&&final.Items.SequenceEqual(before.Items)&&final.Receipts.Count(x=>x.SemanticKey==R2LifeRules.Reply+":resolution")==1&&final.Entities.Any(e=>e.Id=="r2-life:post-custody")== (chosen==ReplyDecision.SendShortAcknowledgement));using var body=JsonDocument.Parse(final.Entities.Single(e=>e.Id=="r2-life:reply-result").Json);Require(body.RootElement.GetProperty("sent").GetBoolean()==(chosen==ReplyDecision.SendShortAcknowledgement)&&!body.RootElement.GetProperty("visit_promised").GetBoolean()&&!body.RootElement.GetProperty("address_published").GetBoolean());Require(!Done(r.Service.PreviewSubmissionAsync(R2LifeRules.Reply)).Ready);});
        }
        Add("CP-R2-03","six_bed_layout_rejects_duplicate_unknown_excess_and_unagreed_three_three_arrangements",()=>
        {using var s=Store();var r=FixtureRun(s);r.Garden();foreach(var invalid in new[]{r.Layout(3),r.Layout(4).SetItem(5,new(r.Service.Contract.BedIds[0],TrialCrop.StorageRoots)),r.Layout(4).Add(new("seventh-bed",TrialCrop.StorageRoots)),r.Layout(4).SetItem(5,new("unknown-bed",TrialCrop.StorageRoots))})Throws<InvalidOperationException>(()=>Done(r.Service.SetLayoutAsync(invalid)));Require(r.View.State.Layout.IsEmpty&&r.View.State.Planted.IsEmpty);});
        Add("CP-R2-03","five_inspections_and_saved_note_do_not_replace_six_spaces_grower_approval_or_planting",()=>
        {using var s=Store();var r=FixtureRun(s);r.Garden();foreach(string bed in r.Service.Contract.BedIds.Take(5))Success(Done(r.Service.InspectBedAsync(bed)));Success(Done(r.Service.SetLayoutAsync(r.Layout(4))));r.Save(R2LifeRules.Seeds);Throws<InvalidOperationException>(()=>Done(r.Service.RequestGrowerApprovalAsync()));Throws<InvalidOperationException>(()=>Done(r.Service.PlantBedAsync(r.Service.Contract.BedIds[0])));Throws<InvalidOperationException>(()=>r.Meaning(R2LifeRules.Seeds));Require(!Done(r.Service.PreviewSubmissionAsync(R2LifeRules.Seeds)).Ready&&r.View.State.Planted.IsEmpty);});
        foreach(int leaves in new[]{2,4})
        {
            int count=leaves;Add("CP-R2-03","six_bed_"+count+"_leaves_actual_planting_keeps_conditional_estimate_out_of_stock_and_rewards",()=>
            {using var s=Store();var r=FixtureRun(s);var before=r.View.Snapshot;r.Garden();r.PrepareGarden(count);r.Plant();r.FinishGarden();var final=r.View.Snapshot;Require(R2LifeRules.PlantedAll(r.Service.Contract,r.View.State)&&final.Balances.SequenceEqual(before.Balances)&&final.Items.SequenceEqual(before.Items)&&final.Receipts.Count(x=>x.ActionType=="r2_trial_bed_actually_planted")==6&&final.Receipts.Count(x=>x.SemanticKey==R2LifeRules.Seeds+":resolution")==1);var estimate=R2LifeRules.Estimate(r.View.State.Layout);Require(estimate.EarlyServingBaskets==(count==4?8:4)&&estimate.LaterStorageBaskets==(count==4?4:8)&&estimate.Scope=="conditional_local_trial_estimate_not_stock");Require(final.Entities.Single(e=>e.Id=="quest:Q-201").Json==before.Entities.Single(e=>e.Id=="quest:Q-201").Json);});
        }
        Add("CP-R2-03","changed_layout_invalidates_approval_and_planted_beds_cannot_be_reset_or_counted_twice",()=>
        {using var s=Store();var r=FixtureRun(s);r.Garden();r.PrepareGarden();Success(Done(r.Service.SetLayoutAsync(r.Layout(2))));Require(r.View.State.Approval is null);Throws<InvalidOperationException>(()=>Done(r.Service.PlantBedAsync(r.Service.Contract.BedIds[0])));Success(Done(r.Service.RequestGrowerApprovalAsync()));r.Plant(1);long seq=s.Session.Sequence;Require(Done(r.Service.PlantBedAsync(r.Service.Contract.BedIds[0])).Status==CommitStatus.AlreadyCommitted&&s.Session.Sequence==seq);Throws<InvalidOperationException>(()=>Done(r.Service.SetLayoutAsync(r.Layout(4))));Require(r.View.State.Planted.Length==1);});
        Add("CP-R2-03","revision_request_remains_pending_and_five_actual_beds_cannot_submit_a_six_bed_result",()=>
        {using var s=Store();var r=FixtureRun(s);r.Garden();Success(Done(r.Service.RequestLayoutRevisionAsync("Could the grower agree to three leaf beds and three root beds?")));Require(r.View.State.RevisionRequest.Length>0&&r.View.State.Approval is null);Throws<InvalidOperationException>(()=>Done(r.Service.SetLayoutAsync(r.Layout(3))));r.PrepareGarden();r.Plant(5);r.Save(R2LifeRules.Seeds);Throws<InvalidOperationException>(()=>r.Meaning(R2LifeRules.Seeds));Require(!Done(r.Service.PreviewSubmissionAsync(R2LifeRules.Seeds)).Ready&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="r2-life:trial-result"));});
        Add("CP-R2-03","six_bed_ready_preview_is_read_only_and_rejects_current_draft_edit_or_old_load",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Garden();r.PrepareGarden();r.Plant();r.Save(R2LifeRules.Seeds);r.Meaning(R2LifeRules.Seeds);
            long beforePreview=s.Session.Sequence;var p=r.Preview(R2LifeRules.Seeds);Require(s.Session.Sequence==beforePreview,"Preview itself must not commit world state.");
            var current=Done(s.GetDraftAsync(r.Service.Writing.Single(w=>w.Quest==R2LifeRules.Seeds).Id))!;
            // Stay at the actual allocation stand. Run.Save also traverses to its stand, which is a real world walk.
            Done(r.Service.SaveDraftAsync(R2LifeRules.Seeds,current.Id,current.Revision,"A changed final note for the actual six planted beds."));
            Require(s.Session.Sequence==beforePreview,"Draft-only save must not commit world state.");Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected,"Edited draft must invalidate the ready preview.");
            r.Meaning(R2LifeRules.Seeds);var old=r.Preview(R2LifeRules.Seeds);Done(s.ContinueAsync());Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmPreviewAsync(old.Token)));
            Require(!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="r2-life:trial-result"),"Old load must not issue a trial result.");Success(Done(r.Service.ConfirmPreviewAsync(r.Preview(R2LifeRules.Seeds).Token)));
        });
        Add("CP-R2-03","declining_two_optional_stories_preserves_mainline_recruitment_ending_and_legal_vessel",()=>
        {using var s=Store();var r=FixtureRun(s);var before=r.View.Snapshot;r.PublicLetter();Success(Done(r.Service.DeclineAsync(R2LifeRules.Reply)));r.Exit("R2-S02");r.Exit("R2-S05");r.Exit("R2-O02");r.Spot("R2-O02.allocation");Success(Done(r.Service.DeclineAsync(R2LifeRules.Seeds)));var after=r.View.Snapshot;Require(after.Balances.SequenceEqual(before.Balances)&&after.Items.SequenceEqual(before.Items)&&r.View.Quests.All(q=>q.Outcome==QuestOutcome.DeclinedPermanently)&&after.Entities.Single(e=>e.Id=="quest:Q-201")==before.Entities.Single(e=>e.Id=="quest:Q-201")&&after.Entities.Single(e=>e.Id=="ship:glimmer")==before.Entities.Single(e=>e.Id=="ship:glimmer"));});
        Add("CP-R2-03","completed_trial_survives_continue_without_replanting_or_renewing_instances",()=>
        {using var s=Store();var r=FixtureRun(s);r.Garden();r.PrepareGarden();r.Plant();r.FinishGarden();var before=r.View;Done(s.ContinueAsync());Success(Done(r.Service.InitializeAsync()));var after=r.View;Require(after.State.Planted.SequenceEqual(before.State.Planted)&&after.Quests.SequenceEqual(before.Quests)&&after.Snapshot.Sequence==before.Snapshot.Sequence&&after.Snapshot.Receipts.Count(x=>x.ActionType=="r2_trial_bed_actually_planted")==6);});
    }
}
