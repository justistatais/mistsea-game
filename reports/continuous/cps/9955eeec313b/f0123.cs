using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.OptionalLocalWorks;

namespace Mist.Application.OptionalLocalWorks;

public sealed partial class MusicBoxRepairService
{
    public async ValueTask<DraftSaved> SaveDraftAsync(long expectedRevision,DraftId? draft,string body)
    {var session=_repository.Session;return await _repository.SaveAsync(new(session.Profile,session.Branch,session.Load,Task,draft,expectedRevision,body,Specification.Version));}
    public async ValueTask<CommandResult> ConfirmNoteMeaningAsync(ImmutableDictionary<string,string> fields,bool structuredAssistance)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"note");Permit(s);Require(Acquired(s),"请先实际取得主人许可范围内的规格。");var d=await _repository.GetDraftAsync(Task)??throw new InvalidOperationException("维修便条尚未实际保存。");
        Require(!d.Deleted&&!string.IsNullOrWhiteSpace(d.Body),"维修便条正文须真实保存；结构化解释不能替代便条原文。");var meaning=new MusicBoxNoteMeaning(d.Hash,structuredAssistance,fields);
        return await Execute(Plan(s,"musicbox_note_meaning_confirmed",State(s),new{structured_assistance=structuredAssistance,free_english_understanding="not_claimed",language_score=(string?)null},[BranchOperations.Change(s,StateId+":meaning","optional_note_meaning",meaning)],draft:new(Task,d.Id,d.Revision,d.Hash)));
    }
    private static string EvidenceId<T>(BranchSnapshot s,string action,T body) where T:class
        =>s.Receipts.Where(r=>r.ActionType==action&&r.ResultJson==StrictJson.Serialize(body)).OrderByDescending(r=>r.Sequence).FirstOrDefault()?.ReceiptId??throw new InvalidOperationException("当前具体维修事实没有同内容的实际来源回执。");
    public async ValueTask<MusicBoxPreview> PreviewDeliveryAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"owner");var st=State(s);var issues=ImmutableArray.CreateBuilder<RuleIssue>();void Check(bool value,string code,string text){if(!value)issues.Add(new(code,StateId,text));}
        Check(!st.Completed&&!BranchOperations.Receipt(s,Completion),"already_closed","同一音乐盒维修便条已经交付，不重新领取奖励。");
        if(st.Completed)return new(Guid.Empty,false,issues.ToImmutable(),null);
        Permit(s);var approval=Approval(s,st);Check(Acquired(s),"source_missing","规格原件须实际取得且当前许可有效。");
        var box=s.Items.SingleOrDefault(i=>i.Id==Box);var wheel=s.Items.SingleOrDefault(i=>i.Id==Wheel12);var check=Read<MusicBoxTurningCheck>(s,StateId+":test");
        Check(st.Returned&&box is{DefinitionId:"R3-MUSIC-BOX",OwnerKind:"warehouse",OwnerId:Owner},"same_box_not_returned","须先将原同一盒子实际归还主人。");
        Check(wheel is{DefinitionId:"MBOX-12",OwnerKind:"installed",OwnerId:Box}&&check is not null&&check.WorkInstance==st.Instance&&check.BoxSerial==Box&&check.FittedPartSerial==Wheel12&&check.ActualTeeth==12&&check.RequiredTeeth==12&&check.Configuration==st.Configuration&&check.CheckScope=="turning_mechanism_only"&&check.Compatible&&st.TestedConfiguration==st.Configuration,"check_stale","获批十二齿件与当前配置的转动检查须对应实际安装件。");
        var d=await _repository.GetDraftAsync(Task);var meaning=Read<MusicBoxNoteMeaning>(s,StateId+":meaning");Check(d is{Deleted:false}&&!string.IsNullOrWhiteSpace(d.Body),"draft_missing","维修便条原文尚未实际保存。");Check(meaning is not null&&meaning.DraftHash==d?.Hash,"meaning_stale","正文意思确认须对应当前保存稿件。");
        bool Field(string key,string value)=>meaning is not null&&meaning.Fields.TryGetValue(key,out var actual)&&actual==value;
        Check(Field("part","MBOX-12")&&Field("check","turning_mechanism_only")&&Field("box",Box)&&Field("return","same_owner")&&Field("limits","mechanism_only")&&Field("memory","private_not_published"),"meaning_invalid","便条须说明十二齿件、实际转动检查、同盒归还、检查边界和回忆保持私密；不自动理解英文。");
        if(issues.Count>0)return new(Guid.Empty,false,issues.ToImmutable(),d?.Hash);
        var currentDraft=d??throw new InvalidOperationException("当前稿件已失效。");var currentMeaning=meaning??throw new InvalidOperationException("当前意思确认已失效。");
        var delivery=new MusicBoxRepairDelivery(st.Instance,Box,Wheel12,Owner,st.Configuration,Specification.Fingerprint,EvidenceId(s,"musicbox_turning_mechanism_checked",check!),EvidenceId(s,"musicbox_owner_part_approved",approval),currentDraft.Hash,currentMeaning.StructuredAssistance,"returned_to_owner","private_not_published",0,0,0);
        // No-op custody writes make the actual serials part of the same atomic CAS as the note receipt.
        var command=Plan(s,"musicbox_repair_note_received",st with{Completed=true},delivery,[BranchOperations.Change(s,StateId+":delivery","private_optional_delivery",delivery)],items:[new(Box,"R3-MUSIC-BOX","warehouse",Owner,"warehouse",Owner,null),new(Wheel12,"MBOX-12","installed",Box,"installed",Box,null)],semantic:Completion,draft:new(Task,currentDraft.Id,currentDraft.Revision,currentDraft.Hash));
        Guid token=Guid.NewGuid();_previews.Add(token,command);return new(token,true,[],currentDraft.Hash);
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?Execute(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",StateId,"维修便条预览已关闭，请重新核对。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}
