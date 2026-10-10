using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Finale;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Application.VesselInterior;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Finale;
using Mist.Core.Progression;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;
using Mist.Core.Skills;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Finale;

// All positive cases begin at zero XP and use the preceding actual chapter and voyage
// producers. There is no prior-progress fixture, naked actor transfer, probe damage or refill.
public static class FinaleCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static FinalePackage Package()=>FinalePackage.ParseJson(File.ReadAllText(Path.Combine(Root,"content/source/finale/learner_package.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"finale-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult result)=>Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(result));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    public static (FinaleService Service,VesselInteriorService Interior,ChapterSix.ChapterSixCases.CheckpointArchive Archive) ActualArrival(ProfileStore store,bool enhanced=false)
    {var prior=ChapterSix.ChapterSixCases.NormalComplete(store,enhanced);var service=new FinaleService(store,Catalog,Package(),prior.Archive);Done(service.InitializeRegionAsync());return(service,prior.Interior,prior.Archive);}
    public static (FinaleService Service,VesselInteriorService Interior,ChapterSix.ChapterSixCases.CheckpointArchive Archive) NormalComplete(ProfileStore store,EndingChoice choice=EndingChoice.Central,string style="Mixed",bool maintenance=false)
    {var pair=ActualArrival(store,choice==EndingChoice.Distributed);var r=new Run(store,pair.Service,style);r.Node1(maintenance);r.Node2();r.Node3();r.Node4(choice);return pair;}
    public static BuildState Build(string style,int level=28)
    {
        string tree=style switch{"H"=>"H","M"=>"M","S"=>"S",_=>"M"};var learned=Enumerable.Range(1,12).Select(n=>tree+n.ToString("00")).ToImmutableArray();string[] active;int[] weights;
        switch(style){case "H":active=["H01","H02","H07","H10"];weights=[4,4,1,1,1];break;case "M":active=["M02","M04","M07","M10"];weights=[3,1,2,3,4];break;case "S":active=["S01","S02","S04","S10"];weights=[4,1,1,3,4];break;default:learned=learned.AddRange(ImmutableArray.Create("H01","H02","S01"));active=["M04","M07","M10","S01"];weights=[3,1,1,3,4];break;}
        var build=new BuildState(ProgressionRules.AllocateTemplate(level,weights),learned,active.ToImmutableArray());TalentRules.Validate(level,build,TalentRules.Read(Catalog));return build;
    }
    public sealed class Run(ProfileStore store,FinaleService service,string style="Mixed")
    {
        public FinaleService Service=>service;public FinaleView View=>Done(service.ViewAsync());public readonly List<object> BattleRecords=[];
        public void Walk(Point2 to){var v=View;var path=new NavigationGrid(v.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,to,true);Require(path.Length>0,"no actual safe RF path: "+v.Actor.Scene);Done(service.TraverseAsync(path));}
        public void Spot(string id)=>Walk(View.Geometry[View.Actor.Scene].Spots.Single(x=>x.Id==id).Stand);
        public void Exit(string to){var p=View.Geometry[View.Actor.Scene].Ports.Single(x=>x.TargetScene==to);Walk(p.Stand);Success(Done(service.EnterPortAsync(p.Id)));}
        public void Goto(string target)
        {
            var v=View;var seen=new Dictionary<string,string?>{{v.Actor.Scene,null}};var queue=new Queue<string>();queue.Enqueue(v.Actor.Scene);
            while(queue.TryDequeue(out var scene)){if(scene==target)break;foreach(var p in v.Geometry[scene].Ports){var projected=v.Snapshot with{Entities=v.Snapshot.Entities.Select(e=>e.Id=="actor"?e with{Json=StrictJson.Serialize(v.Actor with{Scene=scene})}:e).ToImmutableArray()};if(service.PortIssue(projected,p) is null&&seen.TryAdd(p.TargetScene,scene))queue.Enqueue(p.TargetScene);}}
            Require(seen.ContainsKey(target),"no current paired RF route to "+target);var path=new List<string>();for(string? scene=target;seen[scene!] is{} from;scene=from)path.Add(scene!);path.Reverse();foreach(string scene in path)Exit(scene);
        }
        public void At(string spot){Goto(spot.Split('.')[0]);Spot(spot);}
        public void Material(string id){var material=service.Package.Materials.Single(x=>x.Id==id);At(material.Spot);Success(Done(service.DiscoverAsync(id)));Require(Done(service.OpenMaterialAsync(id)).English==material.English);}
        public void Sources(string quest){foreach(var m in service.Package.Materials.Where(m=>m.Quest==quest))Material(m.Id);}
        public void Act(string action){At(service.Package.Stations.Single(x=>x.Action==action).Spot);Success(Done(service.ExecuteStationAsync(action)));}
        public void Meaning(string quest,params (string Key,string Value)[] fields)
        {At(FinaleService.SubmissionSpot(quest));var task=service.Package.WritingTasks.Single(t=>t.Quest==quest);var old=Done(store.GetDraftAsync(task.Id));Done(service.SaveDraftAsync(task.Id,old?.Revision??0,old?.Id,"Engineering candidate prose. The separately confirmed structured meaning is controller input, not automatic English interpretation or a language score."));Success(Done(service.ConfirmMeaningAsync(quest,fields.ToImmutableDictionary(x=>x.Key,x=>x.Value),true)));}
        public FinaleSubmissionPreview Submit(string quest){At(FinaleService.SubmissionSpot(quest));long before=store.Session.Sequence;var p=Done(service.PreviewSubmissionAsync(quest));Require(p.Ready,StrictJson.Serialize(p));Require(store.Session.Sequence==before,"preview altered actual world");Success(Done(service.ConfirmPreviewAsync(p.Token)));Success(Done(service.ConfirmPreviewAsync(p.Token)));Require(View.Snapshot.Receipts.Count(r=>r.SemanticKey==quest+":resolution")==1);return p;}
        public void ConfigureBuild()
        {
            At(View.State.RookCustody&&View.Quests.Single(q=>q.Id=="Q-F02").Lifecycle==QuestLifecycle.Closed?"RF-S04.safe":"RF-S01.writer");var party=new PartyService(store,Catalog);var actual=party.Inspect(View.Snapshot);Require(actual.Level==28&&actual.Grade==4,"actual final preparation must supply level 28 and fourth calibration");Success(Done(party.ApplyBuildAsync(Build(style,actual.Level))));
            var snapshot=Done(store.SnapshotAsync());string definition=style=="H"?"WP-01":"WP-05";var weapon=snapshot.Items.Single(x=>x.Id=="collection:"+definition);if(weapon.OwnerKind!="equipped"||weapon.Slot!="weapon")Success(Done(party.EquipAsync(weapon.Id,"weapon")));
            var armor=Done(store.SnapshotAsync()).Items.FirstOrDefault(x=>x.DefinitionId=="AR-02"&&x.OwnerKind is "player" or "equipped");if(armor is not null&&(armor.OwnerKind!="equipped"||armor.Slot!="armor"))Success(Done(party.EquipAsync(armor.Id,"armor")));
        }
        public void Battle(string id)
        {
            ConfigureBuild();At(id switch{"ENC-RF-01"=>"RF-S03.coil","ENC-RF-02"=>"RF-S04.rook","ENC-RF-03"=>"RF-S05.relay",_=>"RF-S06.control"});Success(Done(service.BeginEncounterAsync(id)));var entry=Done(service.EncounterAsync(id))!;var party=new PartyService(store,Catalog).Inspect(View.Snapshot);var hero=entry.Entry.Units.Single(u=>u.Id=="hero");Require(hero.Level==party.Level&&hero.MaxHp==party.Derived.MaxHp&&hero.Learned.SequenceEqual(party.Build.Learned)&&hero.Active.SequenceEqual(party.Build.Active),"battle entry must derive actual equipment and lawful build");
            int confirmedCommands=0;var engineeringClock=System.Diagnostics.Stopwatch.StartNew();
            for(int n=0;n<1500&&Done(service.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;n++)
            {
                var before=Done(service.EncounterAsync(id))!.Battle;var request=FinaleBattleCandidate.Choose(service.BattleDefinitions,new(service.BattleDefinitions,before));var p=Done(service.PreviewEncounterActionAsync(id,request));Require(p.Action.Legal,p.Description);Success(Done(service.ConfirmPreviewAsync(p.Token)));var after=Done(service.EncounterAsync(id))!.Battle;
                confirmedCommands++;
                Require(after.Instance==entry.Battle.Instance&&after.Attempt==entry.Battle.Attempt&&after.Aid<=before.Aid&&before.Supplies.Where(x=>x.Used).All(x=>after.Supplies.Single(y=>y.Instance==x.Instance).Used),"same actual attempt must retain consumed shared resources");
                if(after.Finale?.Phase!=before.Finale?.Phase){var transitioned=after.Finale??throw new InvalidOperationException("actual transition lost its mechanism");Require(before.Finale is not null&&(int)transitioned.Phase==(int)before.Finale.Phase+1);var core=after.Units.Single(u=>u.Id==transitioned.Subject);Require(core.Hp==(int)Math.Ceiling(core.MaxHp*(transitioned.Phase==FinaleCorePhase.AutomaticRestart?.70:.35)),"actual capacity-derived phase boundary changed");BattleRecords.Add(new{scope="actual level-28 legal build, unchanged capacities within this attempt; engineering controller, not human balance approval",numeric_version=FinaleEncounterFactory.NumericVersion,controller_version=FinaleBattleCandidate.StrategyVersion,encounter=id,style,request,before,after});}
                if(n==0&&id=="ENC-RF-04")Throws<InvalidOperationException>(()=>Done(new PartyService(store,Catalog).ApplyBuildAsync(Build(style))));
            }
            engineeringClock.Stop();var result=Done(service.EncounterAsync(id))!;BattleRecords.Add(new{encounter=id,style,entry=entry.Entry,result=result.Battle,commands=confirmedCommands,engineering_elapsed_seconds=engineeringClock.Elapsed.TotalSeconds,human_play_seconds=(double?)null,numeric_version=FinaleEncounterFactory.NumericVersion,controller_version=FinaleBattleCandidate.StrategyVersion,numeric_status=FinaleEncounterFactory.NumericStatus});Require(result.Battle.Outcome==CombatOutcome.Controlled,"actual final encounter failed: "+id+" / "+style+" / "+StrictJson.Serialize(result.Battle));
        }
        public void Node1(bool maintenance=false)
        {
            foreach(string id in new[]{"RF-QF01-R-SCOPE","RF-QF01-R-ROUTES","RF-QF01-R-CHECKPOINT"})Material(id);Act("register_checkpoint");
            if(maintenance){Material("RF-QF01-R-SHAFT");Act("secure_shaft_handle");Material("RF-QF01-R-COIL");Battle("ENC-RF-01");Act("isolate_shaft");Act("inspect_shaft");Goto("RF-S04");}
            else{Material("RF-QF01-R-AUTHORITY");At("RF-S01.scope");Success(Done(service.FormalHandoverAsync(true)));Material("RF-QF01-L-DUTY");Material("RF-QF01-R-CORRIDOR");Act("release_corridor");Goto("RF-S04");}
            Meaning("Q-F01",("route",maintenance?"maintenance":"formal"),("scope","entry_only_not_core_safe"));Submit("Q-F01");
        }
        public void Node2()
        {
            Sources("Q-F02");Battle("ENC-RF-02");Act("audit_command_result");Meaning("Q-F02",("command_scope","local_combat_two_points"),("rook_state","alive_in_custody"),("machine_state","running_requires_separate_work"),("history","retained"));Submit("Q-F02");
            At("RF-S04.safe");Success(Done(new PartyService(store,Catalog).ApplyBuildAsync(Build(style))));Spot("RF-S04.audit");Throws<InvalidOperationException>(()=>Done(new PartyService(store,Catalog).ApplyBuildAsync(Build(style))));
        }
        public void Node3()
        {
            Material("RF-QF03-R-RESTART");Material("RF-QF03-R-ISOLATION");Battle("ENC-RF-03");At("RF-S05.isolator");Success(Done(service.ConfigureLayerIsolationAsync("ISO-L",true)));Success(Done(service.ConfigureLayerIsolationAsync("ISO-R",true)));Act("verify_layer_isolation");
            foreach(string id in new[]{"RF-QF03-R-LINK","RF-QF03-R-RENEWAL","RF-QF03-R-DAMPING","RF-QF03-R-CLOCK","RF-QF03-L-OPERATOR"})Material(id);Battle("ENC-RF-04");At("RF-S06.writer");Success(Done(service.RecordCentralSafetyAsync()));Meaning("Q-F03",("isolation","current_layer_not_global_shutdown"),("core","safe_controlled_stop_restart_disabled"),("resources","one_attempt_no_phase_refill"),("policy","not_selected"));Submit("Q-F03");
        }
        public void Node4(EndingChoice choice)
        {
            Sources("Q-F04");At("RF-S07.control");Success(Done(service.VerifyFinalControlAsync()));var task=service.Package.WritingTasks.Single(x=>x.Quest=="Q-F04");var draft=Done(store.GetDraftAsync(task.Id));Done(service.SaveDraftAsync(task.Id,draft?.Revision??0,draft?.Id,"A recorded bounded final instruction. The engineering controller explicitly selects the policy below."));Success(Done(service.ConfirmPolicyAsync(choice,true)));long sequence=store.Session.Sequence;var cancelled=Done(service.PreviewPolicyAsync());Require(cancelled.Ready&&store.Session.Sequence==sequence,StrictJson.Serialize(cancelled));service.ClosePreview(cancelled.Token);Require(Done(service.ConfirmPreviewAsync(cancelled.Token)).Status==CommitStatus.Rejected);var p=Done(service.PreviewPolicyAsync());Require(p.Ready,StrictJson.Serialize(p));Success(Done(service.ConfirmPreviewAsync(p.Token)));Success(Done(service.ConfirmPreviewAsync(p.Token)));var ending=Done(service.PresentEpilogueAsync());Require(ending.Regions.Length==6&&ending.Companions.Length==4&&ending.NaturalMistRemains);At("RF-S07.epilogue");Success(Done(service.AcknowledgeEpilogueAsync(false)));Success(Done(service.AcknowledgeEpilogueAsync(true)));
        }
        public void Report(string name)=>File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.MainPath)!,name),StrictJson.Serialize(new{scope="actual zero-XP chapter/voyage/finale path with legal level-28 builds; not human balance approval or language assessment",numeric_version=FinaleEncounterFactory.NumericVersion,controller_version=FinaleBattleCandidate.StrategyVersion,style,battles=BattleRecords,final=View.Snapshot,review="original_unreviewed_human_pending"}));
    }
    public static void Register()
    {
        Add("CP-RF-01","RF seven original scenes and fourteen paired ports keep scripts, four short drafts and authors separate",()=>{var p=Package();Require(p.Scenes.Length==7&&p.Scenes.Sum(x=>x.Geometry.Ports.Length)==14&&p.Materials.Length==22&&p.Materials.Count(m=>m.Kind=="listening_script")==4&&p.Materials.Where(m=>m.Kind=="listening_script").All(m=>m.AudioAsset=="")&&p.WritingTasks.Length==4);foreach(var scene in p.Scenes){var nav=new NavigationGrid(scene.Geometry);foreach(var port in scene.Geometry.Ports)foreach(var spot in scene.Geometry.Spots)Require(nav.FindPath(port.Stand,spot.Stand,true).Length>0);}});
        Add("CP-RF-05","Actual zero-XP through Q604 leaves untouched RF native entry and real protected checkpoint",()=>
        {using var s=Store("native-entry");var pair=ActualArrival(s);var v=Done(pair.Service.ViewAsync());var frozen=Read<FrozenPreparation>(s,"final:preparation");var checkpoint=Done(pair.Archive.FindAsync(frozen.PreFinalCheckpoint));Require(v.Snapshot.Balances["experience"]==16740&&ProgressionRules.Level(16740)==28&&v.Actor.Scene=="RF-S01"&&v.Actor.Position==FinalePackage.LandingStand&&v.Materials.Length==0&&!v.State.CheckpointRegistered&&v.State.ActualApproach is null&&v.Quests.Single(q=>q.Id=="Q-F01").Lifecycle==QuestLifecycle.Active&&v.Quests.Where(q=>q.Id!="Q-F01").All(q=>q.Lifecycle==QuestLifecycle.Unaccepted)&&checkpoint is not null);Require(v.Snapshot.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));Done(s.CreateCheckpointAsync("manual"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-rf-native-entry.json"),StrictJson.Serialize(new{scope="actual zero-XP through six chapters, board/chart, protected preparation and single atomic actual Q604 departure; RF only initialized",experience=16740,profile_path=s.MainPath,actor=v.Actor,protected_checkpoint=checkpoint,receipts=v.Snapshot.Receipts,native_human="pending",language_review="human_pending"}));});
        foreach(var scenario in new[]{(Style:"H",Choice:EndingChoice.Central,Maintenance:false),(Style:"M",Choice:EndingChoice.Island,Maintenance:false),(Style:"S",Choice:EndingChoice.Distributed,Maintenance:false),(Style:"Mixed",Choice:EndingChoice.Central,Maintenance:true)})
        {
            var captured=scenario;Add("CP-RF-03","Actual zero-XP finale "+captured.Style+" keeps 28-level resources and closes "+captured.Choice+" once",()=>
            {using var s=Store("normal-"+captured.Style);var pair=ActualArrival(s,captured.Choice==EndingChoice.Distributed);var r=new Run(s,pair.Service,captured.Style);r.Node1(captured.Maintenance);r.Node2();r.Node3();r.Node4(captured.Choice);r.Report("normal-rf-"+captured.Style.ToLowerInvariant()+".json");var final=Done(s.SnapshotAsync());Require(final.Balances["experience"]==19140&&ProgressionRules.Level(19140)==30&&final.Entities.Count(e=>e.Kind=="shared_vessel")==1&&FinaleService.MainQuests.All(q=>Read<QuestState>(s,"quest:"+q).Lifecycle==QuestLifecycle.Closed)&&final.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&x.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));Require(Read<FinalEndingSnapshot>(s,"final:ending").Choice==captured.Choice&&r.BattleRecords.Count>=5);
                // Write the actual report before tempo assertions so a failed calibration
                // retains the full world, battle and resource evidence for the next trial.
                var rook=Done(pair.Service.EncounterAsync("ENC-RF-02"))!.Battle;var core=Done(pair.Service.EncounterAsync("ENC-RF-04"))!.Battle;
                Require(rook.Round is >=5 and <=8,"engineering Rook tempo outside 5–8: "+captured.Style+" / "+rook.Round);
                Require(core.Round is >=8 and <=10,"engineering core tempo outside 8–10: "+captured.Style+" / "+core.Round);
                if(captured.Style=="Mixed"){var allies=rook.Units.Where(u=>u.Side==CombatSide.Ally).ToArray();Require(allies.Count(u=>u.Alive)>=2&&allies.Sum(u=>u.Hp)*100>=allies.Sum(u=>u.MaxHp)*20,"mixed Rook margin below the declared engineering candidate threshold (two seats and 20% team HP)");}
            });
        }
        Add("CP-RF-03","Actual RF draft CAS and checkpoint load invalidate a ready foreground preview before leasing",()=>
        {using var s=Store("stale");var pair=ActualArrival(s);var r=new Run(s,pair.Service);r.Node1();r.Node2();AtWriter();void AtWriter()=>r.At("RF-S04.writer");var old=Done(s.GetDraftAsync(pair.Service.Package.WritingTasks.Single(x=>x.Quest=="Q-F02").Id));Require(old is not null);Throws<StoreConflictException>(()=>Done(pair.Service.SaveDraftAsync(pair.Service.Package.WritingTasks.Single(x=>x.Quest=="Q-F02").Id,0,null,"wrong revision")));r.Material("RF-QF03-R-RESTART");r.Material("RF-QF03-R-ISOLATION");r.ConfigureBuild();r.At("RF-S05.relay");Success(Done(pair.Service.BeginEncounterAsync("ENC-RF-03")));var b=Done(pair.Service.EncounterAsync("ENC-RF-03"))!.Battle;var preview=Done(pair.Service.PreviewEncounterActionAsync("ENC-RF-03",FinaleBattleCandidate.Choose(pair.Service.BattleDefinitions,new(pair.Service.BattleDefinitions,b))));Require(preview.Action.Legal);string checkpoint=Done(s.CreateCheckpointAsync("manual"));Done(s.LoadCheckpointAsync(checkpoint));var before=Done(s.SnapshotAsync());var rejected=Done(pair.Service.ConfirmPreviewAsync(preview.Token));Require(rejected.Status==CommitStatus.Rejected&&rejected.Issues.Any(x=>x.Code=="stale_load")&&Done(s.SnapshotAsync()).Sequence==before.Sequence);});
    }
}
