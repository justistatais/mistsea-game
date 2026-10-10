using System.Collections.Immutable;

namespace Mist.Core.NPC;

public enum WorkRelationship { Unestablished,Verified,Ongoing,RepairPending }
public enum PrivateRelationship { NotInvited,Invited,Paused }
public enum IssuePosition { Unknown,Support,Reserved,Oppose }
public enum RelationshipAction { VerifiedWork,MutualOngoing,PrivateInvitation,PrivatePause,ConfirmedBreach,AcceptedRepair,IssueStatement }
public sealed record RelationshipEvent(string Id,string Actor,RelationshipAction Action,string SourceReceipt,string? Work=null,string? RepairOf=null,string? Issue=null,IssuePosition Position=IssuePosition.Unknown);
public sealed record RelationshipState(WorkRelationship Work,PrivateRelationship Private,ImmutableDictionary<string,IssuePosition> Issues,ImmutableArray<RelationshipEvent> History)
{
    public static RelationshipState Empty=>new(WorkRelationship.Unestablished,PrivateRelationship.NotInvited,ImmutableDictionary<string,IssuePosition>.Empty,[]);
    public RelationshipState Apply(RelationshipEvent e)
    {
        if(History.Any(h=>h.Id==e.Id))return this;
        if(e.Action==RelationshipAction.AcceptedRepair&&!History.Any(h=>h.Id==e.RepairOf&&h.Action==RelationshipAction.ConfirmedBreach))throw new InvalidOperationException("修复必须引用仍保留的实际失约事实。");
        if(e.Action==RelationshipAction.IssueStatement&&string.IsNullOrWhiteSpace(e.Issue))throw new InvalidOperationException("立场需要具体议题。");
        var history=History.Add(e);bool unresolved=history.Where(h=>h.Action==RelationshipAction.ConfirmedBreach).Any(b=>!history.Any(r=>r.Action==RelationshipAction.AcceptedRepair&&r.RepairOf==b.Id));
        return this with
        {
            Work=unresolved?WorkRelationship.RepairPending:e.Action switch{RelationshipAction.VerifiedWork=>WorkRelationship.Verified,RelationshipAction.MutualOngoing=>WorkRelationship.Ongoing,RelationshipAction.AcceptedRepair=>WorkRelationship.Verified,_=>Work},
            Private=e.Action switch{RelationshipAction.PrivateInvitation=>PrivateRelationship.Invited,RelationshipAction.PrivatePause=>PrivateRelationship.Paused,_=>Private},
            Issues=e.Action==RelationshipAction.IssueStatement?Issues.SetItem(e.Issue!,e.Position):Issues,History=history
        };
    }
}
