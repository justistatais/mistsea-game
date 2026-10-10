using System.Collections.Immutable;

namespace Mist.Core.OptionalLocalWorks;

// The local owner and serials are declared original runtime details, not additional canonical NPCs.
public sealed record MusicBoxWorkState
{
    public required string ContentVersion {get;init;}
    public required string SourceFingerprint {get;init;}
    public required Guid Instance {get;init;}
    public string Invitation {get;init;}="unasked";
    public long PermissionVersion {get;init;}
    public bool Inspected {get;init;}
    public string? SelectedPart {get;init;}
    public long? ApprovedPermission {get;init;}
    public bool Isolated {get;init;}
    public bool Fitted {get;init;}
    public long Configuration {get;init;}
    public long? TestedConfiguration {get;init;}
    public bool Returned {get;init;}
    public bool Completed {get;init;}
}
public sealed record MusicBoxOwnerPermission(Guid WorkInstance,string Owner,string Scope,long Version,bool ReadSpecification,bool RepairMechanism,bool PublishMemory,string SourceFingerprint);
public sealed record MusicBoxApproval(Guid WorkInstance,string Owner,string Part,string PartSerial,long PermissionVersion,string SourceFingerprint);
public sealed record MusicBoxTurningCheck(Guid WorkInstance,string BoxSerial,string FittedPartSerial,int ActualTeeth,int RequiredTeeth,long Configuration,string CheckScope,bool Compatible);
public sealed record MusicBoxNoteMeaning(string? DraftHash,bool StructuredAssistance,ImmutableDictionary<string,string> Fields);
public sealed record MusicBoxRepairDelivery(Guid WorkInstance,string BoxSerial,string PartSerial,string Owner,long Configuration,string SpecificationFingerprint,string TurningCheckReceipt,string ApprovalReceipt,string DraftHash,bool StructuredAssistance,string Outcome,string MemoryDisposition,int Experience,int Marks,int Parts);
