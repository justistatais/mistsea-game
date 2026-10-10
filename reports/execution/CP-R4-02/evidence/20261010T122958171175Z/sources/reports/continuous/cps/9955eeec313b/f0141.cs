using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Application.Commands;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;
using Mist.Core.ChapterFive;
using Mist.Core.Contracts;
using Mist.Core.ScopedPersonalWorks;

namespace Mist.Application.ScopedPersonalWorks;

public sealed partial class ScopedPersonalWorksService
{
    // Returns only actual current permitted full responses. This is also the dictionary permission provider.
    public async ValueTask<ImmutableArray<PersonalReadableResponse>> ListReadableResponsesAsync()
    {
        var s=await _repository.SnapshotAsync();var views=ImmutableArray.CreateBuilder<PersonalReadableResponse>();
        foreach(var response in Package.Responses)
        {
            string? source=null;string? permission=null;string node="";long generation=0;
            foreach(string actor in new[]{"CP-MIRA","W-04"})
            {
                var exercise=BranchOperations.Read<PersonalAccessExercise>(s,"R5:personal_access_exercise");if(exercise is null)continue;var a=PersonalProductionRules.CurrentMiraAnswer(s,actor,exercise.Mode,Package.Version);
                if(a is null||a.ResponseHash!=response.Fingerprint||a.Actor!=response.Actor)continue;
                var receipt=s.Receipts.Single(r=>r.ActionType=="mira_current_bounded_question_answered"&&r.ResultJson==StrictJson.Serialize(a));source=receipt.ReceiptId;permission=a.InvitationReceipt;node="CQ-MIRA-03";generation=a.Generation;
            }
            var expansion=BranchOperations.Read<ActualMiraRenewalAnswer>(s,"personal-detail:mira02-expansion");
            var currentR4Invitation=s.Receipts.Where(r=>r.ActionType=="glasshaven_mira_review_invitation_decided").OrderByDescending(r=>r.Sequence).FirstOrDefault();
            if(source is null&&response.Id=="SP-W04-DENY"&&expansion is not null&&expansion.ResponseHash==response.Fingerprint&&expansion.NodeInstance==BranchOperations.Read<Mist.Core.Quests.QuestState>(s,"quest:CQ-MIRA-02")?.Instance&&BranchOperations.Read<ChapterFourState>(s,"chapter4") is{MiraReviewInvited:true}&&expansion.InvitationReceipt==currentR4Invitation?.ReceiptId)
            {source=s.Receipts.Single(r=>r.ActionType=="mira_paper_scope_expansion_answered"&&r.ResultJson==StrictJson.Serialize(expansion)).ReceiptId;permission=expansion.InvitationReceipt;node="CQ-MIRA-02";generation=expansion.Generation;}
            var receiving=BranchOperations.Read<OrenNamedReceiving>(s,"personal-detail:oren03-receiving");
            if(response.Id is "SP-LOCAL-RECEIVER" or "SP-REMOTE-REVIEW"&&receiving is not null&&receiving.NodeInstance==BranchOperations.Read<Mist.Core.Quests.QuestState>(s,"quest:CQ-OREN-03")?.Instance&&receiving.Version==Package.Version&&receiving.ResponseHashes.GetValueOrDefault(response.Id)==response.Fingerprint&&receiving.InvitationReceipt==PersonalProductionRules.CurrentInvitationReceipt(s,"CQ-OREN-03")&&BranchOperations.Read<ChapterFiveState>(s,"chapter5") is{OrenInvited:true})
            {source=s.Receipts.Single(r=>r.ActionType=="oren_named_incoming_instructions_received"&&r.ResultJson==StrictJson.Serialize(receiving)).ReceiptId;permission=receiving.InvitationReceipt;node="CQ-OREN-03";generation=receiving.Generation;}
            if(source is not null&&permission is not null)views.Add(new(response.Id,Package.Version,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response.English))).ToLowerInvariant(),response.English,response.Chinese,node,source,permission,generation));
        }
        return views.ToImmutable();
    }
    public async ValueTask<PersonalReadableResponse> OpenResponseAsync(string id)=>(await ListReadableResponsesAsync()).SingleOrDefault(r=>r.Id==id)??throw new InvalidOperationException("该应答未实际取得，或版本、当前本人邀请/范围已改变。");
}
