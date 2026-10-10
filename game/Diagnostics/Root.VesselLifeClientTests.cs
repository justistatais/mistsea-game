#if DEBUG
using Godot;
using Mist.Application.EventScheduler;
using Mist.Core.Contracts;
using Mist.Core.VesselLife;

public partial class Root
{
    private async Task RunVesselLifeClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        IEnumerable<Node> Nodes(Node parent){foreach(var n in parent.GetChildren()){yield return n;foreach(var c in Nodes(n))yield return c;}}
        async Task Settle(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);for(int n=0;_busy&&n<3000;n++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("Ship life UI command did not settle.");await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Click(string text){var b=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==text).ToArray();if(b.Length!=1)throw new InvalidOperationException("Expected one ship life button "+text+"; got "+b.Length);b[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        try
        {
            var before=await _store.SnapshotAsync();Check("actual_normal_entry_3840",before.Balances["experience"]==3840&&before.Receipts.All(r=>r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            _selected="HUB-S01.harbor_view";InteractSelected();await Settle();await Click("船上生活 · 当前人物与话题");
            Check("private_topic_not_opened_by_joining",!Nodes(_panels.Peek()).OfType<Button>().Any(b=>b.Text=="开始 · 听过家人回信后如何折起信纸"));
            await Click("邀请当前人物 · 谁把修好的椅子又拆开了");await Click("开始 · 谁把修好的椅子又拆开了");var script=VesselLife().Package.Scripts.Single(x=>x.Id=="BAN-LINA-OREN-1");Check("original_first_line",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==script.Lines[0].English));Check("active_special_panel_cannot_be_dropped",!CanLeaveVesselLifePanel(_routing.Top));
            await Click("明确继续下一句");await Click("返回");var paused=StrictJson.Deserialize<BanterState>((await _store.SnapshotAsync()).Entities.Single(e=>e.Id=="banter:BAN-LINA-OREN-1").Json);Check("back_pauses_same_sentence",paused.Paused&&paused.NextLine==1);
            Check("pause_releases_foreground",await _store.TryLeaseAsync("input:foreground","native-probe"));await _store.ReleaseLeaseAsync("input:foreground","native-probe");
            CloseAllPanels();await _store.ContinueAsync();ResetVesselLifeUiState();await Refresh();await OpenVesselLifeList("HUB-S01.harbor_view");await Click("恢复 · 谁把修好的椅子又拆开了");Check("continued_original_second_line",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==script.Lines[1].English));
            for(int i=1;i<script.Lines.Length;i++)await Click(i+1==script.Lines.Length?"读完本段并保存一次展示记录":"明确继续下一句");var after=await _store.SnapshotAsync();Check("once_completion_0reward",after.Receipts.Count(r=>r.SemanticKey=="banter:BAN-LINA-OREN-1:vessel-life-1:completed")==1&&before.Balances.OrderBy(x=>x.Key).SequenceEqual(after.Balances.OrderBy(x=>x.Key)));
            CloseAllPanels();await OpenVesselLifeList("HUB-S01.harbor_view");await Click("回看 · 谁把修好的椅子又拆开了");long sequence=_store.Session.Sequence;Check("readonly_replay_exact_original",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&script.Lines.All(l=>t.Text.Contains(l.English,StringComparison.Ordinal))));await Click("查看中文辅助");Check("replay_no_world_command",_store.Session.Sequence==sequence);
            Check("all_owned_event_not_running",(await _store.SnapshotAsync()).Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Where(e=>e.Definition.Id.StartsWith("BAN-",StringComparison.Ordinal)).All(e=>e.State!=EventLifecycle.Running));
        }
        catch(Exception e){checks.Add(new{name="native_exception",passed=false,error=e.ToString()});failures++;}
        string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://diagnostics");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"vessel_life_client_results.json"),StrictJson.Serialize(new{scope="actual native buttons on copied normal zero-XP route profile; no private source seeded",checks,failures,human_language="pending",human_ime="pending",audio="not_recorded"}));GetTree().Quit(failures==0?0:1);
    }
}
#endif
