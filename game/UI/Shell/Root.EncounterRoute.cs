using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Core.Combat;

public partial class Root
{
    private bool TowerSelected=>_chapter is not null&&(_view.Actor.Scene=="R1-S10"||_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.ChapterOne.ChapterOneService.TowerEncounter&&Mist.Core.Contracts.StrictJson.Deserialize<EncounterState>(e.Json).Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat));
    private async ValueTask<EncounterState?> CurrentEncounterAsync()
    {
        if(OptionalEncounterSelected)return await OptionalCurrentEncounterAsync();
        if(FieldRelayEncounterSelected){var current=await FieldRelayService().EncounterAsync();return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.Battle.Outcome==CombatOutcome.Controlled);}
        if(FirstApproachEncounterSelected)return await FirstApproachCurrentEncounterAsync();
        if(ChapterSixEncounterSelected){var current=await _chapterSix!.EncounterAsync(_chapterSixSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}
        if(FinaleEncounterSelected){var current=await _finale!.EncounterAsync(_finaleSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}
        if(ChapterFiveEncounterSelected){var current=await _chapterFive!.EncounterAsync(_chapterFiveSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}
        if(ChapterFourEncounterSelected){var current=await _chapterFour!.EncounterAsync(_chapterFourSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}
        if(ChapterThreeEncounterSelected){var current=await _chapterThree!.EncounterAsync(_chapterThreeSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}
        if(ChapterTwoEncounterSelected){var current=await _chapterTwo!.EncounterAsync(_chapterTwoSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}
        return TowerSelected?await _chapter!.TowerAsync():await _encounters.ViewAsync();
    }
    private ValueTask<EncounterActionPreview> PreviewEncounter(BattleRequest r)=>OptionalEncounterSelected?OptionalPreviewEncounter(r):FieldRelayEncounterSelected?FieldRelayService().PreviewActionAsync(r):FirstApproachEncounterSelected?FirstApproachPreviewEncounter(r):ChapterSixEncounterSelected?_chapterSix!.PreviewEncounterActionAsync(_chapterSixSelectedEncounter!,r):FinaleEncounterSelected?_finale!.PreviewEncounterActionAsync(_finaleSelectedEncounter!,r):ChapterFiveEncounterSelected?_chapterFive!.PreviewEncounterActionAsync(_chapterFiveSelectedEncounter!,r):ChapterFourEncounterSelected?_chapterFour!.PreviewEncounterActionAsync(_chapterFourSelectedEncounter!,r):ChapterThreeEncounterSelected?_chapterThree!.PreviewEncounterActionAsync(_chapterThreeSelectedEncounter!,r):ChapterTwoEncounterSelected?_chapterTwo!.PreviewEncounterActionAsync(_chapterTwoSelectedEncounter!,r):TowerSelected?_chapter!.PreviewTowerActionAsync(r):_encounters.PreviewAsync(r);
    private ValueTask<CommandResult> ConfirmEncounter(Guid token)=>OptionalEncounterSelected?OptionalConfirmEncounter(token):FieldRelayEncounterSelected?FieldRelayService().ConfirmPreviewAsync(token):FirstApproachEncounterSelected?FirstApproachConfirmEncounter(token):ChapterSixEncounterSelected?_chapterSix!.ConfirmPreviewAsync(token):FinaleEncounterSelected?_finale!.ConfirmPreviewAsync(token):ChapterFiveEncounterSelected?_chapterFive!.ConfirmPreviewAsync(token):ChapterFourEncounterSelected?_chapterFour!.ConfirmPreviewAsync(token):ChapterThreeEncounterSelected?_chapterThree!.ConfirmPreviewAsync(token):ChapterTwoEncounterSelected?_chapterTwo!.ConfirmPreviewAsync(token):TowerSelected?_chapter!.ConfirmPreviewAsync(token):_encounters.ConfirmAsync(token);
    private async ValueTask<CommandResult> RetryEncounter()
    {
        if(OptionalEncounterSelected)return await OptionalRetryEncounter();
        if(FieldRelayEncounterSelected)return await RetryFieldRelayEncounterAsync();
        if(FirstApproachEncounterSelected)return await FirstApproachRetryEncounter();
        if(ChapterSixEncounterSelected){var result=await _chapterSix!.WithdrawEncounterAsync(_chapterSixSelectedEncounter!);CheckCommit(result);return await _chapterSix.BeginEncounterAsync(_chapterSixSelectedEncounter!);}
        if(FinaleEncounterSelected){var result=await _finale!.WithdrawEncounterAsync(_finaleSelectedEncounter!);CheckCommit(result);return await _finale.BeginEncounterAsync(_finaleSelectedEncounter!);}
        if(ChapterFiveEncounterSelected){var result=await _chapterFive!.WithdrawEncounterAsync(_chapterFiveSelectedEncounter!);CheckCommit(result);return await _chapterFive.BeginEncounterAsync(_chapterFiveSelectedEncounter!);}
        if(ChapterFourEncounterSelected){var result=await _chapterFour!.WithdrawEncounterAsync(_chapterFourSelectedEncounter!);CheckCommit(result);return await _chapterFour.BeginEncounterAsync(_chapterFourSelectedEncounter!);}
        if(ChapterThreeEncounterSelected){var result=await _chapterThree!.WithdrawEncounterAsync(_chapterThreeSelectedEncounter!);CheckCommit(result);return await _chapterThree.BeginEncounterAsync(_chapterThreeSelectedEncounter!);}
        if(ChapterTwoEncounterSelected){var result=await _chapterTwo!.WithdrawEncounterAsync(_chapterTwoSelectedEncounter!);CheckCommit(result);return await _chapterTwo.BeginEncounterAsync(_chapterTwoSelectedEncounter!);}
        return TowerSelected?await _chapter!.RetryTowerAsync():await _encounters.RetryAsync();
    }
    private ValueTask<CommandResult> WithdrawEncounter()=>OptionalEncounterSelected?OptionalWithdrawEncounter():FieldRelayEncounterSelected?WithdrawFieldRelayEncounterAsync():FirstApproachEncounterSelected?FirstApproachWithdrawEncounter():ChapterSixEncounterSelected?_chapterSix!.WithdrawEncounterAsync(_chapterSixSelectedEncounter!):FinaleEncounterSelected?_finale!.WithdrawEncounterAsync(_finaleSelectedEncounter!):ChapterFiveEncounterSelected?_chapterFive!.WithdrawEncounterAsync(_chapterFiveSelectedEncounter!):ChapterFourEncounterSelected?_chapterFour!.WithdrawEncounterAsync(_chapterFourSelectedEncounter!):ChapterThreeEncounterSelected?_chapterThree!.WithdrawEncounterAsync(_chapterThreeSelectedEncounter!):ChapterTwoEncounterSelected?_chapterTwo!.WithdrawEncounterAsync(_chapterTwoSelectedEncounter!):TowerSelected?_chapter!.WithdrawTowerAsync():_encounters.WithdrawAsync();
    private void CloseEncounterPreview(Guid token){OptionalCloseEncounterPreview(token);_fieldRelayRepair?.ClosePreview(token);CloseFirstApproachEncounterPreview(token);_chapterSix?.ClosePreview(token);_finale?.ClosePreview(token);_chapterFive?.ClosePreview(token);_chapterFour?.ClosePreview(token);_chapterThree?.ClosePreview(token);_chapterTwo?.ClosePreview(token);_chapter?.ClosePreview(token);_encounters.ClosePreview(token);}
}
