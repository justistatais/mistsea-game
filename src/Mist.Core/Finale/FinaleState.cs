using System.Collections.Immutable;
using Mist.Core.Combat;
using Mist.Core.Devices;

namespace Mist.Core.Finale;

public sealed record FinaleState
{
    public required string ContentVersion {get;init;}
    public bool CheckpointRegistered {get;init;}
    public bool FormalRecognised {get;init;}
    public bool FormalDeclined {get;init;}
    public bool FormalReleased {get;init;}
    public bool ShaftHandle {get;init;}
    public bool ShaftIsolated {get;init;}
    public long ShaftRevision {get;init;}
    public long? ShaftCheckRevision {get;init;}
    public string? ActualApproach {get;init;}
    public bool RookCustody {get;init;}
    public bool CommandChainReleased {get;init;}
    public bool RunningLoopChecked {get;init;}
    public bool LayerLeftIsolated {get;init;}
    public bool LayerRightIsolated {get;init;}
    public long LayerRevision {get;init;}
    public long? LayerCheckRevision {get;init;}
    public bool MachineHallEntered {get;init;}
    public Guid? PresentedEnding {get;init;}
}
public sealed record FinaleGate(string Name,bool Allowed,string ContentVersion);
public sealed record FinaleEncounterResult(string Encounter,bool Completed,Guid Instance,Guid Attempt,string Method,long Configuration,bool CommandChainReleased,bool AutomaticRestartDisabled);
public sealed record FinaleEncounterState(string Encounter,BattleSnapshot Battle,BattleSnapshot Entry,ActorState ReturnActor,bool ThreatControlled=false);
public sealed record RookCustody(Guid EncounterInstance,Guid Attempt,bool Alive,bool CommandAuthorityReleased,string Custodian,string SourceEncounterReceipt);
public sealed record FinaleBilingualText(string English,string Chinese);
public sealed record FinaleRegionalCard(string Region,string Stage,FinaleBilingualText Text,ImmutableArray<string> SourceHistoryIds);
public sealed record FinaleCompanionNote(string Companion,bool ActuallyJoined,bool PrivateArcCompleted,FinaleBilingualText Text,ImmutableArray<string> SourceHistoryIds);
public sealed record FinaleEpilogue(Guid SnapshotId,string EndingId,FinaleBilingualText Immediate,ImmutableArray<FinaleRegionalCard> Regions,ImmutableArray<FinaleCompanionNote> Companions,FinaleBilingualText Archive,bool NaturalMistRemains,string Scope="six_months_bounded_candidate",string ReviewStatus="original_unreviewed",string HumanReview="human_pending");
