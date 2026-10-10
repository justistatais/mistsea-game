using System.Collections.Immutable;
using Mist.Application.ChapterTwo;
using Mist.Application.ChapterThree;
using Mist.Application.ChapterFour;
using Mist.Application.ChapterFive;
using Mist.Application.ChapterSix;
using Mist.Application.Contracts;
using Mist.Application.Roster;
using Mist.Application.ScopedPersonalWorks;
using Mist.Application.Storage;
using Mist.Application.VesselInterior;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterFour;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.ScopedPersonalWorks;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ScopedPersonalWorks;

public static class ScopedPersonalWorkCases
{
    public static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    public static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static ScopedPersonalPackage Package()=>ScopedPersonalPackage.ParseJson(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/scoped_personal_work/learner_package.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"scoped-personal-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    public static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    public static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    public sealed class Run
    {
        private readonly ImmutableDictionary<string,SceneGeometry> _baseGeometry;
        public ProfileStore Store {get;}public ChapterTwoService Two {get;}public ChapterThreeService Three {get;}public ChapterFourService Four {get;}public ChapterFiveService Five {get;}public VesselInteriorService Interior {get;}public ScopedPersonalWorksService Detail {get;}
        public Run(ProfileStore store,ChapterFiveService five,VesselInteriorService interior)
        {Store=store;Five=five;_baseGeometry=interior.Geometry;Two=new(store,Catalog,ChapterTwo.ChapterTwoCases.Package());Three=new(store,Catalog,ChapterThree.ChapterThreeCases.Package());Four=new(store,Catalog,ChapterFour.ChapterFourCases.Package());Interior=new(store,Catalog,interior.Package,Geometry().SetItems(ChapterSix.ChapterSixCases.Package().Scenes.ToDictionary(s=>s.Geometry.Id,s=>s.Geometry)));Detail=new(store,Catalog,Four,Five,Package(),()=>ValueTask.FromResult(Geometry()));}
        public ImmutableDictionary<string,SceneGeometry> Geometry()=>_baseGeometry.SetItems(Done(Two.CurrentGeometryAsync())).SetItems(Done(Three.CurrentGeometryAsync())).SetItems(Done(Four.CurrentGeometryAsync())).SetItems(Done(Five.CurrentGeometryAsync()));
        private ChapterSix.ChapterSixCases.Run Navigation()=>new(Store,new ChapterSixService(Store,Catalog,ChapterSix.ChapterSixCases.Package(),Interior.Geometry.SetItems(Geometry()),Store),new VesselInteriorService(Store,Catalog,Interior.Package,Interior.Geometry.SetItems(Geometry())));
        public void Sail(string region)=>Navigation().Sail(region);
        public void At(string spot)=>Navigation().PriorAt(spot);
        public void Mira01()
        {
            Sail("R2");At("R2-S04.mira");Success(Done(Two.DecideW04InvitationAsync("accept")));At("R2-S03.w04");Success(Done(Two.RequestW04ScopeAsync(["observation_time","witness_duty","version_source"],"priory_review_desk","verify_local_note","W04-v1")));Success(Done(Two.DiscoverAsync("R2-CQMIRA01-R-W04")));
            var task=Two.Package.WritingTasks.Single(t=>t.Quest=="CQ-MIRA-01");At(task.Spot);var old=Done(Store.GetDraftAsync(task.Id));Done(Two.SaveDraftAsync(task.Id,old?.Revision??0,old?.Id,"I will relay the permitted observation and duty reference to the review desk. The source version stays with it, and the private address is excluded."));Success(Done(Two.ConfirmMeaningAsync("CQ-MIRA-01",new Dictionary<string,string>{{"path","custodian_relay"},{"recipient","priory_review_desk"},{"purpose","verify_local_note"},{"version","W04-v1"},{"address","excluded"}}.ToImmutableDictionary(),true)));var p=Done(Two.PreviewSubmissionAsync("CQ-MIRA-01"));Require(p.Ready,StrictJson.Serialize(p));Success(Done(Two.ConfirmPreviewAsync(p.Token)));
        }
        public void Oren01And02(){Sail("R3");new ChapterThree.ChapterThreeCases.Run(Store,Three).Auxiliary();}
        public void Mira02(bool complete=true)
        {
            Sail("R4");At("R4-S03.review_request");Success(Done(Four.AcceptMiraReviewAsync(true)));Success(Done(Four.DiscoverAsync("R4-CQMIRA02-R-REQUEST")));At("R4-S04.w04_copy");Success(Done(Four.GenerateReviewCopyAsync("R4-review-desk","verify_role_counts","W04-review-v1",ScopedPersonalWorksService.RoleFields)));Success(Done(Detail.MakeReviewPaperAsync()));
            var copy=Read<DerivedReviewCopy>(Store,"R4:mira_review_copy");var seal=Done(Detail.PreviewReviewPaperAsync("seal",new(2,1,copy.Version,copy.SourceRoot,copy.Recipient,copy.Purpose,false)));Require(seal.Ready);Success(Done(Detail.ConfirmPreviewAsync(seal.Token)));var deliver=Done(Detail.PreviewReviewPaperAsync("deliver"));Require(deliver.Ready);Success(Done(Detail.ConfirmPreviewAsync(deliver.Token)));
            if(!complete)return;var r=new ChapterFour.ChapterFourCases.Run(Store,Four);r.Meaning("CQ-MIRA-02",("fields","role_counts_and_source_only"),("recipient","R4-review-desk"),("purpose","verify_role_counts"),("identity_publication","not_permitted"),("copy_independence","same_source"));r.Submit("CQ-MIRA-02");
        }
        public void StartMira03(string mode)
        {At("R5-S07.personal_review");Success(Done(Five.DecidePersonalInvitationAsync("CQ-MIRA-03","accept")));Success(Done(Five.DiscoverAsync("R5-CQMIRA03-R-EXERCISE")));Success(Done(Five.BeginPersonalAccessExerciseAsync(mode)));foreach(string actor in new[]{"CP-MIRA","W-04"})Success(Done(Detail.RequestMiraRenewalAnswerAsync(actor,ScopedPersonalWorksService.RoleFields,ScopedPersonalWorksService.ReviewRecipients(mode))));Success(Done(Detail.MakeMiraDrillCopiesAsync()));}
        public void Mira03Steps(string mode)
        {
            string recipient=ScopedPersonalWorksService.ReviewRecipients(mode)[0];Success(Done(Detail.AttemptMiraReadAsync(recipient,ScopedPersonalWorksService.RoleFields)));Success(Done(Five.PerformPersonalAccessStepAsync("check_role_fields")));
            Success(Done(Detail.WithdrawFutureMiraContactAsync()));Success(Done(Detail.AttemptMiraReadAsync(recipient,ScopedPersonalWorksService.RoleFields,"future_contact")));Success(Done(Detail.VerifyMiraExitAsync()));Success(Done(Five.PerformPersonalAccessStepAsync("test_optional_exit")));
            Success(Done(Detail.AppendMiraCorrectionAsync("role_entries_are_identity_count","role_entries_only_identity_count_unknown")));Success(Done(Five.PerformPersonalAccessStepAsync("append_correction")));
            Success(Done(Detail.AttemptMiraReadAsync("public_notice_board",["identity_key"])));Success(Done(Five.PerformPersonalAccessStepAsync("test_restricted_review")));
        }
        public void RefreshMiraConsent(string mode)
        {foreach(string actor in new[]{"CP-MIRA","W-04"})Success(Done(Detail.RequestMiraRenewalAnswerAsync(actor,ScopedPersonalWorksService.RoleFields,ScopedPersonalWorksService.ReviewRecipients(mode))));string recipient=ScopedPersonalWorksService.ReviewRecipients(mode)[0];Success(Done(Detail.AttemptMiraReadAsync(recipient,ScopedPersonalWorksService.RoleFields)));Success(Done(Detail.AttemptMiraReadAsync(recipient,ScopedPersonalWorksService.RoleFields,"future_contact")));Success(Done(Detail.AttemptMiraReadAsync("public_notice_board",["identity_key"])));}
        public void Mira03(string mode="independent_trustee_review")
        {StartMira03(mode);Mira03Steps(mode);foreach(string actor in new[]{"CP-MIRA","W-04"})Success(Done(Five.RecordPersonalArrangementDecisionAsync(actor,"accept")));new ChapterFive.ChapterFiveCases.Run(Store,Five).Meaning("CQ-MIRA-03",("mode",mode),("access","role_counts_source_only"),("correction","append_keep_original"),("exit","future_contact_only_not_destroy_evidence"),("main_custody","unchanged"));}
        public void Oren03(string mode="supervised_remote_answers")
        {
            At("R5-S07.oren_duty");Success(Done(Five.DecidePersonalInvitationAsync("CQ-OREN-03","accept")));Success(Done(Five.DiscoverAsync("R5-CQOREN03-R-DUTY")));Success(Done(Five.ArrangeOrenDutyAsync(mode,"ROLE-R3-LOCAL-MAINTENANCE","ROLE-R3-REMOTE-REVIEW","next_safe_reassignment")));
            if(mode=="rotation_local_maintenance"){Sail("R3");At("R3-S08.auxiliary_receipt");}Success(Done(Detail.BeginActualOrenWorkAsync("isolate_before_check_same_installed_kit")));foreach(string step in new[]{"inspect_same_kit","isolate_input","test_passive_alarm","restore_input"})Success(Done(Detail.CheckOrenDeviceAsync(step)));Success(Done(Detail.ReceiveOrenInstructionsAsync("ROLE-R3-LOCAL-MAINTENANCE","ROLE-R3-REMOTE-REVIEW",ScopedPersonalWorksService.IncomingOrenWorker)));
            if(mode=="rotation_local_maintenance"){Success(Done(Detail.PrepareActualOrenHandoverAsync()));Sail("R5");}new ChapterFive.ChapterFiveCases.Run(Store,Five).Meaning("CQ-OREN-03",("mode",mode),("responsibility","MC2_bypass_signature_preserved"),("repair","separate_auxiliary_independent_review"),("window","next_safe_reassignment"),("occupation","no_battle_or_other_duty_overlap"));
        }
        public void HandoffOren()
        {At("R5-S07.oren_duty");if(!Done(Store.SnapshotAsync()).Entities.Any(e=>e.Id=="personal-detail:oren03-handover"))Success(Done(Detail.PrepareActualOrenHandoverAsync()));Success(Done(Five.HandoffOrenDutyAsync()));}
        public void Submit(string node)=>new ChapterFive.ChapterFiveCases.Run(Store,Five).Submit(node);
    }
    public static Run MainComplete(ProfileStore store){var p=ChapterFive.ChapterFiveCases.NormalComplete(store);return new(store,p.Service,p.Interior);}
    public static Run NormalPriorComplete(ProfileStore store)
    {var r=MainComplete(store);r.Mira01();r.Oren01And02();r.Mira02();r.Sail("R5");r.At("R5-S07.personal_review");Require(Done(store.SnapshotAsync()).Balances["experience"]==10620);return r;}
    public static void Register()
    {
        Add("CP-PERSONAL-DETAIL","Finite response source retains bounded full bilingual text without audio or an identity key",()=>{var p=Package();Require(p.Version==PersonalProductionRules.DetailVersion&&p.Responses.Length==6&&p.Responses.All(r=>r.English.Length>100&&r.Chinese.Length>30));Require(p.Responses.All(r=>r.Fingerprint.Length==64));});
        Add("CP-PERSONAL-DETAIL","Fresh zero XP profile cannot make a paper or fabricate a maintenance or current consent receipt",()=>{using var s=Store("unarrived");var four=new ChapterFourService(s,Catalog,ChapterFour.ChapterFourCases.Package());var five=new ChapterFiveService(s,Catalog,ChapterFive.ChapterFiveCases.Package());var svc=new ScopedPersonalWorksService(s,Catalog,four,five,Package(),()=>ValueTask.FromResult(ImmutableDictionary<string,SceneGeometry>.Empty));long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(svc.MakeReviewPaperAsync()));Throws<InvalidOperationException>(()=>Done(svc.BeginActualOrenWorkAsync("isolate_before_check_same_installed_kit")));Require(s.Session.Sequence==seq&&!PersonalProductionRules.Ready(Done(s.SnapshotAsync()),"CQ-MIRA-02"));});
        Add("CP-PERSONAL-DETAIL","Actual zero XP source chain delivers same paper then performs truthful consent exit correction and remote independent replacement once",()=>
        {
            using var s=Store("normal-remote");var r=NormalPriorComplete(s);var before=Done(s.SnapshotAsync());string custody=Read<ChapterFiveState>(s,"chapter5").Custody!.Mode;string responsibility=before.Entities.Single(e=>e.Id=="finding:R3-OREN-ACCEPTANCE").Json;
            Throws<InvalidOperationException>(()=>Done(r.Five.PerformPersonalAccessStepAsync("check_role_fields")));r.Mira03();r.Submit("CQ-MIRA-03");r.Oren03();r.Submit("CQ-OREN-03");r.HandoffOren();var done=Done(s.SnapshotAsync());Require(done.Balances["experience"]==10700&&done.Balances["marks"]-before.Balances["marks"]==60&&done.Balances.GetValueOrDefault("parts")==before.Balances.GetValueOrDefault("parts")&&done.Items.Count(i=>i.Id==ScopedPersonalWorksService.ReviewPaper)==1&&Read<OrenMaintenancePost>(s,"R5:oren_duty").Status=="handed_off"&&Read<ChapterFiveState>(s,"chapter5").Custody!.Mode==custody&&done.Entities.Single(e=>e.Id=="finding:R3-OREN-ACCEPTANCE").Json==responsibility);
            Require(done.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&x.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));foreach(string cq in new[]{"CQ-MIRA-01","CQ-MIRA-02","CQ-MIRA-03","CQ-OREN-01","CQ-OREN-02","CQ-OREN-03"})Require(done.Receipts.Count(x=>x.SemanticKey==cq+":resolution")==1);Done(s.ContinueAsync());Require(Read<RosterEntry>(s,"roster:CP-OREN") is{Availability:ActorAvailability.LocalContact,Location:"R3-S03"});File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-scoped-personal-complete.json"),StrictJson.Serialize(new{scope="actual 0XP through Q504, real same-vessel return voyages and six original personal nodes; no new rewards",experience=10700,receipts=done.Receipts,human_language="pending",native_human="pending"}));
        });
        Add("CP-PERSONAL-DETAIL","Normal source-backed prior nodes produce a native entry with last personal work untouched",()=>
        {using var s=Store("native-entry");var r=NormalPriorComplete(s);var snap=Done(s.SnapshotAsync());Require(snap.Balances["experience"]==10620&&snap.Entities.All(e=>e.Id is not("personal-detail:mira03-drill" or "personal-detail:oren03-work"))&&snap.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));Done(s.CreateCheckpointAsync("manual"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-scoped-personal-native-entry.json"),StrictJson.Serialize(new{scope="actual zero XP through Q504, returned real R2/R3/R4 original previous personal work; current R5 last nodes untouched",experience=10620,profile_path=s.MainPath,receipts=snap.Receipts,human_language="pending",native_human="pending"}));});
    }
}
