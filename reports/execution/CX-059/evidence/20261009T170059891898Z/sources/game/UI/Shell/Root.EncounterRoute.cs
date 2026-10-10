using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Core.Combat;

public partial class Root
{
    private bool TowerSelected=>_chapter is not null&&(_view.Actor.Scene=="R1-S10"||_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.ChapterOne.ChapterOneService.TowerEncounter&&Mist.Core.Contracts.StrictJson.Deserialize<EncounterState>(e.Json).Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat));
    private async ValueTask<EncounterState?> CurrentEncounterAsync()=>TowerSelected?await _chapter!.TowerAsync():await _encounters.ViewAsync();
    private ValueTask<EncounterActionPreview> PreviewEncounter(BattleRequest r)=>TowerSelected?_chapter!.PreviewTowerActionAsync(r):_encounters.PreviewAsync(r);
    private ValueTask<CommandResult> ConfirmEncounter(Guid token)=>TowerSelected?_chapter!.ConfirmPreviewAsync(token):_encounters.ConfirmAsync(token);
    private ValueTask<CommandResult> RetryEncounter()=>TowerSelected?_chapter!.RetryTowerAsync():_encounters.RetryAsync();
    private ValueTask<CommandResult> WithdrawEncounter()=>TowerSelected?_chapter!.WithdrawTowerAsync():_encounters.WithdrawAsync();
    private void CloseEncounterPreview(Guid token){_chapter?.ClosePreview(token);_encounters.ClosePreview(token);}
}
