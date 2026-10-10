using Mist.Application.ChapterThree;
using Mist.Application.Contracts;
using Mist.Application.R3ShiftWork;
using Mist.Core.Contracts;
using Mist.Core.R3ShiftWork;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.R3ShiftWork;
public static class R3ShiftWorkCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static R3ShiftPackage Package()=>R3ShiftPackage.ParseJson(File.ReadAllText(Path.Combine(Root,"content/source/r3_shift_work/learner_package.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"r3-shift-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    public static (R3ShiftWorkService Service,ChapterThreeService Chapter) ActualEntry(ProfileStore s)
    {var (chapter,_)=ChapterThree.ChapterThreeCases.ActualArrival(s);var r=new ChapterThree.ChapterThreeCases.Run(s,chapter);r.Node301();r.Goto("R3-S08");r.Spot("R3-S08.demand_board");return(new(s,chapter,Package()),chapter);}
    public sealed class Run(ProfileStore store,R3ShiftWorkService service,ChapterThreeService chapter)
    {
        private readonly ChapterThree.ChapterThreeCases.Run _main=new(store,chapter);
        public void At(string spot){_main.Goto(spot.Split('.')[0]);_main.Spot(spot);}
        public void Sources(){foreach(var m in service.Package.Materials.Where(m=>m.ResponseActor=="")){At(m.Spot);Success(Done(service.DiscoverAsync(m.Id)));}At(service.Package.BoardSpot);Success(Done(service.AcceptWorkAsync()));Success(Done(service.TakeBoardAsync()));foreach(var p in service.Package.Staff){At(p.Spot);Success(Done(service.RequestAvailabilityAsync(p.Actor)));}}
        public ShiftPlan Plan(bool extra=false)=>new("ROLE-R3-SHIFT-SELA",extra?"ROLE-R3-SHIFT-NIA":"ROLE-R3-SHIFT-TOBIN",extra?"extra_help":"reassign",service.Package.Window,"excluded","retained","mark_on_same_board","not_agreed");
        public void Meaning(bool extra=false){At(service.Package.BoardSpot);var d=Done(store.GetDraftAsync(service.Package.DraftTask));Done(service.SaveDraftAsync(d?.Revision??0,d?.Id,"The original estimate stays on record. This is a finite desk shift and actual handover; the household reason remains private."));Success(Done(service.ConfirmMeaningAsync(Plan(extra),false)));}
        public ShiftWorkPreview Post(){At(service.Package.BoardSpot);var p=Done(service.PreviewPostAsync());Require(p.Ready,StrictJson.Serialize(p));Success(Done(service.ConfirmPreviewAsync(p.Token)));return p;}
        public void Handover(bool extra=false){At(service.Package.HandoverSpot);Success(Done(service.ReceiveShiftAsync("ROLE-R3-SHIFT-SELA","morning")));Success(Done(service.CheckHandoverAsync("ledger_checked_fittings_pending_marked")));At(extra?"R3-S08.demand_board":"R3-S08.auxiliary_receipt");Success(Done(service.ReceiveShiftAsync(extra?"ROLE-R3-SHIFT-NIA":"ROLE-R3-SHIFT-TOBIN","afternoon")));At(service.Package.ReceiptSpot);Success(Done(service.CompleteAsync()));}
    }
    public static void Register()
    {
        Add("CP-R3-04","Shift work uses full original public materials and two finite alternatives without private household details",()=>{var p=Package();Require(p.Materials.Length==6&&p.Hints.Length==3&&p.Staff.Select(x=>x.Spot).Distinct().Count()==3&&p.Materials.All(m=>m.English.Length>300&&m.Chinese.Length>80));});
        Add("CP-R3-04","Actual normal shift reassignment receives the same board and releases both posts once with zero rewards",()=>
        {using var s=Store("normal-reassign");var (service,chapter)=ActualEntry(s);var r=new Run(s,service,chapter);var before=Done(s.SnapshotAsync());r.Sources();r.Meaning();r.Post();r.At("R3-S08.auxiliary_receipt");Throws<InvalidOperationException>(()=>Done(service.ReceiveShiftAsync("ROLE-R3-SHIFT-TOBIN","afternoon")));Throws<InvalidOperationException>(()=>Done(service.CompleteAsync()));r.Handover();Success(Done(service.CompleteAsync()));var after=Done(s.SnapshotAsync());Require(before.Balances.OrderBy(x=>x.Key).SequenceEqual(after.Balances.OrderBy(x=>x.Key))&&after.Items.Count(i=>i.Id==R3ShiftWorkService.BoardId)==1&&after.Receipts.Count(r=>r.SemanticKey=="R3:shift-work:resolution")==1&&after.Entities.Count(e=>e.Id.StartsWith("workpost:ROLE-R3-SHIFT-",StringComparison.Ordinal)&&e.Kind=="released_post")==2&&after.Receipts.All(r=>r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));});
        Add("CP-R3-04","Extra named local afternoon help is an actual alternative and does not make the unselected person on duty",()=>
        {using var s=Store("extra-help");var (service,chapter)=ActualEntry(s);var r=new Run(s,service,chapter);r.Sources();r.Meaning(true);r.Post();r.Handover(true);var v=Done(service.ViewAsync());Require(v.State!.Completed&&v.Posted!.Plan.Afternoon=="ROLE-R3-SHIFT-NIA"&&!v.Snapshot.Entities.Any(e=>e.Id=="workpost:ROLE-R3-SHIFT-TOBIN")&&v.Snapshot.Balances["experience"]==2340);});
        Add("CP-R3-04","Draft edits, current named availability and continuation invalidate only an uncommitted shift preview",()=>
        {using var s=Store("CAS");var (service,chapter)=ActualEntry(s);var r=new Run(s,service,chapter);r.Sources();r.Meaning();var cancelled=Done(service.PreviewPostAsync());Require(cancelled.Ready);service.ClosePreview(cancelled.Token);Require(Done(service.ConfirmPreviewAsync(cancelled.Token)).Status==CommitStatus.Rejected&&!Done(service.ViewAsync()).State!.Posted);var p=Done(service.PreviewPostAsync());var d=Done(s.GetDraftAsync(service.Package.DraftTask))!;Done(service.SaveDraftAsync(d.Revision,d.Id,"New finite handover text."));Require(Done(service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);r.Meaning();p=Done(service.PreviewPostAsync());r.At("R3-S08.demand_board");Success(Done(service.RequestAvailabilityAsync("ROLE-R3-SHIFT-NIA")));Require(Done(service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);r.Meaning();p=Done(service.PreviewPostAsync());Done(s.ContinueAsync());Require(Done(service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&Done(s.GetDraftAsync(service.Package.DraftTask))!.Body is not null&&!Done(service.ViewAsync()).State!.Posted);});
        Add("CP-R3-05","Shift native entry has actual zero-XP through Q301, 2340XP and untouched optional work",()=>
        {using var s=Store("native-entry");var (service,_)=ActualEntry(s);var v=Done(service.ViewAsync());Require(v.State is null&&v.Materials.Length==0&&v.Snapshot.Balances["experience"]==2340&&v.Actor.Scene=="R3-S08"&&v.Snapshot.Receipts.All(r=>r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-r3-shift-native-entry.json"),StrictJson.Serialize(new{scope="actual normal zero-XP through Q301 and safe dispatcher path; no optional shift source/permission/stock seeded",v.Snapshot.Sequence,experience=2340,native_human="pending"}));});
    }
}
