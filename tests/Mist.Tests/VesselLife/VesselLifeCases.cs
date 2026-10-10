using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Roster;
using Mist.Application.VesselInterior;
using Mist.Application.VesselLife;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.VesselLife;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.VesselLife;
public static class VesselLifeCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static VesselLifePackage Package()=>VesselLifePackage.ParseJson(File.ReadAllText(Path.Combine(Root,"content/source/vessel_life/learner_package.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"vessel-life-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    public static ActorState Actor(ProfileStore s)=>StrictJson.Deserialize<ActorState>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id=="actor").Json);
    public static void At(ProfileStore s,VesselInteriorService interior,string spot)
    {
        string target=spot.Split('.')[0];string[] rooms=["HUB-S01","HUB-S02","HUB-S03"];
        while(Actor(s).Scene!=target){string current=Actor(s).Scene;int i=Array.IndexOf(rooms,current),j=Array.IndexOf(rooms,target);var p=interior.Geometry[current].Ports.Single(p=>p.TargetScene==rooms[i+Math.Sign(j-i)]);Walk(p.Stand);Success(Done(interior.EnterPortAsync(p.Id)));}
        Walk(interior.Geometry[target].Spots.Single(x=>x.Id==spot).Stand);
        void Walk(Point2 point){var a=Actor(s);var path=new NavigationGrid(interior.Geometry[a.Scene]).FindPath(a.Position,point,true);Require(path.Length>0);Success(Done(interior.MoveAsync(path)));}
    }
    public static (VesselLifeService Service,VesselInteriorService Interior) NormalBoarded(ProfileStore s,bool repairedFrame=false)
    {
        var (chapter,interior)=ChapterThree.ChapterThreeCases.NormalComplete(s);var r=new ChapterThree.ChapterThreeCases.Run(s,chapter);if(repairedFrame)r.Auxiliary();var roster=new RosterService(s,Catalog);foreach(string actor in new[]{"CP-LINA","CP-OREN","CP-MIRA"}){Success(Done(roster.InviteAsync(actor)));Success(Done(roster.DecideAsync(actor,true)));}
        r.Goto("R3-S01");r.Spot("R3-S01.berth");var p=Done(interior.PreviewBoardAsync());Require(p.Ready,StrictJson.Serialize(p));Success(Done(interior.ConfirmPreviewAsync(p.Token)));return(new(s,interior,Package()),interior);
    }
    private static void Complete(VesselLifeService b,string id){for(int i=0;i<30;i++){var d=Done(b.CurrentAsync(id));Success(Done(b.AdvanceAsync(id)));if(d.State.NextLine+1>=d.Script.Lines.Length)return;}throw new InvalidOperationException("Conversation did not complete within its full text.");}
    public static void Register()
    {
        Add("CP-NPC-LIFE","Twenty-four original ship life scenes retain full text and one original chair script",()=>{var p=Package();Require(p.Scripts.Length==24&&p.Scripts.Sum(s=>s.Lines.Sum(l=>l.English.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length))>2400&&p.Scripts.Count(s=>s.SourceOrigin=="existing_original")==1);string original=File.ReadAllText(Path.Combine(Root,"reference/part09/materials/NPC-MAT-06_Chair_Banter.md"));foreach(var line in p.Scripts.Single(s=>s.Id=="BAN-LINA-OREN-1").Lines)Require(original.Contains(line.English,StringComparison.Ordinal));});
        Add("CP-NPC-LIFE","Normal zero-XP joined crew walks in the actual ship, pauses and resumes a full once-only event without rewards",()=>
        {
            using var s=Store("normal");var (b,v)=NormalBoarded(s);var before=Done(s.SnapshotAsync());At(s,v,"HUB-S01.harbor_view");Success(Done(b.InviteAsync("BAN-LINA-OREN-1")));Success(Done(b.StartAsync("BAN-LINA-OREN-1")));Success(Done(b.AdvanceAsync("BAN-LINA-OREN-1")));Success(Done(b.PauseAsync("BAN-LINA-OREN-1")));Done(s.ContinueAsync());Success(Done(b.StartAsync("BAN-LINA-OREN-1")));Require(Done(b.CurrentAsync("BAN-LINA-OREN-1")).State.NextLine==1);Complete(b,"BAN-LINA-OREN-1");var text=Done(b.ReplayAsync("BAN-LINA-OREN-1"));Require(text.Lines.Length==8);long seq=s.Session.Sequence;Done(b.ReplayAsync(text.Id));Require(s.Session.Sequence==seq);Throws<InvalidOperationException>(()=>Done(b.StartAsync(text.Id)));Success(Done(b.InviteAsync("BAN-LINA-1")));Throws<InvalidOperationException>(()=>Done(b.StartAsync("BAN-LINA-1")));var after=Done(s.SnapshotAsync());Require(before.Balances.OrderBy(x=>x.Key).SequenceEqual(after.Balances.OrderBy(x=>x.Key))&&after.Receipts.Count(r=>r.SemanticKey=="banter:BAN-LINA-OREN-1:vessel-life-1:completed")==1&&after.Receipts.All(r=>r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
        });
        Add("CP-NPC-LIFE","Actual archive scene requires safe participant walks and a held NPC lease blocks the whole event",()=>
        {
            using var s=Store("walk");var (b,v)=NormalBoarded(s);At(s,v,"HUB-S02.archive");Require(Done(s.TryLeaseAsync("CP-MIRA","other-current-work")));Throws<InvalidOperationException>(()=>Done(b.InviteAsync("BAN-MIRA-1")));Done(s.ReleaseLeaseAsync("CP-MIRA","other-current-work"));Success(Done(b.InviteAsync("BAN-MIRA-1")));var presence=StrictJson.Deserialize<BanterPresence>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id=="banter-presence:CP-MIRA").Json);Require(presence.Scene=="HUB-S02"&&presence.ActualWalk.Any(w=>w.FromScene=="HUB-S01"&&w.ToScene=="HUB-S02"&&w.Path.Length>0));Success(Done(b.StartAsync("BAN-MIRA-1")));Complete(b,"BAN-MIRA-1");
        });
        Add("CP-NPC-LIFE","Joined does not authorize private replies or fabricate an earned old toolframe",()=>
        {using var s=Store("scope");var (b,v)=NormalBoarded(s);At(s,v,"HUB-S01.harbor_view");long seq=s.Session.Sequence;foreach(string id in new[]{"BAN-LINA-2","BAN-LINA-MIRA-2"})Throws<InvalidOperationException>(()=>Done(b.InviteAsync(id)));Require(s.Session.Sequence==seq);At(s,v,"HUB-S03.workbench");Throws<InvalidOperationException>(()=>Done(b.InviteAsync("BAN-OREN-1")));var list=Done(b.ListAsync());Require(list.Single(x=>x.Id=="BAN-LINA-2").Issue is not null&&list.Single(x=>x.Id=="BAN-OREN-1").Issue is not null);});
        Add("CP-NPC-LIFE","Normal native-entry profile has actual recruited crew and no opened life scene",()=>
        {using var s=Store("native-entry");var (b,v)=NormalBoarded(s);At(s,v,"HUB-S01.harbor_view");var snapshot=Done(s.SnapshotAsync());var availability=Done(b.ListAsync());var expected=b.Package.Scripts.Where(d=>d.Scope=="public"&&d.ProgressGate==""&&d.Actors.All(a=>a is "CP-LINA" or "CP-OREN" or "CP-MIRA")).Select(d=>d.Id).Order();Require(snapshot.Balances["experience"]==3840,"Actual Q304 boarding entry must retain 3840 XP.");Require(!snapshot.Entities.Any(e=>e.Kind=="banter_state"),"Native entry cannot seed an opened life scene.");Require(availability.Where(x=>x.Issue is null).Select(x=>x.Id).Order().SequenceEqual(expected),"Only current joined participants and actual public ungated sources may be available; Tess, private replies and unrepaired toolframe remain blocked.");File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-vessel-life-native-entry.json"),StrictJson.Serialize(new{scope="actual zero-XP through Q304, actual invitations and actual boarding; no life event seeded",snapshot.Sequence,experience=3840,human="pending"}));});
    }
}
