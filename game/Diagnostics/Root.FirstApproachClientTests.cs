#if DEBUG
using Godot;
using Mist.Application.Contracts;
using Mist.Application.FirstApproaches;
using Mist.Application.UI;
using Mist.Core.ChapterOne;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.FirstApproaches;
using Mist.DebugCandidates;

public partial class Root
{
    private async Task RunFirstTowerClientTests()
    {
        bool outer=OS.GetCmdlineUserArgs().Contains("--first-outer-client-test");string id=outer?FirstApproachRules.Outer:FirstApproachRules.Side;var checks=new List<object>();int failures=0;void Check(string name,bool passed){checks.Add(new{name,passed});if(!passed)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("Actual tower base UI action did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected one actual base approach button: "+caption+"; found "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton Choice(string name)=>Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName==name);
        async Task Walk(Point2 to){CloseAllPanels();await Refresh();var path=new NavigationGrid(ActiveGeometry[_view.Actor.Scene]).FindPath(_view.Actor.Position,to,true);if(path.IsEmpty)throw new InvalidOperationException("Actual base safe path absent.");await _chapter!.MoveAsync(path);await Refresh();}
        async Task Spot(string name){await Walk(ActiveGeometry["R1-S09"].Spots.Single(s=>s.Id==name).Stand);_selected=name;InteractSelected();await Settle();}
        async Task RejectWrongStation(){long sequence=(await _store.SnapshotAsync()).Sequence;bool rejected=false;try{await _firstApproaches!.BeginAsync(id);}catch(InvalidOperationException){rejected=true;}Check("base_begin_at_source_notice_rejected_without_write",rejected&&(await _store.SnapshotAsync()).Sequence==sequence);}
        async Task ActionPreview()
        {
            var battle=(await _firstApproaches!.EncounterAsync(id))!.Battle;var request=BattleCandidate.Choose(new(_firstApproaches.BattleDefinitions,battle));var actor=battle.Units.Single(u=>u.Id==battle.CurrentActor);var ids=new[]{"BASIC","GUARD","MOVE","AID","REVIVE","SUPPLY","RECLAIM","CONTROL"}.Concat(actor.Active).Append("END").ToArray();int index=Array.IndexOf(ids,request.Action);if(index<0)throw new InvalidOperationException("Current legal controller action not offered by actual shared UI.");var action=Choice("计划行动");action.Select(index);action.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
            foreach(var unit in battle.Units){var c=Nodes(_panels.Peek()).OfType<CheckBox>().Single(c=>c.Text==unit.Name+" · "+SlotName(unit.Slot));c.ButtonPressed=request.Targets.Contains(unit.Id);}
            Choice("计划换位位置").Select(request.Destination is{} destination?destination+1:0);var devices=battle.Traps.Where(t=>t.Owner==actor.Id).Select(t=>t.Id).Concat(battle.Supplies.Where(s=>!s.Used).Select(s=>s.Instance)).Concat(battle.Controls.Keys).ToArray();int device=request.Device is null?-1:Array.IndexOf(devices,request.Device);if(request.Device is not null&&device<0)throw new InvalidOperationException("Selected device absent from real base UI.");Choice("装置、补给或现场工作点").Select(device+1);long sequence=(await _store.SnapshotAsync()).Sequence;await Click("预览此行动与费用");Check("base_current_legal_preview_read_only",(await _store.SnapshotAsync()).Sequence==sequence&&_battlePreview is not null&&_routing.Owner==InputOwner.Confirmation);
        }
        try
        {
            if(_chapter is null||_firstApproaches is null)throw new InvalidOperationException("Requires actual copied untouched normal-first-tower-native-entry.");var start=await _store.SnapshotAsync();Check("actual_zero_XP_source_has_real_680XP_and_no_positive_fixture",start.Balances.GetValueOrDefault("experience")==680&&_view.Actor.Scene=="R1-S09"&&start.Receipts.All(r=>r.ActionType!="explicit_engineering_fixture"&&!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)));Check("base_source_unread_and_both_paths_unplayed",(await _firstApproaches.MaterialsAsync()).All(m=>m.Scene!="R1-S09")&&await _firstApproaches.EncounterAsync(id)is null&&FirstApproachService.TowerAccessIssue(start)is not null);if(start.Balances.GetValueOrDefault("experience")!=680||FirstApproachService.TowerAccessIssue(start)is null)throw new InvalidOperationException("Native base test must start from untouched real 680XP entry.");
            await Spot("R1-S09.control_notice");await Click(outer?"取得塔基外闸完整说明":"取得塔基侧闸完整说明");var source=_firstApproaches.Package.Materials.Single(m=>m.Id==(outer?"R1-APP-R-OUTER":"R1-APP-R-SIDE"));Check("base_full_original_actually_displayed",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==string.Join("\n\n",source.Paragraphs.Select(p=>p.En))));ApplyScale(200);await Frame();Check("base_reader_200_percent",_split.Vertical&&Nodes(_panels.Peek()).OfType<TextEdit>().Single().GetThemeFontSize("font_size")==40);ApplyScale(100);await RejectWrongStation();CloseAllPanels();
            if(!outer)
            {
                await Spot("R1-S09.side_gate");long before=(await _store.SnapshotAsync()).Sequence;await Click("实际固定侧闸支架");Check("side_brace_before_isolation_rejected",(await _store.SnapshotAsync()).Sequence==before&&FirstApproachService.TowerAccessIssue(await _store.SnapshotAsync())is not null);CloseAllPanels();await Spot("R1-S09.side_gate");await Click("实际隔离侧闸驱动");var isolated=(await _store.SnapshotAsync()).Entities.Single(e=>e.Id=="first-approach:side-maintenance").Json;CloseAllPanels();await _store.ContinueAsync();ResetFirstApproachUiState();await Refresh();Check("side_partial_actual_isolation_survives_continue",(await _store.SnapshotAsync()).Entities.Single(e=>e.Id=="first-approach:side-maintenance").Json==isolated&&FirstApproachService.TowerAccessIssue(await _store.SnapshotAsync())is not null);
                await Spot("R1-S09.side_gate");await Click("实际固定侧闸支架");Check("side_brace_is_not_alone_zero_energy_result",FirstApproachService.TowerAccessIssue(await _store.SnapshotAsync())is not null);await Spot("R1-S09.side_gate");await Click("现场检查侧闸零能与零运动");
            }
            else
            {
                await Spot("R1-S09.outer_gate");await Click("明确选择外闸机卫战斗或继续");Check("base_supplies_not_implicitly_carried",Nodes(_panels.Peek()).OfType<CheckBox>().All(c=>!c.ButtonPressed));await Click("明确进入塔基所选遭遇");var entry=(await _firstApproaches.EncounterAsync(id))!;Check("base_shared_actual_panel_with_legal_three_people",FirstApproachEncounterSelected&&_routing.Owner==InputOwner.CombatDecision&&_battleCards.Count==8&&!MayMove()&&entry.Battle.Units.Count(u=>u.Side==CombatSide.Ally)==3&&entry.Battle.Units.Where(u=>u.Side==CombatSide.Ally).All(u=>u.Level==5));
                await ActionPreview();var token=_battlePreview!.Value;long before=(await _store.SnapshotAsync()).Sequence;await Click("取消计划，保留当前资源");Check("base_cancelled_native_preview_cannot_execute",(await _firstApproaches.ConfirmAsync(token)).Status==CommitStatus.Rejected&&(await _store.SnapshotAsync()).Sequence==before);
                await ActionPreview();await Click("执行已选行动");var actual=StrictJson.Serialize(await _firstApproaches.EncounterAsync(id));CloseAllPanels();await _store.ContinueAsync();ResetFirstApproachUiState();await Refresh();await OpenBattle();await Frame();Check("base_continue_restores_actual_selected_encounter_not_workshop",FirstApproachEncounterSelected&&StrictJson.Serialize(await _firstApproaches.EncounterAsync(id))==actual&&_routing.Owner==InputOwner.CombatDecision);
                await Click("撤回完整战前入口…");await Click("明确撤回入口");Check("base_withdraw_actual_entry_preserves_source",(await _firstApproaches.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Withdrawn&&(await _firstApproaches.MaterialsAsync()).Any(m=>m.Id==source.Id));CloseAllPanels();await Spot("R1-S09.outer_gate");await Click("明确选择外闸机卫战斗或继续");await Click("明确进入塔基所选遭遇");var retry=(await _firstApproaches.EncounterAsync(id))!;Check("base_retry_keeps_instance_changes_attempt",retry.Battle.Instance==entry.Battle.Instance&&retry.Battle.Attempt!=entry.Battle.Attempt);
                for(int i=0;i<800&&(await _firstApproaches.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;i++){await ActionPreview();await Click("执行已选行动");}
            }
            var controlled=await _store.SnapshotAsync();var local=StrictJson.Deserialize<TidegateLocalState>(controlled.Entities.Single(e=>e.Id=="tower:local").Json);Check("one_actual_base_result_does_not_create_tower_safety_ship_or_reward",FirstApproachService.TowerAccessIssue(controlled)is null&&controlled.Receipts.Count(r=>r.ActionType=="first_approach_result"&&r.SemanticKey.StartsWith("first-approach:ENC-R1-",StringComparison.Ordinal))==1&&!local.Safe&&!local.FinalInspection&&!controlled.Entities.Any(e=>e.Id=="ship:glimmer"||e.Id=="readiness:TWR-R1")&&controlled.Balances.OrderBy(p=>p.Key).SequenceEqual(start.Balances.OrderBy(p=>p.Key)));CloseAllPanels();var port=ActiveGeometry["R1-S09"].Ports.Single(p=>p.TargetScene=="R1-S10");await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();Check("base_actual_paired_port_opens_only_after_current_result",_view.Actor.Scene=="R1-S10"&&_view.Snapshot.Balances["experience"]==680);await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="tower_base_native_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,outer?"first_outer_client_results.json":"first_side_client_results.json"),StrictJson.Serialize(new{checks,failures,route=id,source="actual zero-XP through real relay/Q101/Q102/Q103 to untouched 680XP current Q104 permit and supply; native source/station/manual or full shared combat/preview/cancel/continue/withdraw/retry/paired port controls; engineering action controller",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",balance_and_language_calibration="human_pending"}));GD.Print("MISTSEA_FIRST_TOWER_CLIENT_TESTS "+id+" "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif
