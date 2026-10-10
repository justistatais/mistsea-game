using System.Collections.Immutable;
using Mist.Application.ChapterOne;
using Mist.Application.Contracts;
using Mist.Application.R1Life;
using Mist.Core.Campaign;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R1Life;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.R1Life;

public static class R1LifeCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static R1LifePackage Package()=>R1LifePackage.Parse(File.ReadAllText(Path.Combine(Root,"content/source/r1_life/learner_package.json")));
    public static ImmutableDictionary<string,SceneGeometry> Harbour(ProfileStore s)=>new ChapterOneService(s,Catalog,ChapterOne.ChapterOneCases.Package(),StrictJson.Deserialize<CampaignPackage>(File.ReadAllText(Path.Combine(Root,"content/source/campaign/first_entry.json"))).Scenes.ToImmutableDictionary(x=>x.Id)).Geometry;
    private static ProfileStore Store()=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"r1-life-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    // Explicit prior-history engineering fixture. It does not claim that prologue or registration was played.
    private static Run FixtureRun(ProfileStore s)
    {
        Fixture(s,"Q-101:resolution","temporary_maintenance_commission_issued",new{scope=CampaignRules.Scope,authority_actor="N-HAVEL",permission_source="Q-101:resolution",q102_entry_level=3,glimmer_transferred=false},
            [Entity(s,"actor","actor",new ActorState("R1-S02",new(48,90),0))]);
        return new(s,new(s,s,Package(),Harbour(s)));
    }
    public sealed class Run(ProfileStore store,R1LifeService service)
    {
        public R1LifeService Service {get;}=service;
        public R1LifeView View=>Done(Service.ViewAsync());
        public void Initialize()=>Success(Done(Service.InitializeRegionAsync()));
        public void Walk(Point2 to)
        {
            var a=Read<ActorState>(store,"actor");var path=new NavigationGrid(Service.Geometry[a.Scene]).FindPath(a.Position,to,true);Require(path.Length>0);Done(Service.TraverseAsync(path));
        }
        public void Spot(string id){var a=Read<ActorState>(store,"actor");Walk(Service.Geometry[a.Scene].Spots.Single(s=>s.Id==id).Stand);}
        public void Exit(string target){var a=Read<ActorState>(store,"actor");var port=Service.Geometry[a.Scene].Ports.Single(p=>p.TargetScene==target);Walk(port.Stand);Success(Done(Service.EnterPortAsync(port.Id)));}
        public void Material(string id){var m=Service.Package.Materials.Single(m=>m.Id==id);Spot(m.Spot);Success(Done(Service.DiscoverAsync(id)));Require(Done(Service.OpenMaterialAsync(id)).Fingerprint==m.Fingerprint);}
        public void Inn()
        {
            Initialize();Spot("R1-S02.inn_notice");Success(Done(Service.DiscoverInnAsync()));Exit("R1-O01");Spot("R1-O01.front_desk");Success(Done(Service.AcceptAsync(R1LifeRules.Rooms)));
        }
        public void PrepareRooms(bool scriptFirst=false)
        {
            foreach(string id in scriptFirst?new[]{"R1-ROOM-L-GUESTS","R1-ROOM-R-SEPARATE","R1-ROOM-R-AMEND","R1-ROOM-R-OLD","R1-ROOM-R-CARDS","R1-ROOM-R-DESK"}:new[]{"R1-ROOM-R-OLD","R1-ROOM-R-AMEND","R1-ROOM-R-SEPARATE","R1-ROOM-L-GUESTS","R1-ROOM-R-CARDS","R1-ROOM-R-DESK"})Material(id);
            Spot("R1-O01.guest_confirmation");Success(Done(Service.ConfirmArrivedGuestsAsync()));Spot("R1-O01.room_quiet");Success(Done(Service.InspectRoomAsync("QT-1")));Spot("R1-O01.room_ground");Success(Done(Service.InspectRoomAsync("GS-1")));Spot("R1-O01.ledger");
            Success(Done(Service.ArrangeRoomsAsync([new("BKG-17","QT-1"),new("BKG-22","GS-1")],ExtraLodgingArrangement.LocalInformation)));
        }
        public void Save(string quest,string body)
        {
            var task=Service.Package.Writing.Single(w=>w.Quest==quest);Spot(task.Spot);var prior=Done(store.GetDraftAsync(task.Id));Done(Service.SaveDraftAsync(quest,prior?.Id,prior?.Revision??0,body));
        }
        public void FinishRooms()
        {
            Save(R1LifeRules.Rooms,"BKG-17 version 2 moves the same two guests to Harbour Day 2. Keep version 1 as history. Quiet Twin meets their needs; Ground Single gives BKG-22 level access. Please record one local information appointment.");Success(Done(Service.ConfirmMeaningAsync(R1LifeRules.Rooms,true)));Spot("R1-O01.front_desk");var p=Done(Service.PreviewSubmissionAsync(R1LifeRules.Rooms));Require(p.Ready,string.Join(";",p.Issues));Success(Done(Service.ConfirmPreviewAsync(p.Token)));
        }
        public void Theatre()
        {
            Spot("R1-O01.theatre_offer");Success(Done(Service.DiscoverTheatreAsync()));Exit("R1-O02");Spot("R1-O02.programme");Success(Done(Service.AcceptAsync(R1LifeRules.Show)));
            foreach(string id in new[]{"R1-SHOW-R-OFFER","R1-SHOW-R-EQUIPMENT","R1-SHOW-L-CREW","R1-SHOW-R-STAGE"})
            {
                if(id=="R1-SHOW-R-OFFER"){Exit("R1-O01");Material(id);Exit("R1-O02");}else Material(id);
            }
        }
        public void PrepareShow(TheatreProgramme programme)
        {
            Spot("R1-O02.stage_check");Success(Done(Service.InspectStageAsync()));Spot("R1-O02.power");Success(Done(Service.LampActionAsync("isolate")));
            if(programme==TheatreProgramme.FullIndoor){Success(Done(Service.LampActionAsync("brace")));Success(Done(Service.LampActionAsync("cell")));Success(Done(Service.LampActionAsync("power")));Success(Done(Service.LampActionAsync("test")));}
            Spot("R1-O02.props");Success(Done(Service.PlacePropAsync("screen")));if(programme==TheatreProgramme.SmallDaylight)Success(Done(Service.PlacePropAsync("drum")));
            Spot("R1-O02.crew");Success(Done(Service.AssignRolesAsync(Roles(programme))));
        }
        public void FinishShow()
        {
            var roles=View.State.Roles!;Save(R1LifeRules.Show,$"We will give the {roles.Programme} programme from {roles.Starts} to {roles.Ends}. The organiser speaks and the second performer takes the assigned role. The screen and required equipment are prepared.");Success(Done(Service.ConfirmMeaningAsync(R1LifeRules.Show,false)));Spot("R1-O02.programme");var p=Done(Service.PreviewSubmissionAsync(R1LifeRules.Show));Require(p.Ready,string.Join(";",p.Issues));Success(Done(Service.ConfirmPreviewAsync(p.Token)));
        }
    }
    private static TheatreRoles Roles(TheatreProgramme p)=>new(p,R1LifeRules.Speaker,p==TheatreProgramme.FullIndoor?"lamp_operator":"drummer",R1LifeRules.Second,p==TheatreProgramme.FullIndoor?"18:00":"16:00",p==TheatreProgramme.FullIndoor?"18:30":"16:15");
    public static R1LifeService ActualEntry(ProfileStore s)
    {
        var coast=new Campaign.CampaignCases.Run(s);coast.ThroughCommission();var harbour=new ChapterOneService(s,Catalog,ChapterOne.ChapterOneCases.Package(),coast.Service.Geometry);
        var actor=Read<ActorState>(s,"actor");var port=harbour.Geometry[actor.Scene].Ports.Single(p=>p.TargetScene=="R1-S02");Done(harbour.MoveAsync(new NavigationGrid(harbour.Geometry[actor.Scene]).FindPath(actor.Position,port.Stand,true)));Success(Done(harbour.EnterPortAsync(port.Id)));
        var service=new R1LifeService(s,s,Package(),harbour.Geometry);Success(Done(service.InitializeRegionAsync()));return service;
    }
    public static R1LifeService NormalRooms(ProfileStore s)
    {
        var service=ActualEntry(s);var run=new Run(s,service);run.Inn();run.PrepareRooms(true);run.FinishRooms();return service;
    }
    public static void Register()
    {
        Add("CP-R1-03","life_geometry_reuses_original_inn_approach_and_rejects_duplicate_targets_without_changing_main_ports",()=>
        {
            using var s=Store();var original=Harbour(s);var p=Package();var service=new R1LifeService(s,s,p,original);var street=original["R1-S02"];
            Require(service.Geometry[street.Id].Ports.Count(x=>x.TargetScene=="R1-O01")==1&&service.Geometry[street.Id].Ports.Single(x=>x.TargetScene=="R1-O01")==street.Ports.Single(x=>x.TargetScene=="R1-O01"));
            Require(service.Geometry[street.Id].Ports.Where(x=>x.TargetScene!="R1-O01").SequenceEqual(street.Ports.Where(x=>x.TargetScene!="R1-O01"))&&service.Geometry[street.Id].Spots.Where(x=>x.Id!=p.StreetSpot.Id).SequenceEqual(street.Spots));
            var duplicate=street with{Ports=street.Ports.Add(p.StreetPort with{Id="R1-S02.conflicting_inn"})};Throws<InvalidDataException>(()=>new R1LifeService(s,s,p,original.SetItem(street.Id,duplicate)));
            var wrongTarget=street with{Ports=street.Ports.Select(x=>x.Id==p.StreetPort.Id?x with{TargetPort="unregistered"}:x).ToImmutableArray()};Throws<InvalidDataException>(()=>new R1LifeService(s,s,p,original.SetItem(street.Id,wrongTarget)));
        });
        Add("CP-R1-03","actual_zero_XP_R1_native_entry_preserves_unaccepted_life_work_and_real_340XP_receipts",()=>
        {
            string output=Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.Combine(Path.GetTempPath(),"MistSea-R1-Life-Entry-"+Guid.NewGuid());
            string path=Path.Combine(output,"normal-r1-life-native-entry");using var s=new ProfileStore(path,Catalog.ReleaseId);var service=ActualEntry(s);var v=Done(service.ViewAsync());
            Require(v.Snapshot.Balances["experience"]==340&&v.Actor.Scene=="R1-S02"&&!v.State.InnDiscovered&&!v.State.TheatreDiscovered&&v.Materials.IsEmpty);
            Require(v.Quests.All(q=>q.Lifecycle==QuestLifecycle.Unaccepted)&&v.Snapshot.Items.Count(i=>i.OwnerKind=="warehouse"&&i.OwnerId==R1LifeRules.Store)==4);
            Require(v.Snapshot.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            Done(s.BackupAsync());Done(s.CreateCheckpointAsync("manual"));
            File.WriteAllText(Path.Combine(path,"entry_evidence.json"),StrictJson.Serialize(new{scope="actual zero-XP prologue then Q101 registration and public street travel",experience=340,life_work="unaccepted",fixture_history=false,human_play="not_run"}));
        });
        Add("CP-R1-03","actual_zero_XP_prologue_registration_then_optional_inn_produces_one_25XP_result_without_mainline_changes",()=>
        {
            using var s=Store();var service=NormalRooms(s);var snap=Done(s.SnapshotAsync());Require(snap.Balances["experience"]==365&&snap.Receipts.Count(r=>r.SemanticKey=="SC-R1-001:resolution")==1&&snap.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));
            Require(Read<QuestState>(s,"quest:Q-102").Lifecycle==QuestLifecycle.Unaccepted&&!snap.Entities.Any(e=>e.Id=="ship:glimmer")&&Read<R1LifeState>(s,R1LifeService.StateId).Allocations.Length==2);Require(Done(service.ViewAsync()).Materials.Length==6);
        });
        Add("CP-R1-03","two_real_optional_spaces_have_reciprocal_ports_full_bilingual_materials_and_no_encounter_inflation",()=>
        {
            var p=Package();Require(p.Scenes.Length==2&&p.Materials.Length==11&&p.Materials.All(m=>m.Paragraphs.Length>=2&&m.AudioAsset==""&&m.HumanReview=="human_pending"));Require(p.PortGates.Count==4&&p.Bookings.Count(b=>b.Id=="BKG-17")==2&&R1LifeRules.CurrentBookings(p).Length==2);
            foreach(var scene in p.Scenes)foreach(var a in scene.Spots)foreach(var b in scene.Ports)Require(new NavigationGrid(scene).FindPath(a.Stand,b.Stand,true).Length>0);
        });
        Add("CP-R1-03","undiscovered_optional_entry_and_remote_actions_do_not_move_or_grant_work",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Initialize();var before=StrictJson.Serialize(Done(s.SnapshotAsync()));Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverInnAsync()));Require(StrictJson.Serialize(Done(s.SnapshotAsync()))==before);
            r.Walk(r.Service.Package.StreetPort.Stand);Throws<InvalidOperationException>(()=>Done(r.Service.EnterPortAsync(r.Service.Package.StreetPort.Id)));Require(Read<ActorState>(s,"actor").Scene=="R1-S02");
        });
        Add("CP-R1-03","copies_dates_and_two_room_constraints_are_actual_checked_records_not_three_bookings",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.PrepareRooms();var state=r.View.State;Require(state.GuestsConfirmed&&state.Allocations.Length==2);
            Throws<InvalidOperationException>(()=>Done(r.Service.ArrangeRoomsAsync([new("BKG-17","QT-1"),new("BKG-17","GS-1"),new("BKG-22","GS-1")],ExtraLodgingArrangement.LodgingDiscount)));
            Throws<InvalidOperationException>(()=>Done(r.Service.ArrangeRoomsAsync([new("BKG-17","GS-1"),new("BKG-22","QT-1")],ExtraLodgingArrangement.LocalInformation)));
        });
        Add("CP-R1-03","guest_and_room_inspection_cannot_be_replaced_by_only_opening_letters",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();foreach(var m in r.Service.Package.Materials.Where(m=>m.Quest==R1LifeRules.Rooms))r.Material(m.Id);r.Spot("R1-O01.ledger");
            Throws<InvalidOperationException>(()=>Done(r.Service.ArrangeRoomsAsync([new("BKG-17","QT-1"),new("BKG-22","GS-1")],ExtraLodgingArrangement.LocalInformation)));Require(r.View.State.Allocations.IsEmpty);
        });
        Add("CP-R1-03","script_first_or_letter_first_leads_to_same_real_room_allocation_without_audio_claim",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.PrepareRooms(true);r.FinishRooms();Require(Done(s.SnapshotAsync()).Balances["experience"]==25);
            var result=Done(s.SnapshotAsync()).Receipts.Single(r=>r.SemanticKey=="SC-R1-001:resolution");Require(result.ResultJson.Contains("not_evaluated",StringComparison.Ordinal)&&result.ResultJson.Contains("structured_assistance",StringComparison.Ordinal));
        });
        Add("CP-R1-03","inn_pause_continue_uses_same_instance_and_real_clock_never_evicts_guests",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();var instance=Read<QuestState>(s,"quest:SC-R1-001").Instance;Success(Done(r.Service.PauseAsync(R1LifeRules.Rooms)));Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmArrivedGuestsAsync()));Done(s.ContinueAsync());r.Spot("R1-O01.front_desk");Success(Done(r.Service.ResumeAsync(R1LifeRules.Rooms)));r.PrepareRooms();r.FinishRooms();Require(Read<QuestState>(s,"quest:SC-R1-001").Instance==instance&&Done(s.SnapshotAsync()).Balances["experience"]==25);
        });
        Add("CP-R1-03","innkeeper_foreground_occupation_delays_acceptance_without_cloning_or_consuming_reward",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Initialize();r.Spot("R1-S02.inn_notice");Success(Done(r.Service.DiscoverInnAsync()));r.Exit("R1-O01");r.Spot("R1-O01.front_desk");Require(Done(s.TryLeaseAsync("N-INNKEEPER","other-foreground")));
            Throws<InvalidOperationException>(()=>Done(r.Service.AcceptAsync(R1LifeRules.Rooms)));Require(Read<QuestState>(s,"quest:SC-R1-001").Lifecycle==QuestLifecycle.Unaccepted);
            Done(s.ReleaseLeasesAsync(s.Session.Load,"other-foreground"));Success(Done(r.Service.AcceptAsync(R1LifeRules.Rooms)));Require(Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==0);
        });
        Add("CP-R1-03","inn_reward_replay_and_reopen_have_one_semantic_receipt_and_no_new_credit",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.PrepareRooms();r.FinishRooms();var before=Done(s.SnapshotAsync());Done(s.ContinueAsync());r.Spot("R1-O01.front_desk");Require(!Done(r.Service.PreviewSubmissionAsync(R1LifeRules.Rooms)).Ready);Success(Done(r.Service.InitializeRegionAsync()));var after=Done(s.SnapshotAsync());Require(after.Balances["experience"]==25&&after.Receipts.Count(r=>r.SemanticKey=="SC-R1-001:resolution")==1&&after.Items.Length==before.Items.Length);
        });
        Add("CP-R1-03","changed_saved_confirmation_invalidates_preview_and_does_not_half_award",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.PrepareRooms();r.Save(R1LifeRules.Rooms,"The two current orders fit the inspected rooms.");Success(Done(r.Service.ConfirmMeaningAsync(R1LifeRules.Rooms,false)));r.Spot("R1-O01.front_desk");var p=Done(r.Service.PreviewSubmissionAsync(R1LifeRules.Rooms));Require(p.Ready);r.Save(R1LifeRules.Rooms,"Changed confirmation with revised explanation.");r.Spot("R1-O01.front_desk");var result=Done(r.Service.ConfirmPreviewAsync(p.Token));Require(result.Status==CommitStatus.Rejected&&Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==0&&Read<QuestState>(s,"quest:SC-R1-001").Lifecycle==QuestLifecycle.Active);
        });
        Add("CP-R1-03","declining_inn_and_pausing_show_does_not_add_mainline_or_ending_requirements",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Initialize();r.Spot("R1-S02.inn_notice");Success(Done(r.Service.DiscoverInnAsync()));r.Exit("R1-O01");r.Spot("R1-O01.front_desk");Success(Done(r.Service.DeclineAsync(R1LifeRules.Rooms)));r.Theatre();
            // The theatre was explicitly accepted; pausing preserves it without forcing a result.
            Success(Done(r.Service.PauseAsync(R1LifeRules.Show)));Require(Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==0&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Kind is "ending_policy" or "tower_readiness"));
        });
        foreach(var programme in new[]{TheatreProgramme.FullIndoor,TheatreProgramme.SmallDaylight})
        {
            var chosen=programme;Add("CP-R1-03","actual_theatre_"+chosen+"_has_finite_local_objects_roles_programme_and_zero_reward",()=>
            {
                using var s=Store();var r=FixtureRun(s);r.Inn();r.Theatre();r.PrepareShow(chosen);r.FinishShow();var snap=Done(s.SnapshotAsync());Require(snap.Balances.GetValueOrDefault("experience")==0&&snap.Receipts.Count(r=>r.SemanticKey=="SC-R1-002:resolution")==1&&snap.Items.Count(i=>i.Id.StartsWith("R1-THEATRE-",StringComparison.Ordinal))==4);
                Require(snap.Items.Single(i=>i.Id=="R1-THEATRE-SCREEN") is{OwnerKind:"installed",OwnerId:R1LifeRules.Stage});Require(chosen==TheatreProgramme.FullIndoor?r.View.State.LampReady:snap.Items.Single(i=>i.Id=="R1-THEATRE-CELL").OwnerKind=="warehouse"&&r.View.State.LampIsolated);
                r.Spot("R1-O02.performance");Success(Done(r.Service.WatchShowAsync(true)));Success(Done(r.Service.WatchShowAsync(false)));r.Material("R1-SHOW-R-SCENE");Require(Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==0&&r.View.State.ShowWatched);
            });
        }
        Add("CP-R1-03","lamp_changes_invalidate_actual_test_and_cannot_power_without_own_brace_cell",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.Theatre();r.Spot("R1-O02.power");Throws<InvalidOperationException>(()=>Done(r.Service.LampActionAsync("power")));r.PrepareShow(TheatreProgramme.FullIndoor);r.Spot("R1-O02.power");Success(Done(r.Service.LampActionAsync("isolate")));Success(Done(r.Service.LampActionAsync("power")));Require(!r.View.State.LampReady);r.Save(R1LifeRules.Show,"Full programme.");Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmMeaningAsync(R1LifeRules.Show,false)));
        });
        Add("CP-R1-03","props_transfer_once_and_duplicate_install_does_not_consume_or_create_items",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.Theatre();r.Spot("R1-O02.props");Success(Done(r.Service.PlacePropAsync("screen")));var count=Done(s.SnapshotAsync()).Items.Length;Success(Done(r.Service.PlacePropAsync("screen")));Require(Done(s.SnapshotAsync()).Items.Length==count);r.Spot("R1-O02.power");Success(Done(r.Service.LampActionAsync("brace")));Success(Done(r.Service.LampActionAsync("brace")));Require(Done(s.SnapshotAsync()).Items.Length==count);
        });
        Add("CP-R1-03","one_actor_two_roles_wrong_time_or_unprepared_programme_is_rejected",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.Theatre();r.Spot("R1-O02.crew");Throws<InvalidOperationException>(()=>Done(r.Service.AssignRolesAsync(Roles(TheatreProgramme.FullIndoor) with{SecondPerson=R1LifeRules.Speaker})));
            Throws<InvalidOperationException>(()=>Done(r.Service.AssignRolesAsync(Roles(TheatreProgramme.SmallDaylight) with{Starts="18:00"})));r.Spot("R1-O02.programme");Require(!Done(r.Service.PreviewSubmissionAsync(R1LifeRules.Show)).Ready);
        });
        Add("CP-R1-03","restored_scene_is_unavailable_before_actual_programme_receipt",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.Theatre();r.Spot("R1-O02.performance");Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverAsync("R1-SHOW-R-SCENE")));Throws<InvalidOperationException>(()=>Done(r.Service.WatchShowAsync(false)));Require(r.View.Materials.All(m=>m.Id!="R1-SHOW-R-SCENE"));
        });
        Add("CP-R1-03","old_load_submission_preview_cannot_issue_inn_reward",()=>
        {
            using var s=Store();var r=FixtureRun(s);r.Inn();r.PrepareRooms();r.Save(R1LifeRules.Rooms,"The rooms meet the current bookings.");Success(Done(r.Service.ConfirmMeaningAsync(R1LifeRules.Rooms,false)));r.Spot("R1-O01.front_desk");var p=Done(r.Service.PreviewSubmissionAsync(R1LifeRules.Rooms));Require(p.Ready);Done(s.ContinueAsync());Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmPreviewAsync(p.Token)));Require(Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==0);
        });
    }
}
