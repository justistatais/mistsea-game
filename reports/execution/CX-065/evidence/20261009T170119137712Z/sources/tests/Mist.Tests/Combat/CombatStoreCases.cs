using System.Collections.Immutable;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Combat;

public static class CombatStoreCases
{
    private static string Root(string name){string p=Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),name+"-"+Guid.NewGuid());Directory.CreateDirectory(p);return p;}
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static void Fixture(ProfileStore store,string semantic,ImmutableArray<BalanceChange> balances)
    {
        var s=store.Session;Success(Done(store.ExecuteAsync(new CommitPlanCommand(new(s.Profile,s.Branch,s.Load,new(Guid.NewGuid()),s.ReleaseId,[]),new(semantic,"explicit_engineering_fixture",[],[],balances,"{\"scope\":\"test fixture, no player or human evidence\"}")))));
    }
    private static (Q102RouteCandidate Route,PartyService Party,EncounterService Encounter) Prepare(ProfileStore store)
    {
        var cat=Q102.Q102Cases.Catalog();var route=new Q102RouteCandidate(store,cat);Done(route.Service.InitializeSliceAsync());var party=new PartyService(store,cat);Done(party.InitializeStarterCollectionAsync());
        var d=new BattleDefinitions(cat);Success(Done(party.ApplyBuildAsync(d.ReferenceBuild("BUILD-Q102-ENTRY-M"))));Success(Done(party.EquipAsync("collection:WP-05","weapon")));Done(route.Exit("R1-S05"));var port=route.Service.Geometry["R1-S05"].Ports.Single(p=>p.TargetScene=="R1-S06");Done(route.Walk(port.Stand));
        return(route,party,new EncounterService(store,cat,id=>store.CreateCheckpointAsync("protected",protectedId:id)));
    }
    private static CommandResult Act(EncounterService encounter,BattleRequest request)
    {
        var preview=Done(encounter.PreviewAsync(request));Require(preview.Action.Legal,preview.Description);var result=Done(encounter.ConfirmAsync(preview.Token));Success(result);return result;
    }
    public static void Register()
    {
        Add("CX-042","Schema-2 migration preserves original SHA and interrupted candidate cannot promote",()=>
        {
            bool fail=false;var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("inventory-migration"),cat.ReleaseId,p=>{if(fail&&p=="inventory_migration_before_pointer")throw new IOException("migration fault");});Done(new Q102Service(store,cat).InitializeSliceAsync());string original=store.MainPath;string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(original)));var backup=Done(store.BackupAsync());
            fail=true;Throws<IOException>(()=>Done(store.EnsureInventorySchemaAsync()));Require(store.MainPath==original&&hash==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(original)))&&Done(store.SnapshotAsync()).SchemaVersion==1);
            fail=false;Done(store.EnsureInventorySchemaAsync());Require(store.MainPath!=original&&hash==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(original)))&&Done(store.SnapshotAsync()).SchemaVersion==2);
            var party=new PartyService(store,cat);Done(party.InitializeStarterCollectionAsync());var v2=Done(store.BackupAsync());string slot=Done(store.CreateCheckpointAsync("manual"));Done(store.LoadCheckpointAsync(slot));Require(Done(store.SnapshotAsync()).Items.Count(i=>i.OwnerKind=="equipped")==2);Done(store.RestoreBackupAsync(v2.Id));Require(Done(store.SnapshotAsync()).SchemaVersion==2);Done(store.RestoreBackupAsync(backup.Id));Require(Done(store.SnapshotAsync()).SchemaVersion==1);Done(party.InitializeStarterCollectionAsync());Require(Done(store.SnapshotAsync()).SchemaVersion==2);
        });
        Add("CX-042","A second live store cannot write the preserved old database after pointer promotion",()=>
        {
            var cat=Q102.Q102Cases.Catalog();string root=Root("inventory-multiple-stores");using var first=new ProfileStore(root,cat.ReleaseId);Done(new Q102Service(first,cat).InitializeSliceAsync());using var second=new ProfileStore(root,cat.ReleaseId);string old=first.MainPath;Done(first.EnsureInventorySchemaAsync());string originalHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(old)));
            Throws<StoreConflictException>(()=>Done(second.SetSettingAsync("test_stale_writer","{}")));Throws<StoreConflictException>(()=>Done(second.SnapshotAsync()));Require(originalHash==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(old))));using var reopened=new ProfileStore(root,cat.ReleaseId);Require(Done(reopened.SnapshotAsync()).SchemaVersion==2);
        });
        Add("CX-042","SQLite module transfer is free, upgrade consumes original atomically, replay cannot double-pay",()=>
        {
            var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("modules"),cat.ReleaseId);var q=new Q102Service(store,cat);Done(q.InitializeSliceAsync());var party=new PartyService(store,cat);Done(party.InitializeStarterCollectionAsync());Require(Done(store.SnapshotAsync()).Items.Count(i=>i.Id.StartsWith("collection:"))==4);
            Fixture(store,"Q-104:resolution",[new("marks",1000),new("parts",20)]);Fixture(store,"Q-304:resolution",[]);
            var request=new CommandId(Guid.NewGuid());Success(Done(party.CraftModuleAsync("MOD-D1",null,request)));var before=Done(store.SnapshotAsync());Require(Done(party.CraftModuleAsync("MOD-D1",null,request)).Status==CommitStatus.AlreadyCommitted&&before.Sequence==Done(store.SnapshotAsync()).Sequence);
            Throws<InvalidOperationException>(()=>Done(party.CraftModuleAsync("MOD-L1",null,request)));string id="module:"+request.Value;Success(Done(party.InstallModuleAsync(id,"collection:WP-01")));Success(Done(party.InstallModuleAsync(id,"collection:WP-05")));Require(Done(store.SnapshotAsync()).Balances["marks"]==920);
            var upgrade=new CommandId(Guid.NewGuid());Success(Done(party.CraftModuleAsync("MOD-D2",id,upgrade)));var after=Done(store.SnapshotAsync());Require(after.Items.Single(i=>i.Id==id).OwnerKind=="consumed"&&after.Items.Single(i=>i.Id=="module:"+upgrade.Value).OwnerKind=="player"&&after.Balances["marks"]==760&&after.Balances["parts"]==15);
            Require(Done(party.CraftModuleAsync("MOD-D2",id,upgrade)).Status==CommitStatus.AlreadyCommitted);Throws<InvalidOperationException>(()=>Done(party.SellAsync("collection:WP-01")));Throws<InvalidOperationException>(()=>Done(party.SellAsync(Q102Service.BracketId)));
            Throws<InvalidOperationException>(()=>Done(party.CraftModuleAsync("MOD-D2",id,new(Guid.NewGuid()))));Require(Done(store.SnapshotAsync()).Sequence==after.Sequence);
        });
        Add("CX-042","Insufficient module resources leave ownership and balances unchanged",()=>
        {
            var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("module-funds"),cat.ReleaseId);Done(new Q102Service(store,cat).InitializeSliceAsync());var party=new PartyService(store,cat);Done(party.InitializeStarterCollectionAsync());Fixture(store,"Q-104:resolution",[]);var before=StrictJson.Serialize(Done(store.SnapshotAsync()));
            Require(Done(party.CraftModuleAsync("MOD-D1",null,new(Guid.NewGuid()))).Status==CommitStatus.Rejected);Require(before==StrictJson.Serialize(Done(store.SnapshotAsync())));
        });
        Add("CX-050","Stable SQLite combat action continues exactly; cancelled and old-load previews cannot act",()=>
        {
            var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("combat-continue"),cat.ReleaseId);var run=Prepare(store);Success(Done(run.Encounter.BeginWorkshopAsync()));Require(Done(store.SlotsAsync()).Keys.Any(k=>k.StartsWith("protected:encounter:")));
            var state=Done(run.Encounter.ViewAsync())!;var e=new BattleEngine(new(cat),state.Battle);var request=BattleCandidate.Choose(e);var preview=Done(run.Encounter.PreviewAsync(request));var before=StrictJson.Serialize(state);run.Encounter.ClosePreview(preview.Token);Require(Done(run.Encounter.ConfirmAsync(preview.Token)).Status==CommitStatus.Rejected&&before==StrictJson.Serialize(Done(run.Encounter.ViewAsync())));
            preview=Done(run.Encounter.PreviewAsync(request));Success(Done(run.Encounter.ConfirmAsync(preview.Token)));var after=StrictJson.Serialize(Done(run.Encounter.ViewAsync()));Require(Done(run.Encounter.ConfirmAsync(preview.Token)).Status==CommitStatus.AlreadyCommitted&&after==StrictJson.Serialize(Done(run.Encounter.ViewAsync())));
            var stale=Done(run.Encounter.PreviewAsync(BattleCandidate.Choose(new(new(cat),Done(run.Encounter.ViewAsync())!.Battle))));Done(store.ContinueAsync());Require(after==StrictJson.Serialize(Done(run.Encounter.ViewAsync()))&&Done(run.Encounter.ConfirmAsync(stale.Token)).Status==CommitStatus.Rejected);
            Throws<InvalidOperationException>(()=>Done(run.Route.Service.OpenLatchAsync()));Throws<InvalidOperationException>(()=>Done(run.Party.EquipAsync("collection:WP-01","weapon")));
        });
        Add("CX-050","Real personal supply is consumed once; retry restores entrance and preserves latest draft",()=>
        {
            var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("combat-retry"),cat.ReleaseId);var route=new Q102RouteCandidate(store,cat);Done(route.Service.InitializeSliceAsync());var party=new PartyService(store,cat);Done(party.InitializeStarterCollectionAsync());Fixture(store,"fixture:supply-funds",[new("marks",100)]);
            var buy=new CommandId(Guid.NewGuid());Success(Done(party.BuyConsumableAsync("CON-04",buy)));string item="supply:"+buy.Value;var run=Prepare(store);Success(Done(run.Encounter.BeginWorkshopAsync(carriedSupplies:[item])));var state=Done(run.Encounter.ViewAsync())!;
            Act(run.Encounter,new(new(Guid.NewGuid()),"SUPPLY",state.Battle.CurrentActor!,["hero"],Device:item));Require(Done(store.SnapshotAsync()).Items.Single(i=>i.Id==item).OwnerKind=="consumed");
            var session=store.Session;Done(store.SaveAsync(new(session.Profile,session.Branch,session.Load,Q102Service.DraftTask,null,0,"草稿😀保留在重试之后","v0.1")));Success(Done(run.Encounter.RetryAsync("relaxed")));
            var retry=Done(run.Encounter.ViewAsync())!;Require(retry.Battle.Instance==state.Battle.Instance&&retry.Battle.Attempt!=state.Battle.Attempt&&retry.Battle.Round==1&&retry.Battle.Aid==2&&retry.Battle.Supplies.All(i=>!i.Used)&&retry.Battle.Difficulty=="relaxed");Require(Done(store.SnapshotAsync()).Items.Single(i=>i.Id==item).OwnerKind=="player"&&Done(store.GetDraftAsync(Q102Service.DraftTask))!.Body=="草稿😀保留在重试之后"&&Done(run.Route.Service.ViewAsync()).Workshop.Mode==WorkshopMode.Active);
        });
        Add("CX-048","Actual workshop victory atomically disables only unit; bracket remains at physical shelf",()=>
        {
            var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("combat-victory"),cat.ReleaseId);var run=Prepare(store);var before=Done(store.SnapshotAsync());Success(Done(run.Encounter.BeginWorkshopAsync()));
            for(int count=0;count<600;count++) { var state=Done(run.Encounter.ViewAsync())!;if(state.Battle.Outcome!=CombatOutcome.Active)break;Act(run.Encounter,BattleCandidate.Choose(new(new(cat),state.Battle))); }
            var final=Done(run.Encounter.ViewAsync())!;var world=Done(run.Route.Service.ViewAsync());Require(final.Battle.Outcome==CombatOutcome.Controlled&&final.ResultApplied&&world.Workshop.Mode==WorkshopMode.Disabled&&!world.Workshop.LatchOpen&&world.Snapshot.Balances.OrderBy(x=>x.Key).SequenceEqual(before.Balances.OrderBy(x=>x.Key)));
            Require(world.Snapshot.Items.Single(i=>i.Id==Q102Service.BracketId).OwnerKind=="warehouse"&&!world.Ferry.BracketInstalled);Throws<InvalidOperationException>(()=>Done(run.Encounter.RetryAsync()));
            Done(run.Route.Exit("R1-S06"));Done(run.Route.Spot("R1-S06.spare_bracket"));Success(Done(run.Route.Service.PickBracketAsync()));Require(Done(store.SnapshotAsync()).Items.Count(i=>i.Id==Q102Service.BracketId)==1&&Done(store.SnapshotAsync()).Items.Single(i=>i.Id==Q102Service.BracketId).OwnerKind=="player");
            File.WriteAllText(Path.Combine(store.RootPath,"actual-victory.json"),StrictJson.Serialize(new{final,world=Done(run.Route.Service.ViewAsync())}));
        });
        Add("CX-050","Injected combat commit failure rolls back complete action, then exact preview retries",()=>
        {
            bool fail=false;var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("combat-fault"),cat.ReleaseId,point=>{if(fail&&point=="after_entities")throw new IOException("combat transaction fault");});var run=Prepare(store);Success(Done(run.Encounter.BeginWorkshopAsync()));var before=StrictJson.Serialize(Done(store.SnapshotAsync()));var state=Done(run.Encounter.ViewAsync())!;var preview=Done(run.Encounter.PreviewAsync(BattleCandidate.Choose(new(new(cat),state.Battle))));
            fail=true;Throws<IOException>(()=>Done(run.Encounter.ConfirmAsync(preview.Token)));Require(before==StrictJson.Serialize(Done(store.SnapshotAsync())));fail=false;Success(Done(run.Encounter.ConfirmAsync(preview.Token)));
        });
        Add("CX-053","Three full actual SQLite methods share the same reward and remain in port",()=>
        {
            foreach(var method in new[]{WorkshopMode.Disabled,WorkshopMode.Isolated,WorkshopMode.Focused})
            {
                var cat=Q102.Q102Cases.Catalog();using var store=new ProfileStore(Root("q102-three-"+method),cat.ReleaseId);var run=new Q102RouteCandidate(store,cat);Done(run.Complete(method));var before=StrictJson.Serialize(Done(store.SnapshotAsync()));Done(store.ContinueAsync());Require(before==StrictJson.Serialize(Done(store.SnapshotAsync())));Require(Done(run.Service.ViewAsync()).Actor.Scene=="R1-S02");File.WriteAllText(Path.Combine(store.RootPath,"three-method-path.json"),StrictJson.Serialize(run.Steps));
            }
        });
    }
}
