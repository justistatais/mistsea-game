using System.Collections.Immutable;
using Mist.Core.Contracts;

namespace Mist.Core.ReleaseReadiness;

// The current work producers must update the register with each accepted onsite commitment.
// An absent register is unknown, rather than proof that no risky work remains.
public sealed record CurrentRiskRegister(long Version,ImmutableArray<string> CommitmentIds,ImmutableArray<string> TransportIds,string SourceReceipt);
public sealed record RiskRegisterVerification(long Version,ImmutableArray<string> CommitmentIds,ImmutableArray<string> TransportIds);
public sealed record ProtectedEndingCheckpoint(string Id,string Slot,ProfileId Profile,BranchId SourceBranch,LoadContext SourceLoad,string Release,long WorldSequence);
public sealed record EndingPresentation(string EndingId,ImmutableArray<string> Modules,bool LanguageEvaluated=false,decimal? Band=null,bool LearningHistoryReset=false);
