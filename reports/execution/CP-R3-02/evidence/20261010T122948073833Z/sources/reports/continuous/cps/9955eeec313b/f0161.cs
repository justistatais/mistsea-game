using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.OptionalLocalWorks;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.OptionalLocalWorks;
using Mist.Infrastructure.Storage;
using Mist.Tests.ChapterThree;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.OptionalLocalWorks;

public static class MusicBoxRepairCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static ProfileStore Store(string label)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"musicbox-normal-"+label+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult result)=>Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(result));
    private static MusicBoxWorkState State(ProfileStore s)=>StrictJson.Deserialize<MusicBoxWorkState>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==MusicBoxRepairService.StateId).Json);
    public sealed class Run
    {
        public ProfileStore Store {get;}
        public ChapterThreeCases.Run Chapter {get;}
        public MusicBoxRepairService Service {get;}
        public Run(ProfileStore store)
        {
            Store=store;var arrival=ChapterThreeCases.ActualArrival(store);Chapter=new(store,arrival.Service);
            Chapter.Material("R3-Q301-R-LANDING");Chapter.Act("register_landing");Chapter.Goto("R3-O01");Service=new(store,Catalog,arrival.Service.Package);
        }
        public void At(string suffix){Chapter.Goto("R3-O01");Chapter.Spot("R3-O01."+suffix);}
        public void Accept(){At("owner");Success(Done(Service.DecideInvitationAsync("accept")));At("parts");Success(Done(Service.DiscoverSpecificationAsync()));}
        public void Approved()
        {Accept();At("musicbox");Success(Done(Service.InspectMechanismAsync()));At("parts");Success(Done(Service.SelectWheelAsync("MBOX-12",12)));At("owner");Success(Done(Service.RequestOwnerApprovalAsync()));}
        public void Fitted()
        {Approved();At("musicbox");Success(Done(Service.IsolateMechanismAsync()));Success(Done(Service.FitApprovedWheelAsync()));}
        public void Returned()
        {Fitted();Success(Done(Service.CheckTurningAsync()));At("owner");Success(Done(Service.ReturnSameBoxAsync()));}
        public void Meaning(string memory="private_not_published")
        {
            At("note");var head=Done(Store.GetDraftAsync(MusicBoxRepairService.Task));Done(Service.SaveDraftAsync(head?.Revision??0,head?.Id,"The MBOX-12 twelve-tooth wheel was fitted with the owner's approval. Its turning mechanism was checked, and the same box was returned to its owner. The work concerned only the mechanism. The family memory remains private."));
            Success(Done(Service.ConfirmNoteMeaningAsync(new Dictionary<string,string>{{"part","MBOX-12"},{"check","turning_mechanism_only"},{"box",MusicBoxRepairService.Box},{"return","same_owner"},{"limits","mechanism_only"},{"memory",memory}}.ToImmutableDictionary(),true)));
        }
        public MusicBoxPreview Preview(){At("owner");return Done(Service.PreviewDeliveryAsync());}
    }
    // These assertions concern world consequences, not an implementation-shaped list of local state flags.
    private static string MainWorld(BranchSnapshot snapshot)=>StrictJson.Serialize(new{
        entities=snapshot.Entities.Where(e=>e.Id=="chapter3"||e.Kind is "quest" or "shared_vessel" or "tower_readiness" or "permission" or "companion_roster" or "workpost"||e.Id.StartsWith("R3:worker:",StringComparison.Ordinal)||e.Id.Contains("actual_handover",StringComparison.Ordinal)).ToImmutableArray(),
        items=snapshot.Items.Where(i=>!i.Id.StartsWith("R3-O01:",StringComparison.Ordinal)).ToImmutableArray(),balances=snapshot.Balances});
    public static void Register()
    {
        Add("CP-OPTIONAL-MUSICBOX-05","Actual normal arrival exports an untouched SQLite profile for native music-box controls",()=>{
            using var s=Store("native-entry");var arrival=ChapterThreeCases.ActualArrival(s);var view=Done(arrival.Service.ViewAsync());
            Require(view.Actor.Scene=="R3-S01"&&view.Snapshot.Balances["experience"]==1920&&!view.State.Landing&&!view.State.Commission&&view.Materials.Length==0&&!view.Snapshot.Entities.Any(e=>e.Id==MusicBoxRepairService.StateId)&&!view.Snapshot.Items.Any(i=>i.Id.StartsWith("R3-O01:",StringComparison.Ordinal))&&view.Snapshot.Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            Done(s.CreateCheckpointAsync("manual"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-musicbox-native-entry.json"),StrictJson.Serialize(new{scope="actual normal zero-XP preceding chapters and typed R2R3 arrival; untouched local music-box state for actual native controls",profile_root=s.RootPath,sequence=view.Snapshot.Sequence,experience=1920,actor=view.Actor,actual_receipts=view.Snapshot.Receipts,human_native_play="not_run",physical_ime="not_run",language_calibration="human_pending"}));
        });
        Add("CP-OPTIONAL-MUSICBOX","Normal zero-XP prior chain, owner-approved actual same-box repair and private note settle once without main or six-tower consequences",()=>{
            using var s=Store("complete");var r=new Run(s);var before=Done(s.SnapshotAsync());string world=MainWorld(before);Require(before.Balances["experience"]==1920&&!before.Receipts.Any(x=>x.ActionType.StartsWith("fixture",StringComparison.Ordinal)));
            r.Returned();r.Meaning();var p=r.Preview();Require(p.Ready,StrictJson.Serialize(p));var snapshot=Done(s.SnapshotAsync());Require(MainWorld(snapshot)==world&&!State(s).Completed);Success(Done(r.Service.ConfirmPreviewAsync(p.Token)));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted);
            var after=Done(s.SnapshotAsync());Require(MainWorld(after)==world&&State(s).Completed&&after.Receipts.Count(x=>x.SemanticKey==MusicBoxRepairService.Completion)==1&&after.Items.Single(x=>x.Id==MusicBoxRepairService.Box) is{OwnerKind:"warehouse",OwnerId:MusicBoxRepairService.Owner}&&after.Items.Single(x=>x.Id==MusicBoxRepairService.Wheel12) is{OwnerKind:"installed",OwnerId:MusicBoxRepairService.Box}&&after.Items.Single(x=>x.Id==MusicBoxRepairService.Wheel14) is{OwnerKind:"warehouse",OwnerId:MusicBoxRepairService.PartsStore});
            var result=StrictJson.Deserialize<MusicBoxRepairDelivery>(after.Receipts.Single(x=>x.SemanticKey==MusicBoxRepairService.Completion).ResultJson);Require(result.MemoryDisposition=="private_not_published"&&result.Experience==0&&result.Marks==0&&result.Parts==0&&result.Owner==MusicBoxRepairService.Owner&&result.StructuredAssistance);Require(!r.Preview().Ready);
            Success(Done(r.Service.DecideInvitationAsync("withdraw")));Throws<InvalidOperationException>(()=>Done(r.Service.OpenSpecificationAsync()));Require(State(s).Completed&&Done(s.SnapshotAsync()).Receipts.Any(x=>x.SemanticKey==MusicBoxRepairService.Completion));Done(s.ContinueAsync());Require(State(s).Completed&&MainWorld(Done(s.SnapshotAsync()))==world);
        });
        Add("CP-OPTIONAL-MUSICBOX","Wrong stand, fourteen-tooth part, missing owner approval/isolation/test and different actual box are rejected",()=>{
            using var s=Store("guards");var r=new Run(s);r.At("parts");Throws<InvalidOperationException>(()=>Done(r.Service.DecideInvitationAsync("accept")));Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverSpecificationAsync()));r.Accept();r.At("musicbox");Success(Done(r.Service.InspectMechanismAsync()));r.At("parts");Throws<InvalidOperationException>(()=>Done(r.Service.SelectWheelAsync("MBOX-14",14)));Require(!State(s).Fitted&&Done(s.SnapshotAsync()).Items.Single(x=>x.Id==MusicBoxRepairService.Wheel14).OwnerKind=="warehouse");Success(Done(r.Service.SelectWheelAsync("MBOX-12",12)));r.At("musicbox");Throws<InvalidOperationException>(()=>Done(r.Service.FitApprovedWheelAsync()));r.At("owner");Success(Done(r.Service.RequestOwnerApprovalAsync()));r.At("musicbox");Throws<InvalidOperationException>(()=>Done(r.Service.FitApprovedWheelAsync()));Success(Done(r.Service.IsolateMechanismAsync()));Success(Done(r.Service.FitApprovedWheelAsync()));r.At("owner");Throws<InvalidOperationException>(()=>Done(r.Service.ReturnSameBoxAsync()));r.At("musicbox");Success(Done(r.Service.CheckTurningAsync()));
            // Negative-only custody tampering: the normal repair was real; moving its original box away must not let another serial stand in.
            Mist.Tests.NPC.NpcCases.Fixture(s,"negative:musicbox-custody","negative_custody_probe",new{not_positive_setup=true},items:[new(MusicBoxRepairService.Box,"R3-MUSIC-BOX","installed",MusicBoxRepairService.Bench,"warehouse","negative-other-owner",null),new("negative:other-musicbox","R3-MUSIC-BOX","new","","installed",MusicBoxRepairService.Bench,null)]);
            r.At("owner");Throws<InvalidOperationException>(()=>Done(r.Service.ReturnSameBoxAsync()));Require(!State(s).Returned&&!Done(s.SnapshotAsync()).Receipts.Any(x=>x.SemanticKey==MusicBoxRepairService.Completion));
        });
        Add("CP-OPTIONAL-MUSICBOX","Withdrawing permission after fitting hides private original, preserves installed history and allows unrewarded safe same-box return",()=>{
            using var s=Store("withdraw");var r=new Run(s);r.Fitted();r.At("owner");Success(Done(r.Service.DecideInvitationAsync("withdraw")));Require(Done(r.Service.ViewAsync()).Specification is null);Throws<InvalidOperationException>(()=>Done(r.Service.OpenSpecificationAsync()));r.At("musicbox");Throws<InvalidOperationException>(()=>Done(r.Service.CheckTurningAsync()));r.At("owner");Success(Done(r.Service.ReturnPausedBoxAsync()));var snap=Done(s.SnapshotAsync());Require(!State(s).Completed&&State(s).Returned&&snap.Items.Single(i=>i.Id==MusicBoxRepairService.Wheel12).OwnerKind=="installed"&&snap.Items.Single(i=>i.Id==MusicBoxRepairService.Box).OwnerId==MusicBoxRepairService.Owner&&snap.Entities.Any(e=>e.Id=="material:"+MusicBoxRepairService.Material)&&snap.Receipts.Any(x=>x.ActionType=="musicbox_approved_wheel_fitted")&&snap.Balances["experience"]==1920&&!snap.Receipts.Any(x=>x.SemanticKey==MusicBoxRepairService.Completion));
            Success(Done(r.Service.DecideInvitationAsync("accept")));Throws<InvalidOperationException>(()=>Done(r.Service.ResumeSameBoxAsync()));Success(Done(r.Service.RequestOwnerApprovalAsync()));Success(Done(r.Service.ResumeSameBoxAsync()));r.At("musicbox");Success(Done(r.Service.CheckTurningAsync()));r.At("owner");Success(Done(r.Service.ReturnSameBoxAsync()));r.Meaning();var resumed=r.Preview();Require(resumed.Ready);Success(Done(r.Service.ConfirmPreviewAsync(resumed.Token)));Require(Done(s.SnapshotAsync()).Items.Count(i=>i.Id.StartsWith("R3-O01:",StringComparison.Ordinal))==3&&State(s).Completed&&Done(s.SnapshotAsync()).Balances["experience"]==1920);
        });
        Add("CP-OPTIONAL-MUSICBOX","Private memory claim and saved prose alone cannot settle; preview is read-only, closes, binds real draft CAS and rejects Continue generation",()=>{
            using var s=Store("draft");var r=new Run(s);r.Returned();r.At("note");Done(r.Service.SaveDraftAsync(0,null,"A saved repair note is only a draft."));Require(!r.Preview().Ready);r.Meaning("published");Require(r.Preview().Issues.Any(i=>i.Code=="meaning_invalid"));r.Meaning();var p=r.Preview();Require(p.Ready);var before=StrictJson.Serialize(Done(s.SnapshotAsync()));var another=Done(r.Service.PreviewDeliveryAsync());Require(another.Ready&&StrictJson.Serialize(Done(s.SnapshotAsync()))==before);r.Service.ClosePreview(another.Token);Require(Done(r.Service.ConfirmPreviewAsync(another.Token)).Status==CommitStatus.Rejected);
            var draft=Done(s.GetDraftAsync(MusicBoxRepairService.Task))!;Done(r.Service.SaveDraftAsync(draft.Revision,draft.Id,"Changed English remains unsubmitted."));Throws<StoreConflictException>(()=>Done(r.Service.SaveDraftAsync(draft.Revision,draft.Id,"Stale overwrite")));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&!State(s).Completed);r.Meaning();var current=r.Preview();Require(current.Ready);Done(s.ContinueAsync());Require(Done(r.Service.ConfirmPreviewAsync(current.Token)).Status==CommitStatus.Rejected&&!State(s).Completed);r.At("owner");var fresh=Done(r.Service.PreviewDeliveryAsync());Require(fresh.Ready);Success(Done(r.Service.ConfirmPreviewAsync(fresh.Token)));
        });
    }
}
