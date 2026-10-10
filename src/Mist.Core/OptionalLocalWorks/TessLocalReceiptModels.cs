using System.Collections.Immutable;

namespace Mist.Core.OptionalLocalWorks;

// A projection of work already settled by the chapter producer, never a second reward owner.
public sealed record TessLocalReceiptProof(string Node,Guid NodeInstance,string Outcome,string ChapterResolution,
    string MainResolution,Guid MainQuestInstance,string InvitationSource,string ContentVersion,ImmutableArray<string> ActualSources,
    string? PlanReceipt,Guid? SegmentInstance,ImmutableArray<string> ArrivalReceipts,ImmutableArray<string> BoxSerials);
public sealed record TessLocalReceiptProjection(TessLocalReceiptProof Proof,string InvitationReceipt,string? TransportReceipt,
    string WorkReceipt,string ResultReceipt,int RewardDelta,long ItemDelta,bool MainWorldChanged);
