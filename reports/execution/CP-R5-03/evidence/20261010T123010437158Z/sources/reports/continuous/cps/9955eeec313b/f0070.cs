using System.Collections.Immutable;
using Godot;
using Mist.Application.ScopedPersonalWorks;
using Mist.Core.ChapterFour;
using Mist.Core.ChapterFive;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Learning;
using Mist.Core.ScopedPersonalWorks;
using Mist.Application.UI;

public partial class Root
{
    private ScopedPersonalWorksService? _scopedPersonal;
    private readonly Dictionary<string,Stack<Guid>> _scopedPersonalPreviews=new();
    private async ValueTask<ImmutableDictionary<string,SceneGeometry>> ScopedPersonalGeometry()
    {var g=_vessel?.Geometry??ActiveGeometry;if(_chapterTwo is not null)g=g.SetItems(await _chapterTwo.CurrentGeometryAsync());if(_chapterThree is not null)g=g.SetItems(await _chapterThree.CurrentGeometryAsync());if(_chapterFour is not null)g=g.SetItems(await _chapterFour.CurrentGeometryAsync());if(_chapterFive is not null)g=g.SetItems(await _chapterFive.CurrentGeometryAsync());return g;}
    private ScopedPersonalWorksService ScopedPersonal()
    {
        if(_chapterFour is null||_chapterFive is null)throw new InvalidOperationException("原R4/R5个人节点服务尚未接入。");
        RegisterRegionalLearningScope("scoped-personal-current",ReadScopedPersonalLearningSources);return _scopedPersonal??=new(_store,_catalog,_chapterFour,_chapterFive,ScopedPersonalPackage.ParseJson(Godot.FileAccess.GetFileAsString("res://content/scoped_personal_work/learner_package.json")),ScopedPersonalGeometry);
    }
    private void ResetScopedPersonalUiState(){foreach(var stack in _scopedPersonalPreviews.Values)foreach(var token in stack)_scopedPersonal?.ClosePreview(token);_scopedPersonalPreviews.Clear();_scopedPersonal=null;_regionalLearningScopes.Remove("scoped-personal-current");}
    private void CloseScopedPersonalUiPreview(string title){if(_scopedPersonalPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_scopedPersonal?.ClosePreview(token);}
    private void AddScopedPersonalDetailButton(VBoxContainer flow,string spot)
    {if(spot is "R4-S04.w04_copy" or "R5-S07.personal_review" or "R5-S07.oren_duty" or "R3-S08.auxiliary_receipt")Button(flow,"个人工作 · 实际纸件、范围与交班",()=>Execute(async()=>await OpenScopedPersonalDetail(spot)));}
    private void AddScopedPersonalSourceControls(VBoxContainer flow,string quest)
    {
        if(quest is "CQ-MIRA-02" or "CQ-MIRA-03" or "CQ-OREN-03")Button(flow,"查看当前已取得的个人工作应答",()=>Execute(OpenScopedPersonalArchive));
    }
    private void ScopedPersonalButton(VBoxContainer flow,string text,Func<ValueTask> action)=>Button(flow,text,()=>Execute(async()=>{await action();ClosePanel();await Refresh();}));
    private static OptionButton PersonalChoice(VBoxContainer flow,string name,params string[] labels)
    {var choice=new OptionButton{AccessibilityName=name};foreach(string text in labels)choice.AddItem(text);flow.AddChild(choice);return choice;}
    private async ValueTask OpenScopedPersonalDetail(string spot)
    {
        var service=ScopedPersonal();var s=await service.SnapshotAsync();var flow=Panel("个人工作现场 · "+spot,InputOwner.Workspace);
        Label(flow,"沿用原个人节点、邀请、正文和奖励。这里记录实际纸件与接收范围；候选人物应答是原创剧情文本，人工语言审校待完成。此处不发世界奖励。",20);
        if(spot=="R4-S04.w04_copy")
        {
            ScopedPersonalButton(flow,"实际制作同一有限审阅纸件",async()=>CheckCommit(await service.MakeReviewPaperAsync()));
            var expand=PersonalChoice(flow,"扩大请求明确字段","尚未选择","向W04询问身份键是否许可","向W04询问私人住址是否许可");
            ScopedPersonalButton(flow,"实际询问扩大范围并保留应答",async()=>{if(expand.Selected==0)throw new InvalidOperationException("先明确实际提出的扩大字段。");CheckCommit(await service.AskMiraPaperExpansionAsync([expand.Selected==1?"identity_key":"private_address"]));});
            Button(flow,"逐项核对封口必要字段",()=>Execute(OpenScopedPaperSeal));Button(flow,"预览同一封件实际交付",()=>Execute(async()=>await OpenScopedPaperPreview("deliver")));
            Button(flow,"编辑原CQ-MIRA-02同一份正文",()=>Execute(async()=>await OpenChapterFourWriter("CQ-MIRA-02")));
        }
        if(spot=="R5-S07.personal_review")
        {
            var exercise=s.Entities.SingleOrDefault(e=>e.Id=="R5:personal_access_exercise");Label(flow,exercise is null?"请先沿原章节邀请、原件和安排选项启动本次演练。":"当前原演练："+StrictJson.Deserialize<PersonalAccessExercise>(exercise.Json).Mode,18);
            Button(flow,"提出当前字段与接收方问题",()=>Execute(OpenScopedMiraQuestion));ScopedPersonalButton(flow,"实际把有限副件交具名保管范围",async()=>CheckCommit(await service.MakeMiraDrillCopiesAsync()));
            Button(flow,"实际尝试当前限阅或未来联系",()=>Execute(OpenScopedMiraProbe));ScopedPersonalButton(flow,"实际关闭本演练未来自愿联系",async()=>CheckCommit(await service.WithdrawFutureMiraContactAsync()));ScopedPersonalButton(flow,"核对实际退出拒绝并保留历史",async()=>CheckCommit(await service.VerifyMiraExitAsync()));
            var old=PersonalChoice(flow,"旧解释","尚未确认","旧解释把角色条目当身份人数","旧解释已经有完整身份");var correction=PersonalChoice(flow,"追加解释","尚未确认","条目只给角色数量，身份人数未知","把两副件写成两独立证人");
            ScopedPersonalButton(flow,"实际追加解释更正保留旧版",async()=>{if(old.Selected==0||correction.Selected==0)throw new InvalidOperationException("先明确旧解释与追加解释。");CheckCommit(await service.AppendMiraCorrectionAsync(old.Selected==1?"role_entries_are_identity_count":"all_identities_known",correction.Selected==1?"role_entries_only_identity_count_unknown":"independent_double_witness"));});
            Label(flow,"真实凭证齐备后，由原章节四步骤消费者逐步确认，再依据当前实际应答记录相关本人决定、原正文意思和正式接收。重新接受邀请须重新询问；旧应答不会自动复活。",18);
            Button(flow,"编辑原CQ-MIRA-03同一份正文",()=>Execute(async()=>await OpenChapterFiveWriter("CQ-MIRA-03")));
        }
        if(spot is "R5-S07.oren_duty" or "R3-S08.auxiliary_receipt")
        {
            Label(flow,"地方轮值须同船实际返R3，在调度交接点核原独立套件；远程安排只接收本设备有限答复，不声称安装。独立接替 ROLE-R3-AUX-NEXT-WATCH 只承担下一安全窗口，不占矿泵或塔站岗。",18);
            var answer=PersonalChoice(flow,"本次设备回答","尚未确认","先隔离下一检查输入，引用同一已测套件","不隔离直接加功率");
            ScopedPersonalButton(flow,"实际开展当前有限维修或答复",async()=>{if(answer.Selected==0)throw new InvalidOperationException("先明确本次有限设备回答。");CheckCommit(await service.BeginActualOrenWorkAsync(answer.Selected==1?"isolate_before_check_same_installed_kit":"increase_without_isolation"));});
            foreach(var pair in new[]{("inspect_same_kit","实际核对原同一套件"),("isolate_input","实际隔离本次检查输入"),("test_passive_alarm","实际检查被动报警连续性"),("restore_input","实际恢复原已核配置")}){string step=pair.Item1;ScopedPersonalButton(flow,pair.Item2,async()=>CheckCommit(await service.CheckOrenDeviceAsync(step)));}
            var incoming=PersonalChoice(flow,"独立接替人员","尚未选择","ROLE-R3-AUX-NEXT-WATCH 有限下一班","奥伦自己继续代所有人");
            ScopedPersonalButton(flow,"实际由具名独立接替接收说明",async()=>{if(incoming.Selected==0)throw new InvalidOperationException("先明确实际独立接替人员。");CheckCommit(await service.ReceiveOrenInstructionsAsync("ROLE-R3-LOCAL-MAINTENANCE","ROLE-R3-REMOTE-REVIEW",incoming.Selected==1?ScopedPersonalWorksService.IncomingOrenWorker:"CP-OREN"));});
            ScopedPersonalButton(flow,"实际核对独立交班与原位置安全返程",async()=>CheckCommit(await service.PrepareActualOrenHandoverAsync()));
            if(spot=="R5-S07.oren_duty"){Button(flow,"编辑原CQ-OREN-03同一份正文",()=>Execute(async()=>await OpenChapterFiveWriter("CQ-OREN-03")));ScopedPersonalButton(flow,"由原节点正式交班释放岗位",async()=>CheckCommit(await _chapterFive!.HandoffOrenDutyAsync()));Label(flow,"实际独立接替凭证齐备后，再用原章节交班释放岗位。接受、撤回、原奖励都由原节点执行。",18);}
        }
        Button(flow,"查看当前实际获准应答档案",()=>Execute(OpenScopedPersonalArchive));
    }
    private async ValueTask OpenScopedPaperSeal()
    {
        var flow=Panel("同一审阅件 · 明确封口字段",InputOwner.Confirmation);var observers=PersonalChoice(flow,"观察条目","尚未确认","2","3");var service=PersonalChoice(flow,"服务条目","尚未确认","1","0");var source=PersonalChoice(flow,"副本来源","尚未确认","原请求同根W04-review-v1","独立的新身份源");var recipient=PersonalChoice(flow,"接收用途","尚未确认","R4-review-desk核角色数量","公开身份键");
        Button(flow,"预览明确字段封口",()=>Execute(async()=>{if(new[]{observers,service,source,recipient}.Any(x=>x.Selected==0))throw new InvalidOperationException("先逐项核对实际必要字段。");var s=await ScopedPersonal().SnapshotAsync();var copy=StrictJson.Deserialize<DerivedReviewCopy>(s.Entities.Single(e=>e.Id=="R4:mira_review_copy").Json);await OpenScopedPaperPreview("seal",new(observers.Selected==1?2:3,service.Selected==1?1:0,source.Selected==1?copy.Version:"new_identity_source",copy.SourceRoot,recipient.Selected==1?copy.Recipient:"public_notice_board",copy.Purpose,recipient.Selected!=1));}));
    }
    private async ValueTask OpenScopedPaperPreview(string operation,MiraReviewFields? fields=null)
    {
        var p=await ScopedPersonal().PreviewReviewPaperAsync(operation,fields);string title="个人同件操作预览 · "+operation;var flow=Panel(title,InputOwner.Confirmation);Label(flow,p.Description,20);foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,18);if(!p.Ready)return;if(!_scopedPersonalPreviews.TryGetValue(title,out var stack))_scopedPersonalPreviews.Add(title,stack=new());stack.Push(p.Token);Button(flow,"明确执行同一纸件操作",()=>Execute(async()=>{CheckCommit(await ScopedPersonal().ConfirmPreviewAsync(p.Token));ClosePanel();await Refresh();}));
    }
    private async ValueTask OpenScopedMiraQuestion()
    {
        var flow=Panel("当前有限安排 · 分别询问",InputOwner.Dialogue);var actor=PersonalChoice(flow,"相关本人","尚未选择","CP-MIRA","W-04受控通信应答");var fields=PersonalChoice(flow,"请求字段","尚未确认","role_counts/source_version/source_root","身份键与私人住址");var recipient=PersonalChoice(flow,"当前接收方","尚未选择","本次原演练的具名接收范围","所有公告板");
        ScopedPersonalButton(flow,"实际提出本次有限范围问题",async()=>{if(new[]{actor,fields,recipient}.Any(c=>c.Selected==0))throw new InvalidOperationException("先明确相关本人、字段和接收方。");var s=await ScopedPersonal().SnapshotAsync();var mode=StrictJson.Deserialize<PersonalAccessExercise>(s.Entities.Single(e=>e.Id=="R5:personal_access_exercise").Json).Mode;CheckCommit(await ScopedPersonal().RequestMiraRenewalAnswerAsync(actor.Selected==1?"CP-MIRA":"W-04",fields.Selected==1?ScopedPersonalWorksService.RoleFields:["identity_key","private_address"],recipient.Selected==1?ScopedPersonalWorksService.ReviewRecipients(mode):["public_notice_board"]));});
    }
    private async ValueTask OpenScopedMiraProbe()
    {
        var flow=Panel("限阅规则 · 实际访问尝试",InputOwner.Confirmation);var kind=PersonalChoice(flow,"访问种类","尚未选择","当前有限复核","未来自愿联系");var recipient=PersonalChoice(flow,"实际访问者","尚未选择","本次具名接收人","公告板外部接收人");var fields=PersonalChoice(flow,"尝试字段","尚未选择","角色数量与来源","身份键");
        ScopedPersonalButton(flow,"实际尝试并记录允许或拒绝",async()=>{if(new[]{kind,recipient,fields}.Any(c=>c.Selected==0))throw new InvalidOperationException("先明确实际尝试的访问范围。");var s=await ScopedPersonal().SnapshotAsync();var mode=StrictJson.Deserialize<PersonalAccessExercise>(s.Entities.Single(e=>e.Id=="R5:personal_access_exercise").Json).Mode;CheckCommit(await ScopedPersonal().AttemptMiraReadAsync(recipient.Selected==1?ScopedPersonalWorksService.ReviewRecipients(mode)[0]:"public_notice_board",fields.Selected==1?ScopedPersonalWorksService.RoleFields:["identity_key"],kind.Selected==1?"review":"future_contact"));});
    }
    private async ValueTask OpenScopedPersonalArchive()
    {var responses=await ScopedPersonal().ListReadableResponsesAsync();var flow=Panel("当前获准个人工作应答",InputOwner.Workspace);Label(flow,"这里只显示实际取得、当前邀请和范围仍有效的应答。撤回会停止以后访问；合法原件、主线保管与已结算奖励保留。",20);foreach(var r in responses){string id=r.Id;Button(flow,id+" · "+r.Node,()=>Execute(async()=>await OpenScopedPersonalResponse(id)));}if(responses.Length==0)Label(flow,"当前没有实际取得且仍获准的应答。",20);}
    private async ValueTask OpenScopedPersonalResponse(string id)
    {
        var r=await ScopedPersonal().OpenResponseAsync(id);var flow=Panel("获准应答原文 · "+r.Id,InputOwner.Workspace);var reader=new TextEdit{Text=r.English,Editable=false,CustomMinimumSize=new(0,310),WrapMode=TextEdit.LineWrappingMode.Boundary,AccessibilityName="当前获准个人工作完整英文应答"};flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"查看应答中文辅助",()=>Label(flow,r.Chinese,20));Label(flow,"版本 "+r.Version+" · 当前范围代 "+r.Generation+"。书面剧情应答，不计作音轨或真人同意。",18);Button(flow,"编辑对应原节点同一份正文",()=>Execute(async()=>{if(r.Node=="CQ-MIRA-02")await OpenChapterFourWriter(r.Node);else await OpenChapterFiveWriter(r.Node);}));
    }
    private async ValueTask<IReadOnlyCollection<LearningSourceAccess>> ReadScopedPersonalLearningSources()
    {if(_scopedPersonal is null)return Array.Empty<LearningSourceAccess>();return(await _scopedPersonal.ListReadableResponsesAsync()).Select(r=>new LearningSourceAccess(r.Id,r.Version,r.BodyHash)).ToArray();}
}
