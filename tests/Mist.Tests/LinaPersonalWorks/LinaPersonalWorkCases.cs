using Mist.Application.CompanionArcs;
using Mist.Application.Contracts;
using Mist.Application.LinaPersonalWorks;
using Mist.Application.VesselInterior;
using Mist.Core.Contracts;
using Mist.Core.LinaPersonalWorks;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.LinaPersonalWorks;
public static class LinaPersonalWorkCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static LinaWorkPackage Package()=>LinaWorkPackage.Parse(File.ReadAllText(Path.Combine(Root,"content/source/lina_personal/learner_package.json")));
    private static ProfileStore Store(string name,Action<string>? fault=null)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"lina-work-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId,fault);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    public static (LinaPersonalWorkService Service,VesselInteriorService Interior) NormalBoarded(ProfileStore s)
    {
        var p=ChapterSix.ChapterSixCases.ActualArrival(s);var r=new ChapterSix.ChapterSixCases.Run(s,p.Service,p.Interior);r.Node601();r.Board();
        VesselLife.VesselLifeCases.At(s,p.Interior,"HUB-S01.deckwatch");return(new(s,Catalog,Package()),p.Interior);
    }
    private static void At(ProfileStore s,VesselInteriorService v,string node)=>VesselLife.VesselLifeCases.At(s,v,node=="CQ-LINA-02"?"HUB-S02.correspondence":"HUB-S01.deckwatch");
    private static void Start(ProfileStore s,LinaPersonalWorkService w,VesselInteriorService v,string node)
    {At(s,v,node);Success(Done(w.InviteAsync(node,true)));Success(Done(w.AcceptAsync(node)));}
    private static void Material(LinaPersonalWorkService w,string id){Success(Done(w.AcquireAsync(id)));Require(!string.IsNullOrWhiteSpace(Done(w.OpenMaterialAsync(id)).English));}
    private static void Submit(LinaPersonalWorkService w,string node,string outcome)
    {
        var p=Done(w.PreviewAsync(node,outcome));w.ClosePreview(p.Token);Require(Done(w.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);
        p=Done(w.PreviewAsync(node,outcome));Require(p.Ready);Success(Done(w.ConfirmPreviewAsync(p.Token)));
    }
    private static void First(ProfileStore s,LinaPersonalWorkService w,VesselInteriorService v,string outcome="继续限域查询")
    {
        Start(s,w,v,"CQ-LINA-01");Material(w,"LI01-APPENDIX");Material(w,"LI01-INDEX");
        long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(w.VerifyTransferAsync("LT-17","I-LT-17","current_address","alive")));Require(s.Session.Sequence==seq);
        Success(Done(w.VerifyTransferAsync("LT-17","I-LT-17","historical_transfer_only","unknown")));Submit(w,"CQ-LINA-01",outcome);
    }
    private static void Second(ProfileStore s,LinaPersonalWorkService w,VesselInteriorService v,string path)
    {
        Start(s,w,v,"CQ-LINA-02");Material(w,"LI02-RELAY");if(path=="direct")Material(w,"LI02-DIRECT");
        Throws<InvalidOperationException>(()=>Done(w.AcquireAsync("LI02-DELIVERY")));
        var draft=Done(w.SaveLetterAsync(null,0,"Would you like to exchange letters now? This sealed enquiry keeps the address private. It does not promise a visit. 中文原稿保留。"));
        Success(Done(w.ConfirmLetterMeaningAsync("sealed_LT17_recipient","ask_current_contact_preference","not_public","not_promised",false)));
        var head=Done(s.GetDraftAsync(LinaPersonalWorkService.LetterTask))!;Done(w.SaveLetterAsync(head.Id,head.Revision,head.Body+" Revised."));
        Throws<InvalidOperationException>(()=>Done(w.DispatchLetterAsync(path)));
        Success(Done(w.ConfirmLetterMeaningAsync("sealed_LT17_recipient","ask_current_contact_preference","not_public","not_promised",false)));Success(Done(w.DispatchLetterAsync(path)));
        Material(w,"LI02-DELIVERY");var local=Done(w.ViewAsync()).Single(n=>n.Node=="CQ-LINA-02");var snap=Done(s.SnapshotAsync());Require(snap.Items.Single(i=>i.Id=="LINA-LETTER:"+local.Instance) is{OwnerKind:"warehouse",OwnerId:"LT17:sealed-recipient"});
        Submit(w,"CQ-LINA-02",path=="custodian"?"经保管人中继":"本人同意的直接书信");
    }
    public static void Register()
    {
        Add("CP-LINA-PERSONAL-03","eight_actual_bilingual_Lina_sources_keep_original_nodes_and_distinct_private_purposes",()=>{var p=Package();Require(p.Materials.Length==8&&p.Materials.Sum(m=>m.English.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length)>500);using var s=Store("definition");var original=new CompanionArcService(s,Catalog).Definitions;Require(original.Values.Count(d=>d.Actor=="CP-LINA")==3);});
        foreach(string ending in new[]{"继续通信","双方同意后见面","明确暂缓联系并保存转交方式"})
        {
            string chosen=ending;Add("CP-LINA-PERSONAL-03","actual_zero_XP_Lina_three_producers_"+chosen+"_once_original_rewards_current_permission_and_private_custody",()=>
            {
                using var s=Store(chosen);var (w,v)=NormalBoarded(s);var before=Done(s.SnapshotAsync());Require(before.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal))&&Done(w.AccessibleMaterialsAsync()).Length==0);
                First(s,w,v);Second(s,w,v,chosen=="双方同意后见面"?"direct":"custodian");Start(s,w,v,"CQ-LINA-03");
                Throws<InvalidOperationException>(()=>Done(w.AcquireAsync("LI03-MEETING")));Material(w,"LI03-REPLY");
                Throws<InvalidOperationException>(()=>Done(w.VerifyReplyAsync("N-LINA-BROTHER","current_correspondence_preference",false,true)));
                Success(Done(w.VerifyReplyAsync("N-LINA-BROTHER","current_correspondence_preference",false,false)));
                if(chosen!="明确暂缓联系并保存转交方式")
                {
                    var purpose=chosen=="继续通信"?ContactPurpose.Correspondence:ContactPurpose.Meeting;
                    if(purpose==ContactPurpose.Meeting)Material(w,"LI03-MEETING");
                    Success(Done(w.ConfirmCurrentContactAsync("CP-LINA",purpose)));Throws<InvalidOperationException>(()=>Done(w.PreviewAsync("CQ-LINA-03",chosen)));
                    Success(Done(w.ConfirmCurrentContactAsync("N-LINA-BROTHER",purpose)));var stale=Done(w.PreviewAsync("CQ-LINA-03",chosen));
                    Success(Done(w.ConfirmCurrentContactAsync("N-LINA-BROTHER",purpose,true)));Require(Done(w.ConfirmPreviewAsync(stale.Token)).Status!=CommitStatus.Committed);
                    Success(Done(w.ConfirmCurrentContactAsync("N-LINA-BROTHER",purpose)));
                }
                Material(w,"LI03-TOPICS");Success(Done(w.DecidePublicTopicAsync("folding_only",true)));Success(Done(w.DecidePublicTopicAsync("keep_original_words",false)));
                var preview=Done(w.PreviewAsync("CQ-LINA-03",chosen));long seq=s.Session.Sequence;Done(s.ContinueAsync());Require(Done(w.ConfirmPreviewAsync(preview.Token)).Status!=CommitStatus.Committed&&s.Session.Sequence==seq);
                Submit(w,"CQ-LINA-03",chosen);var final=Done(s.SnapshotAsync());Require(final.Balances["experience"]==before.Balances["experience"]+85&&final.Items.Count(i=>i.DefinitionId=="LINA-PRIVATE-LETTER")==1);
                Require(final.Receipts.Count(r=>r.SemanticKey=="CQ-LINA-03:resolution")==1);long complete=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(w.PreviewAsync("CQ-LINA-03",chosen)));Require(s.Session.Sequence==complete);
                Success(Done(w.InviteAsync("CQ-LINA-03",false)));Require(!Done(w.AccessibleMaterialsAsync()).Any(m=>m.Node=="CQ-LINA-03"));Throws<InvalidOperationException>(()=>Done(w.OpenMaterialAsync("LI03-REPLY")));
                File.WriteAllText(Path.Combine(s.RootPath,"lina-three-producers-result.json"),StrictJson.Serialize(new{scope="actual zero-XP public chain and actual private invitation/custody/current answers; no seeded result",ending=chosen,experience=final.Balances["experience"],nodes=Done(w.ViewAsync()),receipts=final.Receipts,human_review="pending"}));
            });
        }
        Add("CP-LINA-PERSONAL-05","actual_zero_XP_Lina_native_entry_without_invitation_private_sources_or_work_result",()=>
        {using var s=Store("native-entry");var (w,v)=NormalBoarded(s);var snap=Done(s.SnapshotAsync());Require(Done(w.ViewAsync()).Length==0&&Done(w.AccessibleMaterialsAsync()).Length==0&&snap.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));File.WriteAllText(Path.Combine(s.RootPath,"normal-lina-personal-native-entry.json"),StrictJson.Serialize(new{scope="actual zero-XP through Q601 and actual boarding; no private letter or personal producer seeded",snap.Sequence,experience=snap.Balances["experience"],human="pending"}));});
    }
}
