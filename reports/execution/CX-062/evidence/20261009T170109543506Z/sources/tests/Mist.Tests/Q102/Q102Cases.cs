using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Materials;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Application.UI;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;

namespace Mist.Tests.Q102;

public static class Q102Cases
{
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static RuntimeCatalog Catalog()=>StrictJson.Deserialize<RuntimeCatalog>(File.ReadAllText(Path.Combine(ProjectRoot,"game/content/runtime_catalog.json")));
    private static string Root(string name){string path=Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private static void Require(bool good,string detail="assertion failed")=>TestRegistry.Require(good,detail);
    public static void Register()
    {
        TestRegistry.Add("CX-023","Nested ownership, editing, IME and background input cannot execute world",()=>
        {
            var r=new InputRouting();r.Push("reader",InputOwner.Workspace);r.Push("dictionary",InputOwner.Workspace);r.Pop();Require(r.WorldPaused&&!r.MayExecuteWorld(false,false,false,false));r.Pop();
            Require(!r.MayExecuteWorld(true,false,false,false)&&!r.MayExecuteWorld(false,true,false,false)&&!r.MayExecuteWorld(false,false,false,true)&&!r.MayExecuteWorld(false,false,true,false));
            Require(r.MayExecuteWorld(false,false,false,false)&&!InputRouting.MayMutateCombatFromView("open_archive"));
        });
        TestRegistry.Add("CX-024","Body radius, obstacles and screen transforms hold at five viewport sizes",()=>
        {
            var geometry=new SceneGeometry("test",new(0,0,100,100),[new(40,30,60,70)],[],[],[]);var nav=new NavigationGrid(geometry);
            Require(!nav.CanStand(new(0.5,50))&&!nav.CanStand(new(39,50))&&!nav.SegmentClear(new(20,50),new(80,50)));
            var path=nav.FindPath(new(20,50),new(80,50));Require(path.Length>1);Point2 previous=new(20,50);foreach(var p in path){Require(nav.SegmentClear(previous,p));previous=p;}
            foreach(var viewport in new Bounds[]{new(24,50,800,650),new(24,50,2560,1080),new(24,50,3840,1600),new(48,100,1200,1200),new(16,32,400,300)})
            foreach(var point in new Point2[]{new(5,8),new(50,70),new(95,90)})Require(point.Distance(NavigationGrid.ScreenToWorld(NavigationGrid.WorldToScreen(point,viewport),viewport))<1e-9);
        });
        TestRegistry.Add("CX-025","All fourteen directed exits walk, pair and restore the registered stand point",()=>
        {
            using var store=new ProfileStore(Root("all-exits"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Service.InitializeSliceAsync());
            Done(run.Exit("R1-S05"));Done(run.Exit("R1-S07"));Done(run.Spot("R1-S07.local_controller"));Done(run.Service.WorkshopModeAsync(WorkshopMode.Isolated));Done(run.Exit("R1-S06"));Done(run.Spot("R1-S06.shortcut_latch"));Done(run.Service.OpenLatchAsync());Done(run.Exit("R1-S02"));
            foreach(string target in new[]{"R1-S03","R1-S02","R1-S04","R1-S02","R1-S05","R1-S06","R1-S05","R1-S07","R1-S06","R1-S07","R1-S05","R1-S02","R1-S06","R1-S02"})
            {
                var before=Done(run.Service.ViewAsync());var port=run.Service.Geometry[before.Actor.Scene].Ports.Single(p=>p.TargetScene==target);Done(run.Exit(target));var after=Done(run.Service.ViewAsync());
                Require(after.Actor.Scene==target&&after.Actor.Position==run.Service.Geometry[target].Ports.Single(p=>p.Id==port.TargetPort).Stand);
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.MainPath)!,"all-exits.json"),StrictJson.Serialize(run.Steps));
        });
        TestRegistry.Add("CX-025","Missing paired entry preserves source actor and sequence",()=>
        {
            var cat=Catalog();var geo=JsonNode.Parse(cat.Q102GeometryJson)!;geo["R1-S03"]!["ports"]=new JsonArray();cat=cat with { Q102GeometryJson=geo.ToJsonString() };
            using var store=new ProfileStore(Root("missing-port"),cat.ReleaseId);var run=new Q102RouteCandidate(store,cat);Done(run.Service.InitializeSliceAsync());Done(run.Walk(run.Service.Geometry["R1-S02"].Ports.Single(p=>p.TargetScene=="R1-S03").Stand));var before=Done(run.Service.ViewAsync());
            TestRegistry.Throws<InvalidOperationException>(()=>Done(run.Service.EnterPortAsync("R1-S02.to.R1-S03")));var after=Done(run.Service.ViewAsync());Require(before.Actor==after.Actor&&before.Snapshot.Sequence==after.Snapshot.Sequence);
        });
        TestRegistry.Add("CX-026","Across-wall pickup rejected; installed unique item survives continue and old branch",()=>
        {
            using var store=new ProfileStore(Root("ownership"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Service.InitializeSliceAsync());
            TestRegistry.Throws<InvalidOperationException>(()=>Done(run.Service.PickBracketAsync()));Done(run.Prepare(WorkshopMode.Isolated));string checkpoint=Done(store.CreateCheckpointAsync("manual"));
            Done(store.ContinueAsync());Require(Done(run.Service.ViewAsync()).Snapshot.Items.Single(i=>i.Id==Q102Service.BracketId).OwnerKind=="installed");Done(store.LoadCheckpointAsync(checkpoint));
            var v=Done(run.Service.ViewAsync());Require(v.Snapshot.Items.Count(i=>i.Id==Q102Service.BracketId)==1&&v.Snapshot.Items.Single(i=>i.Id==Q102Service.BracketId).OwnerKind=="installed"&&v.Workshop.LocalReserveAvailable&&!v.Ferry.MainGeneratorRunning);
        });
        TestRegistry.Add("CX-027","Focused side route avoids bidirectional scan; latch remains open after reactivation",()=>
        {
            using var store=new ProfileStore(Root("focused"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Prepare(WorkshopMode.Focused,true,true));
            Done(run.Exit("R1-S02"));Done(run.Exit("R1-S06"));var v=Done(run.Service.ViewAsync());Require(run.Service.PortIssue(v,run.Service.Geometry[v.Actor.Scene].Ports.Single(p=>p.TargetScene=="R1-S05")) is not null);
            Done(run.Exit("R1-S07"));Done(run.Spot("R1-S07.local_controller"));Done(run.Service.WorkshopModeAsync(WorkshopMode.Active));Done(run.Exit("R1-S05"));Done(run.Exit("R1-S02"));Done(run.Exit("R1-S06"));Done(run.Exit("R1-S02"));
            Require(Done(run.Service.ViewAsync()).Workshop.LatchOpen&&Done(run.Service.ViewAsync()).Ferry.BatteryConnected);
            var front=run.Service.Geometry["R1-S06"].Ports.Single(p=>p.TargetScene=="R1-S05").Stand;Require(!new NavigationGrid(run.Service.Geometry["R1-S06"]).CanStand(front,true));
        });
        TestRegistry.Add("CX-029","Journal exposes only acquired IDs; dictionary does not disclose task solution",()=>
        {
            using var store=new ProfileStore(Root("journal"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Service.InitializeSliceAsync());var v=Done(run.Service.ViewAsync());
            TestRegistry.Throws<InvalidOperationException>(()=>MaterialJournal.Open(Catalog(),v.Snapshot,"Q102-L-REVISION"));Done(run.Read("R1-S02.bulletin","Q102-R-NOTICE"));
            Require(MaterialJournal.Visible(Catalog(),Done(run.Service.ViewAsync()).Snapshot).Length==1);Require(!MaterialJournal.Lookup("route").Contains("route C",StringComparison.OrdinalIgnoreCase));
        });
        TestRegistry.Add("CX-034","One NPC lease spans locations and expires on new load context",()=>
        {
            using var store=new ProfileStore(Root("lease"),Catalog().ReleaseId);Require(Done(store.TryLeaseAsync("N-HAVEL","foreground")));Require(!Done(store.TryLeaseAsync("N-HAVEL","background")));Done(store.ReleaseLeaseAsync("N-HAVEL","other"));Require(!Done(store.TryLeaseAsync("N-HAVEL","second")));Done(store.ContinueAsync());Require(Done(store.TryLeaseAsync("N-HAVEL","new-load")));Done(store.ReleaseLeaseAsync("N-HAVEL","foreground"));Require(!Done(store.TryLeaseAsync("N-HAVEL","third")));
        });
        TestRegistry.Add("CX-033","Local feedback locates original text and never inserts or estimates a band",()=>
        {
            string text="😀 "+string.Join(' ',Enumerable.Repeat("word",45))+".";var view=Mist.Application.Learning.LocalWritingFeedback.Inspect(text);
            Require(view.WordCount==45&&view.Advice.Length==1&&view.Advice[0].StartColumn==0&&view.Advice[0].EndColumn==text.EnumerateRunes().Count());
            Require(Mist.Application.Learning.LocalWritingFeedback.Inspect("Short message.").Advice.Length==0);
        });
        TestRegistry.Add("CX-032","Partial order allows both install orders; unknown capacity and missing steps do not pass",()=>
        {
            using var store=new ProfileStore(Root("puzzle"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Service.InitializeSliceAsync());string before=StrictJson.Serialize(Done(store.SnapshotAsync()));
            Mist.Core.Puzzles.OrderConstraint[] constraints=[new("bracket","test"),new("battery","test"),new("test","depart")];
            Require(Mist.Core.Puzzles.PuzzleWork.CheckOrder(["bracket","battery","test","depart"],constraints).Length==0&&Mist.Core.Puzzles.PuzzleWork.CheckOrder(["battery","bracket","test","depart"],constraints).Length==0);
            Require(Mist.Core.Puzzles.PuzzleWork.CheckOrder(["battery","test","bracket","depart"],constraints).Length>0&&Mist.Core.Puzzles.PuzzleWork.CheckOrder(["battery","test"],constraints).Length>0);
            Require(Mist.Core.Puzzles.PuzzleWork.CheckAllocation(new Dictionary<string,int>{{"unknown",1}},new Dictionary<string,int?>{{"unknown",null}}).Length>0);
            Require(before==StrictJson.Serialize(Done(store.SnapshotAsync())));
        });
        TestRegistry.Add("CX-033","Six learning states require scoped reviewed successes, distinct families and real calendar delay",()=>
        {
            var time=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);var first=new Mist.Core.Learning.ReviewedEvidence("R01","reading","family1","session1","context1",time,true,true,false,"test-controlled-check");
            Require(Mist.Core.Learning.EvidenceStateRules.Project([],"R01","reading")==Mist.Core.Learning.EvidenceState.Insufficient);
            Require(Mist.Core.Learning.EvidenceStateRules.Project([first with{Supported=true}],"R01","reading")==Mist.Core.Learning.EvidenceState.SupportedSuccess);
            Require(Mist.Core.Learning.EvidenceStateRules.Project([first],"R01","reading")==Mist.Core.Learning.EvidenceState.IndependentSuccess);
            var second=first with{Family="family2",Session="session2",Context="context2",SubmittedAt=time.AddDays(1)};var third=second with{Family="family3",SubmittedAt=time.AddDays(2)};
            Require(Mist.Core.Learning.EvidenceStateRules.Project([first,second,third],"R01","reading")==Mist.Core.Learning.EvidenceState.MultiContext);
            Require(Mist.Core.Learning.EvidenceStateRules.Project([first,second,third with{SubmittedAt=time.AddDays(7)}],"R01","reading")==Mist.Core.Learning.EvidenceState.DelayedNewContext);
            Require(Mist.Core.Learning.EvidenceStateRules.Project([first,second with{Success=false},third with{Success=false}],"R01","reading")==Mist.Core.Learning.EvidenceState.ReviewSuggested);
            Require(Mist.Core.Learning.EvidenceStateRules.Project([first,second,third],"R01","listening")==Mist.Core.Learning.EvidenceState.Insufficient);
        });
        TestRegistry.Add("CX-030","An available but unplayed audio cannot be independent listening",()=>
        {
            var evidence=new Mist.Core.Learning.LearningObservation("id","family","attempt",Mist.Core.Learning.InputModality.Listening,true,true,true,false,[],"1","note");
            Require(!Mist.Core.Learning.ObservationRules.Project(evidence,false).IndependentListening);Require(Mist.Core.Learning.ObservationRules.Project(evidence with{AudioPlaybackStarted=true},false).IndependentListening);
        });
        TestRegistry.Add("CX-035","B is not corrected to C; changed draft requires reconfirmation; blank support remains explicit",()=>
        {
            using var store=new ProfileStore(Root("meaning"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Prepare(WorkshopMode.Isolated));
            Done(run.Service.ConfirmMeaningAsync(Q102RouteCandidate.CorrectMeaning with { Route=RouteChoice.B },false));Require(Done(run.Service.PreviewDepartureAsync()).Preview.Status!=PreviewStatus.Ready);
            Done(run.Service.ConfirmMeaningAsync(Q102RouteCandidate.CorrectMeaning,false));Done(run.SaveText("Changed instructions."));Require(Done(run.Service.PreviewDepartureAsync()).Preview.Issues.Any(i=>i.Code=="proposal_changed"));
            Done(run.SaveText(""));TestRegistry.Throws<InvalidOperationException>(()=>Done(run.Service.ConfirmMeaningAsync(Q102RouteCandidate.CorrectMeaning,false)));Done(run.Service.ConfirmMeaningAsync(Q102RouteCandidate.CorrectMeaning,true));Require(!Done(run.Service.PreviewDepartureAsync()).Preview.IndependentWritingEvidence);
        });
        TestRegistry.Add("CX-036","Disconnect/reconnect invalidates certificate and cancelled preview cannot execute",()=>
        {
            using var store=new ProfileStore(Root("test-stale"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Prepare(WorkshopMode.Isolated));var preview=Done(run.Service.PreviewDepartureAsync());run.Service.ClosePreview(preview.Token);TestRegistry.Throws<InvalidOperationException>(()=>Done(run.Service.DepartAsync(preview.Token)));
            Done(run.Spot("R1-S04.reserve_battery"));Done(run.Service.ConnectBatteryAsync(false));Done(run.Service.ConnectBatteryAsync(true));Require(!Done(run.Service.ViewAsync()).Ferry.Ready);Done(run.Spot("R1-S04.writing_desk"));Require(Done(run.Service.PreviewDepartureAsync()).Preview.Status!=PreviewStatus.Ready);
            Require(Done(run.Service.ViewAsync()).Snapshot.Items.Single(i=>i.Id==Q102Service.BracketId).OwnerKind=="installed");
        });
        TestRegistry.Add("CX-037","Departure rollback is complete before commit and receipt resolves uncertain after-commit result",()=>
        {
            string? phase=null;using var store=new ProfileStore(Root("departure-fault"),Catalog().ReleaseId,p=>{if(p==phase)throw new IOException("Injected "+p);});var run=new Q102RouteCandidate(store,Catalog());Done(run.Prepare(WorkshopMode.Isolated));var before=Done(store.SnapshotAsync());phase="after_balances";var preview=Done(run.Service.PreviewDepartureAsync());TestRegistry.Throws<IOException>(()=>Done(run.Service.DepartAsync(preview.Token)));phase=null;Require(StrictJson.Serialize(before)==StrictJson.Serialize(Done(store.SnapshotAsync())));
            phase="after_commit";preview=Done(run.Service.PreviewDepartureAsync());TestRegistry.Throws<IOException>(()=>Done(run.Service.DepartAsync(preview.Token)));phase=null;var after=Done(run.Service.ViewAsync());Require(after.Snapshot.Receipts.Count(r=>r.SemanticKey=="Q-102:resolution")==1&&after.Snapshot.Balances["marks"]==60&&!after.Ferry.Docked);Require(Done(run.Service.PreviewDepartureAsync()).Preview.Status!=PreviewStatus.Ready);
        });
        TestRegistry.Add("CX-037","SQLite draft CAS rejects a draft changed between read preview and transaction",()=>
        {
            using var store=new ProfileStore(Root("draft-stamp"),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Service.InitializeSliceAsync());Done(run.SaveText("old"));var head=Done(store.GetDraftAsync(Q102Service.DraftTask))!;var s=store.Session;Done(run.SaveText("new"));
            var result=Done(store.ExecuteAsync(new CommitPlanCommand(new(s.Profile,s.Branch,s.Load,new(Guid.NewGuid()),s.ReleaseId,[],new(Q102Service.DraftTask,head.Id,head.Revision,head.Hash)),new("stale-draft","departure",[],[],[new("marks",60)],"{}"))));Require(result.Status==CommitStatus.Rejected&&!Done(store.SnapshotAsync()).Balances.ContainsKey("marks"));
        });
        foreach(var method in new[]{WorkshopMode.Isolated,WorkshopMode.Focused})TestRegistry.Add("CX-038","Actual SQLite full noncombat route: "+method,()=>
        {
            using var store=new ProfileStore(Root("q102-"+method),Catalog().ReleaseId);var run=new Q102RouteCandidate(store,Catalog());Done(run.Complete(method,method==WorkshopMode.Focused,method==WorkshopMode.Focused));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.MainPath)!,"playthrough.json"),StrictJson.Serialize(new { route=method,steps=run.Steps,human_validation="not_run",listening_comprehension="not_evaluated" }));
            var snapshot=Done(store.SnapshotAsync());Done(store.ContinueAsync());Require(StrictJson.Serialize(snapshot)==StrictJson.Serialize(Done(store.SnapshotAsync())));
        });
    }
}
