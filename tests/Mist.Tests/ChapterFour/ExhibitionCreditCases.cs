using System.Collections.Immutable;
using Mist.Application.ChapterFour;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;
using Mist.Core.Contracts;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ChapterFour;

public static class ExhibitionCreditCases
{
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static ExhibitionCreditPackage Package()=>ExhibitionCreditPackage.ParseJson(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/chapter_four/optional_credit.json")));
    private static ProfileStore Store()=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"r4-exhibition-"+Guid.NewGuid()),Q102.Q102Cases.Catalog().ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static readonly ExhibitionContributionComparison CorrectComparison=new("workshop_team","student_reference_checks",2,"presenter",false,false);
    private static readonly ExhibitionCreditMeaning CorrectMeaning=new("workshop_team",2,"presenter","leave_as_questions","retain_originals","public_credit_only_not_trial_approval");
    private sealed class Run
    {
        public ProfileStore Store {get;} public ChapterFourCases.Run Chapter {get;} public ExhibitionCreditService Service {get;}
        public Run(ProfileStore store,bool normal=false)
        {
            Store=store;
            if(normal){var arrival=ChapterFourCases.ActualArrival(store);Chapter=new(store,arrival.Service);}
            else {Chapter=new(store);Chapter.FixturePriorCommissionAndActualVoyage();}
            Chapter.Receive();Service=new(store,Chapter.Service,Package());
        }
        public ExhibitionCreditView View=>Done(Service.ViewAsync());
        public void At(string spot)=>Chapter.At(spot);
        public void Sources()
        {foreach(var material in Service.Package.Materials){At(material.Spot);Success(Done(Service.DiscoverAsync(material.Id)));}}
        public void Commission(){Sources();At("R4-O02.revision");Success(Done(Service.AcceptCommissionAsync()));Success(Done(Service.TakeCardAsync()));Success(Done(Service.CompareAsync(CorrectComparison)));}
        public void Meaning(string body="A public contribution proposal; interpretation is explicitly confirmed, not machine-assessed English.",bool assisted=true)
        {At("R4-O02.revision");var old=Done(Store.GetDraftAsync(Service.Package.DraftTask));Done(Service.SaveDraftAsync(old?.Revision??0,old?.Id,body));Success(Done(Service.ConfirmMeaningAsync(CorrectMeaning,assisted)));}
        public ExhibitionCreditPreview Print()
        {At("R4-O02.revision");var p=Done(Service.PreviewAsync(ExhibitionCreditOperation.Print));Require(p.Ready,StrictJson.Serialize(p));Success(Done(Service.ConfirmPreviewAsync(p.Token)));return p;}
        public void Mount(){At("R4-O02.label");Success(Done(Service.MountCardAsync()));Success(Done(Service.InspectMountedAsync()));}
        public ExhibitionCreditPreview Receive()
        {At("R4-O02.revision");var p=Done(Service.PreviewAsync(ExhibitionCreditOperation.Receive));Require(p.Ready,StrictJson.Serialize(p));Success(Done(Service.ConfirmPreviewAsync(p.Token)));return p;}
    }
    public static void Register()
    {
        Add("CP-R4-04","Exhibition normal zero-XP origin actual public source comparison card mount and receipt once without reward",()=>
        {
            using var s=Store();var r=new Run(s,true);Require(Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==3840);
            var before=Done(s.SnapshotAsync());var original=r.Chapter.Service.Package.Materials.Single(m=>m.Id=="R4-O02-R-CONTRIBUTION");
            r.At("R4-O02.label");Success(Done(r.Chapter.Service.DiscoverAsync(original.Id)));r.Commission();r.Meaning();
            Require(!r.View.Snapshot.Receipts.Any(x=>x.SemanticKey=="R4:optional-credit:resolution"));
            long sequence=r.View.Snapshot.Sequence;var preview=Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Print));Require(preview.Ready&&r.View.Snapshot.Sequence==sequence);
            Success(Done(r.Service.ConfirmPreviewAsync(preview.Token)));Success(Done(r.Service.ConfirmPreviewAsync(preview.Token)));r.Mount();var received=r.Receive();Success(Done(r.Service.ConfirmPreviewAsync(received.Token)));
            var after=Done(s.SnapshotAsync());Require(after.Balances.Count==before.Balances.Count&&before.Balances.All(b=>after.Balances.GetValueOrDefault(b.Key)==b.Value));
            Require(after.Items.Count(i=>i.Id==ExhibitionCreditService.CardId)==1&&after.Items.Single(i=>i.Id==ExhibitionCreditService.CardId) is{OwnerKind:"installed",OwnerId:"R4-O02.label"});
            var receipt=after.Receipts.Single(x=>x.SemanticKey=="R4:optional-credit:resolution");Require(receipt.ActionType=="exhibition_public_revision_received");var body=StrictJson.Deserialize<ExhibitionCreditReceipt>(receipt.ResultJson);
            Require(body.ExperienceAwarded==0&&body.MarksAwarded==0&&body.PartsAwarded==0&&body.Recipient==Package().Recipient&&body.OriginalLabelPolicy=="retained_with_contribution_book"&&body.TrialSafetyScope=="public_credit_only_not_trial_approval");
            Require(Done(r.Chapter.Service.OpenMaterialAsync(original.Id)).Fingerprint==original.Fingerprint&&!after.Receipts.Any(x=>x.SemanticKey=="Q-401:resolution"||x.SemanticKey=="Q-404:resolution")&&after.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));
        });
        Add("CP-R4-04","Exhibition prior-source fixture still rejects remote missing sources wrong roles and card mounting shortcut",()=>
        {
            using var s=Store();var r=new Run(s);Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverAsync("R4-O02-CREDIT-R-LABEL")));
            r.At("R4-O02.revision");Throws<InvalidOperationException>(()=>Done(r.Service.AcceptCommissionAsync()));r.Commission();
            Throws<InvalidOperationException>(()=>Done(r.Service.CompareAsync(CorrectComparison with{SpeakerOnlyContributor=true})));r.Meaning();r.Print();
            Throws<InvalidOperationException>(()=>Done(r.Service.MountCardAsync()));var incomplete=Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Receive));Require(!incomplete.Ready&&incomplete.Issues.Any(i=>i.Code=="mount_check_missing"));
            r.At("R4-O02.label");Success(Done(r.Service.MountCardAsync()));r.At("R4-O02.revision");Require(!Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Receive)).Ready);r.At("R4-O02.label");Success(Done(r.Service.InspectMountedAsync()));r.Receive();
        });
        Add("CP-R4-04","Exhibition prior-source fixture preview cancel draft CAS move and stale load never mint or deliver",()=>
        {
            using var s=Store();var r=new Run(s);r.Commission();r.Meaning();var p=Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Print));long seq=r.View.Snapshot.Sequence;
            r.Service.ClosePreview(p.Token);Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&r.View.Snapshot.Sequence==seq);
            p=Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Print));var head=Done(s.GetDraftAsync(Package().DraftTask))!;Done(r.Service.SaveDraftAsync(head.Revision,head.Id,"A new proposal kept without automatic interpretation."));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&r.View.State!.Stage==ExhibitionCardStage.PlayerBlank);
            r.Meaning();p=Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Print));r.At("R4-O02.label");Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);
            r.At("R4-O02.revision");p=Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Print));Done(s.ContinueAsync());seq=r.View.Snapshot.Sequence;var stale=Done(r.Service.ConfirmPreviewAsync(p.Token));Require(stale.Status==CommitStatus.Rejected&&stale.Issues.Any(i=>i.Code=="stale_load")&&r.View.Snapshot.Sequence==seq);
            Require(r.View.Snapshot.Items.Count(i=>i.Id==ExhibitionCreditService.CardId)==1&&!r.View.Snapshot.Receipts.Any(x=>x.SemanticKey=="R4:optional-credit:resolution"));
        });
        Add("CP-R4-04","Exhibition prior-source fixture one installed card can be recovered after changed draft and blank explicit fallback is honest",()=>
        {
            using var s=Store();var r=new Run(s);r.Commission();r.Meaning();r.Print();r.Mount();r.Meaning("Revised unassessed prose.");Require(!Done(r.Service.PreviewAsync(ExhibitionCreditOperation.Receive)).Ready);
            r.At("R4-O02.label");Success(Done(r.Service.ReturnCardToDeskAsync()));Require(r.View.Snapshot.Items.Single(i=>i.Id==ExhibitionCreditService.CardId).OwnerKind=="player");
            r.Meaning("",true);r.Print();var made=StrictJson.Deserialize<ExhibitionPrintedCard>(r.View.Snapshot.Entities.Single(e=>e.Id=="R4:exhibition:printed-card").Json);Require(!made.ProposalEnglishAssessed&&made.PublicText.Contains("two students",StringComparison.Ordinal));r.Mount();r.Receive();
            Throws<InvalidOperationException>(()=>Done(r.Service.ReturnCardToDeskAsync()));Require(r.View.Snapshot.Items.Count(i=>i.Id==ExhibitionCreditService.CardId)==1&&r.View.Snapshot.Receipts.Count(x=>x.SemanticKey=="R4:optional-credit:resolution")==1);
        });
    }
}
