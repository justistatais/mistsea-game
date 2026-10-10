using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Q102;
using Mist.Application.Inventory;
using Mist.Application.CombatSessions;
using Mist.Core.Combat;
using Mist.Core.Content;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Infrastructure.Storage;

namespace Mist.DebugCandidates;

// Internal regression driver. Every movement follows the same navigation and commands as the client.
// This does not stand in for player usability, IME, listening or language calibration.
public sealed class Q102RouteCandidate(ProfileStore store, RuntimeCatalog catalog, Action<string>? trace = null)
{
    public Q102Service Service { get; } = new(store, catalog);
    public List<object> Steps { get; } = new();
    private async ValueTask Record(string action)
    {
        var v = await Service.ViewAsync();
        Steps.Add(new { action, v.Actor, workshop=v.Workshop.Mode, v.Workshop.LatchOpen, v.Ferry, quest=v.Quest.Lifecycle, sequence=v.Snapshot.Sequence });
        trace?.Invoke(action);
    }
    public async ValueTask Walk(Point2 destination)
    {
        var v=await Service.ViewAsync();
        var path=new NavigationGrid(Service.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,destination,v.Actor.Scene=="R1-S06"&&!v.Workshop.FrontSafe);
        if(path.Length==0)throw new InvalidOperationException($"No legal path {v.Actor.Scene}: {v.Actor.Position} -> {destination}");
        await Service.TraverseAsync(path); await Record("walk:"+destination);
    }
    public async ValueTask Spot(string id)
    {
        var v=await Service.ViewAsync(); await Walk(Service.Geometry[v.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);
    }
    public async ValueTask Exit(string target)
    {
        var v=await Service.ViewAsync(); var port=Service.Geometry[v.Actor.Scene].Ports.Single(p=>p.TargetScene==target);
        await Walk(port.Stand); var result=await Service.EnterPortAsync(port.Id);
        if(result.Status!=CommitStatus.Committed)throw new InvalidOperationException("Transition failed: "+result.Status);
        await Record("exit:"+port.Id);
    }
    public async ValueTask Read(string spot, string material)
    {
        await Spot(spot); var result=await Service.DiscoverAsync(material,spot);
        if(result.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))throw new InvalidOperationException("Material discovery rejected.");
        await Record("read:"+material);
    }
    public static PlanMeaning CorrectMeaning=>new(RouteChoice.C,"closed","draught_exceeds_limit","replace_bracket","charged_backup_battery","keep_off_until_isolation_confirmed",true);
    public async ValueTask SaveText(string text)
    {
        var head=await store.GetDraftAsync(Q102Service.DraftTask); var s=store.Session;
        await store.SaveAsync(new(s.Profile,s.Branch,s.Load,Q102Service.DraftTask,head?.Id,head?.Revision??0,text,"Q102-W-PLAN@0.1"));
        await Record("save:real_draft");
    }
    public async ValueTask Prepare(WorkshopMode method, bool earlyPickup=false, bool batteryFirst=false)
    {
        if(method is not(WorkshopMode.Isolated or WorkshopMode.Focused or WorkshopMode.Disabled))throw new ArgumentException("Use a registered internal method.");
        bool actualCommission=(await store.SnapshotAsync()).Receipts.Any(r=>r.SemanticKey=="Q-101:resolution");await Service.InitializeSliceAsync();await Record(actualCommission?"actual_commission_continued":"internal_slice_start_not_prologue");
        await new PartyService(store,catalog).InitializeStarterCollectionAsync();
        if(!earlyPickup){await Spot("R1-S02.quay_contact");await Service.AcceptAsync();await Record("accept");}
        await Read("R1-S02.bulletin","Q102-R-NOTICE");
        await Exit("R1-S05"); await Exit("R1-S07");
        await Read("R1-S07.main_generator_tag","PZ102-R-PANEL");
        await Read("R1-S07.maintenance_note","PZ102-R-WORKSHOP");
        await Spot("R1-S07.local_controller");
        if(method==WorkshopMode.Disabled)
        {
            await Exit("R1-S05");await Walk(Service.Geometry["R1-S05"].Ports.Single(p=>p.TargetScene=="R1-S06").Stand);
            var encounter=new EncounterService(store,catalog,id=>store.CreateCheckpointAsync("protected",protectedId:id));var started=await encounter.BeginWorkshopAsync();if(started.Status!=CommitStatus.Committed)throw new InvalidOperationException("Encounter entry rejected.");
            for(int count=0;count<600;count++)
            {
                var actual=await encounter.ViewAsync()??throw new InvalidOperationException("Missing actual encounter.");if(actual.Battle.Outcome!=CombatOutcome.Active)break;
                var request=BattleCandidate.Choose(new(new BattleDefinitions(catalog),actual.Battle));var preview=await encounter.PreviewAsync(request);if(!preview.Action.Legal)throw new InvalidOperationException(preview.Description);var committed=await encounter.ConfirmAsync(preview.Token);if(committed.Status!=CommitStatus.Committed)throw new InvalidOperationException("Combat action not committed.");
                Steps.Add(new{action="actual_combat_command",request,state=(await encounter.ViewAsync())!.Battle});
            }
            if((await encounter.ViewAsync())!.Battle.Outcome!=CombatOutcome.Controlled)throw new InvalidOperationException("Workshop not controlled.");await Record("combat:unit_disabled_only");
        }
        else{await Service.WorkshopModeAsync(method); await Record("controller:"+method);}
        await Exit("R1-S06"); await Spot("R1-S06.spare_bracket"); await Service.PickBracketAsync(); await Record("pick:one_bracket");
        await Spot("R1-S06.shortcut_latch"); await Service.OpenLatchAsync(); await Record("latch:open"); await Exit("R1-S02");
        await Exit("R1-S03"); await Read("R1-S03.dispatch_audio","Q102-L-REVISION"); await Read("R1-S03.route_b_rule","Q102-R-B");
        if(earlyPickup){await Spot("R1-S03.havel");await Service.AcceptAsync();await Record("accept:after_lawful_pickup");}
        await Exit("R1-S02"); await Exit("R1-S04"); await Read("R1-S04.vessel_spec","Q102-R-VESSEL"); await Read("R1-S04.reserve_battery","Q102-R-BATTERY");
        if(batteryFirst){await Service.ConnectBatteryAsync(true);await Record("battery:connected_first");}
        await Spot("R1-S04.beacon");await Service.InstallBracketAsync();await Record("bracket:installed");
        if(!batteryFirst){await Spot("R1-S04.reserve_battery");await Service.ConnectBatteryAsync(true);await Record("battery:connected");}
        await Spot("R1-S04.beacon");await Service.TestBeaconAsync();await Record("beacon:tested_current_configuration");
        await Spot("R1-S04.writing_desk");
        await SaveText("Please use route C once the beacon has been tested. Route A is closed, and the ferry's 1.4-metre draught exceeds the limit for B even if its pump runs. Fit the replacement bracket and connect the ferry's charged backup battery. Keep the main generator off until cable isolation has been confirmed. The workshop reserve is a separate supply.");
        await Service.ConfirmMeaningAsync(CorrectMeaning,false);await Record("explicit_meaning:confirmed_no_language_score");
    }
    public async ValueTask Complete(WorkshopMode method,bool earlyPickup=false,bool batteryFirst=false)
    {
        await Prepare(method,earlyPickup,batteryFirst);
        var preview=await Service.PreviewDepartureAsync();
        if(preview.Preview.Status!=PreviewStatus.Ready)throw new InvalidOperationException(string.Join(";",preview.Preview.Issues.Select(i=>i.SafeMessage)));
        var result=await Service.DepartAsync(preview.Token);
        if(result.Status!=CommitStatus.Committed)throw new InvalidOperationException("Departure failed: "+result.Status);
        await Record("departure:atomic");
        var v=await Service.ViewAsync();
        if(v.Quest.Lifecycle!=QuestLifecycle.Closed||v.Actor.Scene!="R1-S02"||v.Ferry.Docked||v.Snapshot.Balances["experience"]!=500||v.Snapshot.Balances["marks"]!=60||v.Snapshot.Balances["parts"]!=2)throw new InvalidOperationException("Invalid final world.");
        if(v.Snapshot.Items.Count(i=>i.Id==Q102Service.BracketId)!=1||v.Snapshot.Items.Count(i=>i.Id==Q102Service.BatteryId)!=1||v.Snapshot.Items.Where(i=>i.Id is Q102Service.BracketId or Q102Service.BatteryId).Any(i=>i.OwnerKind!="vessel"||i.OwnerId!="CIVIL-FERRY"))throw new InvalidOperationException("Device ownership did not travel with ferry.");
        if(v.Snapshot.Receipts.Count(r=>r.SemanticKey=="Q-102:resolution")!=1||v.Entity("campaign").Json.Contains("true"))throw new InvalidOperationException("Unexpected reward or campaign advance.");
    }
}
