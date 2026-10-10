using System.Collections.Immutable;

namespace Mist.Core.OptionalLocalWorks;

public sealed record SideWellSourceRead(string EntityId,long Revision);
public sealed record SideWellEntrySafety(string ContractVersion,string ContentVersion,string MainGeometrySha256,string SideGeometrySha256,
    string LadderReceipt,string RailReceipt,string BasicReceipt,ImmutableArray<SideWellSourceRead> Reads,string Scope);
public sealed record SideWellVisit(Guid Instance,string FromPort,string ToPort,string Scene,long ArrivalClock,string SafetyReceipt);
public sealed record SideWellNoteMeaning(string DraftHash,bool StructuredAssistance,ImmutableDictionary<string,string> Fields);
public sealed record SideWellComparisonResult(Guid VisitInstance,string ObservationFingerprint,string MainFingerprint,string DraftHash,
    bool StructuredAssistance,string Comparison,string UnknownConditions,string Causality,string Sampling,int Experience,int Marks,int Parts);
