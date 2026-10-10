#if DEBUG
using Godot;
using Mist.Core.Contracts;
using Mist.Core.LinaPersonalWorks;

public partial class Root
{
    private async Task RunLinaPersonalWorkClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool passed){checks.Add(new{name,passed});if(!passed)failures++;}
        IEnumerable<Node> Nodes(Node root){foreach(var n in root.GetChildren()){yield return n;foreach(var c in Nodes(n))yield return c;}}
        async Task Settle(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);for(int i=0;_busy&&i<4000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("Lina UI did not settle.");await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Click(string text){var b=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==text).ToArray();if(b.Length!=1)throw new InvalidOperationException("Expected one Lina button "+text+", got "+b.Length);b[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        async Task At(string spot)
        {
            string target=spot.Split('.')[0];while(_view.Actor.Scene!=target){var p=_vessel!.Geometry[_view.Actor.Scene].Ports.Single(p=>p.TargetScene==target);var a=_view.Actor;CheckCommit(await _vessel.MoveAsync(new Mist.Core.Exploration.NavigationGrid(_vessel.Geometry[a.Scene]).FindPath(a.Position,p.Stand,true)));CheckCommit(await _vessel.EnterPortAsync(p.Id));await Refresh();}
            var to=_vessel!.Geometry[target].Spots.Single(s=>s.Id==spot).Stand;CheckCommit(await _vessel.MoveAsync(new Mist.Core.Exploration.NavigationGrid(_vessel.Geometry[target]).FindPath(_view.Actor.Position,to,true)));await Refresh();
            _selected=spot;InteractSelected();await Settle();await Click("莉娜当前受邀个人工作");
        }
        async Task Start(string node){await Click("莉娜明确邀请本项有限协助 · "+node);await Click("受理或打开同一项 · "+node);}
        async Task Source(string id)
        {
            await Click("实际取得并读取 · "+LinaMaterialName(id));var m=LinaWork().Package.Materials.Single(m=>m.Id==id);Check("actual_full_private_source_"+id,Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==m.English));
            await Click("查看当前原件中文辅助");await Click("返回");
        }
        void ChooseAll(){foreach(var option in Nodes(_panels.Peek()).OfType<OptionButton>()){Check("meaning_has_explicit_unselected_default",option.Selected==0);option.Select(1);}}
        async Task Submit(string node,string outcome)
        {
            long before=_store.Session.Sequence;await Click("只读预览原结果 · "+outcome);Check("preview_readonly_"+node,_store.Session.Sequence==before);await Click("返回");Check("cancel_has_no_reward_"+node,_store.Session.Sequence==before);await Click("只读预览原结果 · "+outcome);await Click("明确提交莉娜同一项实际结果");
        }
        try
        {
            var before=await _store.SnapshotAsync();Check("normal_zero_XP_source_no_fixture",before.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal))&&!(await LinaWork().ViewAsync()).Any());
            await At("HUB-S01.deckwatch");await Start("CQ-LINA-01");await Source("LI01-APPENDIX");await Source("LI01-INDEX");await Click("比较历史附页与独立索引");ChooseAll();await Click("明确保存这次实际比较");await Submit("CQ-LINA-01","继续限域查询");
            await At("HUB-S02.correspondence");await Start("CQ-LINA-02");await Source("LI02-RELAY");await Click("编辑本人的有限询问信");
            string body="Would the sealed LT-17 recipient like to exchange letters now? Please keep the address private. This enquiry does not promise a visit. 中文草稿 保留 é 🛠️";
            var writer=Nodes(_panels.Peek()).OfType<TextEdit>().Single(t=>t.Editable);writer.Text=body;writer.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存当前询问信正文");Check("native_saved_exact_body",(await _store.GetDraftAsync(Mist.Application.LinaPersonalWorks.LinaPersonalWorkService.LetterTask))?.Body==body);await Click("返回");
            Check("save_before_actual_Continue",await SaveAdventureDrafts());CloseAllPanels();await _store.ContinueAsync();ResetRegionalUiState();await Refresh();await At("HUB-S02.correspondence");await Click("受理或打开同一项 · CQ-LINA-02");await Click("编辑本人的有限询问信");Check("Continue_same_exact_letter",Nodes(_panels.Peek()).OfType<TextEdit>().Single(t=>t.Editable).Text==body);await Click("返回");
            await Click("明确核对当前保存稿意思");ChooseAll();await Click("明确记录当前稿意思");await Click("实际转交同一封信给保管中继");await Source("LI02-DELIVERY");
            var second=(await LinaWork().ViewAsync()).Single(n=>n.Node=="CQ-LINA-02");Check("actual_same_finite_letter_delivered",(await _store.SnapshotAsync()).Items.Single(i=>i.Id=="LINA-LETTER:"+second.Instance).OwnerId=="LT17:sealed-recipient");await Submit("CQ-LINA-02","经保管人中继");
            await At("HUB-S01.deckwatch");await Start("CQ-LINA-03");await Source("LI03-REPLY");await Click("核验当前本人回信的限定范围");ChooseAll();await Click("明确核验当前回信");
            await Click("分别记录本人当前同意 · CP-LINA · 继续通信");await Click("分别记录本人当前同意 · N-LINA-BROTHER · 继续通信");await Source("LI03-TOPICS");await Click("本人许可有限公开话题 · folding_only");await Click("本人撤回有限公开话题 · keep_original_words");
            await Submit("CQ-LINA-03","继续通信");var final=await _store.SnapshotAsync();Check("three_original_once_rewards",final.Balances["experience"]==before.Balances["experience"]+85&&final.Receipts.Count(r=>r.SemanticKey=="CQ-LINA-03:resolution")==1);
            await At("HUB-S01.deckwatch");await Click("撤回本项协助许可 · CQ-LINA-03");Check("current_source_revocation",!(await LinaWork().AccessibleMaterialsAsync()).Any(m=>m.Node=="CQ-LINA-03"));
            Check("private_address_never_public_body",!final.Entities.Any(e=>e.Kind=="lina_public_topic_scope"&&e.Json.Contains(body,StringComparison.Ordinal)));
        }
        catch(Exception e){checks.Add(new{name="native_exception",passed=false,error=e.ToString()});failures++;}
        CloseAllPanels();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://diagnostics");Directory.CreateDirectory(output);System.IO.File.WriteAllText(Path.Combine(output,"lina_personal_client_results.json"),StrictJson.Serialize(new{checks,failures,scope="actual native UI from untouched normal zero-XP through Q601 entry; source acquisition, exact draft, Continue, readonly preview/cancel, original result and current private scope",human_play="not_run",physical_ime="not_run",language_review="human_pending"}));GetTree().Quit(failures==0?0:1);
    }
}
#endif
