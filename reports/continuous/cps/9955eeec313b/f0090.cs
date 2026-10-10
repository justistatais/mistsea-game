using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;

namespace Mist.Application.ChapterFour;

public sealed partial class ExhibitionCreditService
{
    public async ValueTask<ExhibitionCreditPreview> PreviewAsync(ExhibitionCreditOperation operation)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Permit(s);
        var draft=await _repository.GetDraftAsync(Package.DraftTask);var proposal=BranchOperations.Read<ExhibitionCreditProposal>(s,ProposalId);
        var issues=ImmutableArray.CreateBuilder<RuleIssue>();void Check(bool okay,string code,string message){if(!okay)issues.Add(new(code,StateId,message));}
        try{await At(s,"R4-O02.revision");}catch(InvalidOperationException){Check(false,"wrong_stand","请在展示室修订工作台进行明确制作或交接。");}
        Check(AllSources(s),"sources_changed","当前三份公共来源版本或指纹不符。");
        Check(st.Compared&&BranchOperations.Read<ExhibitionContributionComparison>(s,ComparisonId) is{},"comparison_missing","贡献岗位尚未实际比较。");
        Check(proposal is not null&&proposal.DraftHash==draft?.Hash,"draft_changed","正文须有与当前保存版本相符的明确意思确认。");
        Check(proposal?.StructuredAssistance==true||draft is{Deleted:false}&&!string.IsNullOrWhiteSpace(draft.Body),"empty_draft","空稿仅允许明确结构化辅助，不自动填入标准英文。");
        var meaning=proposal?.Meaning;
        Check(meaning is{Construction:"workshop_team",Calibrators:2,Presentation:"presenter",UnknownClaims:"leave_as_questions",HistoricalRecords:"retain_originals",SafetyScope:"public_credit_only_not_trial_approval"},
            "meaning_invalid","修订应保留建造、两人参考检查和介绍岗位，未知主张只列问题，原件保留，不批准试验安全。");
        string publicText="Instrument housing: workshop team. Reference marks: checked by two students. Presentation: presenter. This contribution label records these roles only; it does not certify trial safety. The original label and contribution book are retained.";
        var changes=ImmutableArray.CreateBuilder<EntityChange>();string key,action;object payload;
        if(operation==ExhibitionCreditOperation.Print)
        {
            Check(st.Stage==ExhibitionCardStage.PlayerBlank,"card_not_at_desk","须先实际领取本份空白卡片；制成后重做须明确取回同一张卡。");
            Check(s.Items.Any(i=>i.Id==CardId&&i.DefinitionId==Package.CardDefinition&&i.OwnerKind=="player"&&i.OwnerId=="hero"),"card_missing","本份空白卡片不在你手中。");
            var card=new ExhibitionPrintedCard(st.Instance,CardId,checked(st.CardRevision+1),draft?.Hash,publicText,Versions());
            changes.Add(BranchOperations.Change(s,StateId,"exhibition_credit_state",st with{Stage=ExhibitionCardStage.Printed,CardRevision=card.Revision,InspectionRevision=null}));
            changes.Add(BranchOperations.Change(s,PrintedId,"exhibition_printed_card",card));
            key="R4:exhibition:print:"+st.Instance+":"+card.Revision;action="exhibition_card_physically_prepared";payload=card;
        }
        else if(operation==ExhibitionCreditOperation.Receive)
        {
            var card=BranchOperations.Read<ExhibitionPrintedCard>(s,PrintedId);
            Check(st.Stage==ExhibitionCardStage.Mounted&&st.InspectionRevision==st.CardRevision,"mount_check_missing","须实际挂卡并在原展位核对这一版，不能由工作台按钮远程安装。");
            Check(s.Items.Any(i=>i.Id==CardId&&i.DefinitionId==Package.CardDefinition&&i.OwnerKind=="installed"&&i.OwnerId=="R4-O02.label"),"card_not_installed","本份卡片没有实际留在展位。");
            Check(card is not null&&card.Instance==st.Instance&&card.Revision==st.CardRevision&&card.DraftHash==draft?.Hash&&card.PublicText==publicText&&
                card.SourceVersions.Count==Versions().Count&&card.SourceVersions.All(p=>Versions().GetValueOrDefault(p.Key)==p.Value),"printed_version_changed","已挂卡片与当前稿件、提议或来源版本不同，请先取回重做。");
            var received=new ExhibitionCreditReceipt(st.Instance,CardId,st.CardRevision,Package.Recipient,"R4-O02","R4-O02.label",publicText,draft?.Hash,Versions(),"retained_with_contribution_book","public_credit_only_not_trial_approval");
            changes.Add(BranchOperations.Change(s,StateId,"exhibition_credit_state",st with{Stage=ExhibitionCardStage.Received}));
            changes.Add(BranchOperations.Change(s,"R4:exhibition:accepted-label","exhibition_credit_receipt",received));
            key="R4:optional-credit:resolution";action="exhibition_public_revision_received";payload=received;
        }
        else throw new ArgumentOutOfRangeException(nameof(operation));
        Guid token=Guid.NewGuid();
        if(issues.Count>0)return new(token,operation,false,issues.ToImmutable(),null);
        _previews[token]=BranchOperations.Plan(_repository,s,key,action,changes.ToImmutable(),Reads(s),payload,
            draft:new(Package.DraftTask,draft?.Id,draft?.Revision??0,draft?.Hash));
        return new(token,operation,true,[],publicText);
    }
}
