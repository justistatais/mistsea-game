namespace Mist.Core.Quests;

public enum QuestLifecycle { Unaccepted, Active, Closed }
public enum QuestOutcome { Completed, CompletedWithConsequence, Withdrawn, DeclinedPermanently, Superseded }
public sealed record QuestState(string Id, Guid Instance, QuestLifecycle Lifecycle, QuestOutcome? Outcome)
{
    public QuestState Accept() => Lifecycle == QuestLifecycle.Unaccepted ? this with { Lifecycle = QuestLifecycle.Active } : throw new InvalidOperationException("Quest is already accepted or closed.");
    public QuestState Close(QuestOutcome outcome) => Lifecycle == QuestLifecycle.Active ? this with { Lifecycle = QuestLifecycle.Closed, Outcome = outcome } : throw new InvalidOperationException("Only an active quest can close.");
}
