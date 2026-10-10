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

public static class SideWellObservationCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static ProfileStore Store(string label)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"sidewell-normal-"+label+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static string World(BranchSnapshot s)=>StrictJson.Serialize(new{entities=s.Entities.Where(e=>e.Id!="actor"&&!e.Id.StartsWith("R3-O02:",StringComparison.Ordinal)&&e.Id!="material:"+SideWellObservationService.Material&&!e.Id.StartsWith("walked:R3-O02:",StringComparison.Ordinal)&&!e.Id.StartsWith("walked:R3-S05:R3-O02",StringComparison.Ordinal)&&e.Id!="travel-gate:R3-O02:R3-S05").OrderBy(e=>e.Id),balances=s.Balances.OrderBy(x=>x.Key),items=s.Items.OrderBy(x=>x.Id)});
    private static readonly ImmutableDictionary<string,string> Fields=new Dictionary<string,string>{{"side","one_load_five_reading"},{"main","one_load_four_reading"},{"comparison","different_locations_conditions"},{"unknown_conditions","unknown_not_copied"},{"causality","not_proven"},{"next_check","record_instrument_pump_stage"},{"sampling","no_additional_assignment"}}.ToImmutableDictionary();
    private sealed class Run
    {
        public ProfileStore Store {get;}public ChapterThreeCases.Run Chapter {get;}public SideWellObservationService Side {get;}
        public Run(ProfileStore s,bool ready=true){Store=s;var p=ChapterThreeCases.ActualArrival(s);Chapter=new(s,p.Service);Side=new(s,Catalog,p.Service.Package);if(ready){Chapter.Node301();ToPort();}}
        public void ToPort(){Chapter.Goto("R3-S05");Chapter.Walk(Chapter.View.Geometry["R3-S05"].Ports.Single(x=>x.TargetScene=="R3-O02").Stand);}
        public void Inspect(){Success(Done(Side.InspectEntryAsync()));Success(Done(Side.RegisterEntrySafetyAsync()));}
        public void Enter(){var r=Done(Side.EnterPortAsync("R3-S05.to.R3-O02"));Success(r);Success(Done(Side.RecordWalkedRouteAsync(r.Receipt!.Id.Value.ToString())));}
        public void At(string suffix)=>Chapter.Spot("R3-O02."+suffix);
        public void Read(){At("record");Success(Done(Side.DiscoverObservationAsync()));}
        public void Meaning(ImmutableDictionary<string,string>? fields=null){At("side_route");var d=Done(Store.GetDraftAsync(SideWellObservationService.Task));Done(Side.SaveDraftAsync(d?.Revision??0,d?.Id,"The side-well and cutting-face sheets both show one local extraction unit, but readings five and four come from different places and stages. Sampling conditions and pump details remain unknown. A later controlled comparison should retain instrument, pump state and stage. This note neither proves mist causation nor authorizes another sample."));Success(Done(Side.ConfirmMeaningAsync(fields??Fields,true)));}
        public void Leave(){Chapter.Walk(Chapter.View.Geometry["R3-O02"].Ports.Single().Stand);var r=Done(Side.EnterPortAsync("R3-O02.to.R3-S05"));Success(r);Success(Done(Side.RecordWalkedRouteAsync(r.Receipt!.Id.Value.ToString())));}
    }
    public static void Register()
    {
        Add("CP-OPTIONAL-SIDEWELL-03","Normal R3 source requires actual local authority, installed kit, rail control and persistent ladder before the separately inspected optional connection",()=>
        {using var s=Store("guard");var r=new Run(s,false);long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(r.Side.InspectEntryAsync()));Throws<InvalidOperationException>(()=>Done(r.Side.EnterPortAsync("R3-S05.to.R3-O02")));Require(s.Session.Sequence==seq);r.Chapter.Material("R3-Q301-R-LANDING");r.Chapter.Act("register_landing");r.Chapter.Material("R3-Q301-R-COMMISSION");r.Chapter.Act("request_local_work");r.Chapter.Material("R3-Q301-R-BASIC");r.Chapter.Act("inspect_households");r.Chapter.Act("receive_basic_supplies");r.Chapter.Material("R3-Q301-R-LADDER");r.Chapter.Act("inspect_ladder");r.ToPort();seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(r.Side.InspectEntryAsync()));Require(s.Session.Sequence==seq&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id==SideWellObservationService.SafetyId));r.Chapter.Material("R3-Q301-R-RAIL");r.Chapter.Battle("ENC-R3-01");r.ToPort();Throws<InvalidOperationException>(()=>Done(r.Side.EnterPortAsync("R3-S05.to.R3-O02")));r.Inspect();Require(r.Side.PortIssue(r.Chapter.View.Snapshot,r.Chapter.View.Geometry["R3-S05"].Ports.Single(x=>x.TargetScene=="R3-O02")) is null);r.Enter();Require(r.Chapter.View.Actor.Scene=="R3-O02"&&Done(s.SnapshotAsync()).Balances["experience"]==1920&&r.Chapter.Service.PortIssue(r.Chapter.View.Snapshot,r.Chapter.View.Geometry["R3-O02"].Ports.Single()) is not null&&Done(s.SnapshotAsync()).Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));});
        Add("CP-OPTIONAL-SIDEWELL-03","Actual inspected paired travel, original public readings and bounded local comparison register once without main evidence, extra sample, balances or items",()=>
        {using var s=Store("comparison");var r=new Run(s);string world=World(Done(s.SnapshotAsync()));r.Inspect();r.Enter();r.Read();Require(Done(r.Side.OpenObservationAsync()).Fingerprint==r.Side.Observation.Fingerprint);r.Meaning();var p=Done(r.Side.PreviewComparisonAsync());Require(p.Ready,StrictJson.Serialize(p));long seq=s.Session.Sequence;r.Side.ClosePreview(p.Token);Require(Done(r.Side.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&s.Session.Sequence==seq);p=Done(r.Side.PreviewComparisonAsync());Success(Done(r.Side.ConfirmPreviewAsync(p.Token)));Require(Done(r.Side.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted);var snap=Done(s.SnapshotAsync());var result=StrictJson.Deserialize<SideWellComparisonResult>(snap.Entities.Single(e=>e.Id==SideWellObservationService.ResultId).Json);Require(result is{Causality:"not_proven",Sampling:"no_additional_assignment",Experience:0,Marks:0,Parts:0}&&snap.Balances["experience"]==2340&&snap.Receipts.Count(x=>x.SemanticKey==SideWellObservationService.Completion)==1&&snap.Items.All(i=>i.Id!="R3-O02:sample")&&World(snap)==world);r.Leave();Require(r.Chapter.View.Actor.Scene=="R3-S05"&&World(Done(s.SnapshotAsync()))==world);});
        Add("CP-OPTIONAL-SIDEWELL-03","Copied unknown conditions or causal verdicts cannot submit; saved draft CAS and Continue invalidate old comparison previews",()=>
        {using var s=Store("draft");var r=new Run(s);r.Inspect();r.Enter();r.Read();r.Meaning(Fields.SetItem("causality","mist_cause_proven").SetItem("unknown_conditions","copied_from_main"));Require(!Done(r.Side.PreviewComparisonAsync()).Ready);r.Meaning();var p=Done(r.Side.PreviewComparisonAsync());Require(p.Ready);var d=Done(s.GetDraftAsync(SideWellObservationService.Task))!;Done(r.Side.SaveDraftAsync(d.Revision,d.Id,d.Body+" changed"));Throws<StoreConflictException>(()=>Done(r.Side.SaveDraftAsync(d.Revision,d.Id,"stale overwrite")));Require(Done(r.Side.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);r.Meaning();p=Done(r.Side.PreviewComparisonAsync());Require(p.Ready);Done(s.ContinueAsync());Require(Done(r.Side.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id==SideWellObservationService.ResultId));p=Done(r.Side.PreviewComparisonAsync());Require(p.Ready);Success(Done(r.Side.ConfirmPreviewAsync(p.Token)));});
        Add("CP-OPTIONAL-SIDEWELL-03","A real pump revision expires a previously registered optional gate and requires current inspection while preserving the independent return ladder",()=>
        {using var s=Store("safety-version");var r=new Run(s);r.Inspect();r.Chapter.Goto("R3-S06");r.Chapter.Spot("R3-S06.drain_control");Success(Done(r.Chapter.Service.SetPumpModeAsync("isolated_drain")));r.ToPort();long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(r.Side.EnterPortAsync("R3-S05.to.R3-O02")));Require(s.Session.Sequence==seq&&r.Chapter.View.State.LadderSafe);r.Inspect();r.Enter();r.At("record");Throws<InvalidOperationException>(()=>Done(r.Side.EnterPortAsync("R3-O02.to.R3-S05")));r.Leave();Require(r.Chapter.View.Actor.Scene=="R3-S05"&&r.Chapter.View.State.LadderSafe&&!r.Chapter.View.State.MineRouteSafe);});
    }
}
