using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.NPC;
using Mist.Core.Progression;

namespace Mist.Application.Roster;

public enum Membership { Contact,Invited,Joined }
public enum ActorAvailability { LocalContact,ShipStandby,Working,PendingSafeDeparture,Unavailable }
public sealed record RosterEntry(string Actor,Membership Membership,ActorAvailability Availability,string Location,ImmutableDictionary<int,string> Tactics,string? LastDecision=null);
public sealed record PartyFormation(ImmutableArray<string> Helpers);
public sealed record RosterView(RosterEntry Entry,bool InvitationAvailable,int SharedLevel,ImmutableArray<int> AvailableTactics);
public sealed class RosterService(IAdventureRepository repository,RuntimeCatalog catalog)
{
    private readonly KnowledgeProjection _identities=new(catalog);
    public static readonly string[] Auxiliary=["AUX-GUARD","AUX-SUPPORT"];
    private ActorIdentity Identity(string actor)=>_identities.Actors.TryGetValue(actor,out var p)&&p.Role=="companion"?p:throw new InvalidOperationException("该人物没有正式同伴身份。");
    private RosterEntry Read(BranchSnapshot s,string actor)=>BranchOperations.Read<RosterEntry>(s,"roster:"+actor)??new(actor,Membership.Contact,ActorAvailability.LocalContact,Identity(actor).HomeScene,ImmutableDictionary<int,string>.Empty);
    public async ValueTask<ImmutableArray<RosterView>> ViewAsync()
    {
        var s=await repository.SnapshotAsync();int level=ProgressionRules.Level(s.Balances.GetValueOrDefault("experience"));return _identities.Actors.Values.Where(p=>p.Role=="companion").OrderBy(p=>p.Id).Select(p=>
        {
            var entry=Read(s,p.Id);bool posted=s.Entities.Any(e=>e.Id=="workpost:"+p.Id&&e.Kind=="work_post"||e.Id=="duty:"+p.Id&&e.Kind=="operator_duty");
            if(entry.Membership==Membership.Joined&&entry.Availability==ActorAvailability.ShipStandby&&posted)entry=entry with{Availability=ActorAvailability.Working};
            return new RosterView(entry,BranchOperations.Receipt(s,p.RecruitGate!)&&entry.Membership!=Membership.Joined,level,new[]{12,20,24}.Where(n=>n<=level).ToImmutableArray());
        }).ToImmutableArray();
    }
    private async ValueTask<CommandResult> Write(BranchSnapshot s,RosterEntry next,string action,ImmutableArray<ItemChange> items=default,PartyFormation? formation=null)
    {
        BranchOperations.Safe(s);var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"roster:"+next.Actor,"roster",next));if(formation is not null)changes.Add(BranchOperations.Change(s,"party:formation","formation",formation));
        var reads=s.Entities.Where(e=>e.Kind=="combat"||e.Id=="actor"||e.Kind is "work_post" or "operator_duty").Select(e=>e.Id).Concat(new[]{"combat:workshop","party:formation","workpost:"+next.Actor,"duty:"+next.Actor}).ToArray();
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"roster:"+next.Actor+":"+action+":"+(BranchOperations.Entity(s,"roster:"+next.Actor)?.Revision??-1),action,changes.ToImmutable(),reads,new{actor=next.Actor,next.LastDecision},items));
    }
    public async ValueTask<CommandResult> InviteAsync(string actor)
    {
        var s=await repository.SnapshotAsync();var p=Identity(actor);if(!BranchOperations.Receipt(s,p.RecruitGate!))throw new InvalidOperationException("当前正式邀请时点尚未成立。");var old=Read(s,actor);if(old.Membership==Membership.Joined)throw new InvalidOperationException("人物已经加入。");return await Write(s,old with{Membership=Membership.Invited,LastDecision="invitation"},"invite_companion");
    }
    public async ValueTask<CommandResult> DecideAsync(string actor,bool join)
    {
        var s=await repository.SnapshotAsync();var old=Read(s,actor);if(old.Membership!=Membership.Invited)throw new InvalidOperationException("需要当前有效的正式邀请。");
        if(old.Availability is ActorAvailability.Working or ActorAvailability.Unavailable)throw new InvalidOperationException("人物正在承担现场工作，先完成真实交接。");
        return await Write(s,old with{Membership=join?Membership.Joined:Membership.Contact,Availability=join?ActorAvailability.ShipStandby:ActorAvailability.LocalContact,Location=join?"HUB-S01":Identity(actor).HomeScene,LastDecision=join?"accepted":"declined"},join?"accept_invitation":"decline_invitation");
    }
    public async ValueTask<CommandResult> ChooseTacticsAsync(string actor,ImmutableDictionary<int,string> choices)
    {
        var s=await repository.SnapshotAsync();var old=Read(s,actor);int level=ProgressionRules.Level(s.Balances.GetValueOrDefault("experience"));if(old.Membership!=Membership.Joined||choices.Any(p=>p.Key is not(12 or 20 or 24)||p.Key>level||p.Value is not("A" or "B")))throw new InvalidOperationException("只有已加入同伴能使用当前共享等级开放的战术选择。");
        return await Write(s,old with{Tactics=choices},"configure_companion_tactics");
    }
    public ImmutableArray<string> Helpers(BranchSnapshot s)
    {
        var chosen=BranchOperations.Read<PartyFormation>(s,"party:formation")?.Helpers??[];if(chosen.Length>2||chosen.Distinct().Count()!=chosen.Length)throw new InvalidOperationException("队伍名册不合法。");
        var available=chosen.Where(id=>Auxiliary.Contains(id)||Read(s,id) is{Membership:Membership.Joined,Availability:ActorAvailability.ShipStandby}&&!s.Entities.Any(e=>e.Kind=="work_post"&&e.Id=="workpost:"+id||e.Kind=="operator_duty"&&e.Id=="duty:"+id)).ToImmutableArray();
        return available.Concat(Auxiliary.Where(id=>!available.Contains(id))).Take(2).ToImmutableArray();
    }
    public async ValueTask<CommandResult> SelectAsync(ImmutableArray<string> helpers)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);if(helpers.Length>2||helpers.Distinct().Count()!=helpers.Length)throw new InvalidOperationException("最多选择两个不同援助人。");
        foreach(string id in helpers.Where(id=>!Auxiliary.Contains(id)))if(Read(s,id) is not{Membership:Membership.Joined,Availability:ActorAvailability.ShipStandby}||s.Entities.Any(e=>e.Id=="workpost:"+id&&e.Kind=="work_post"||e.Id=="duty:"+id&&e.Kind=="operator_duty"))throw new InvalidOperationException("人物当前不能出战。");
        var formation=new PartyFormation(helpers);return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"formation:"+Guid.NewGuid(),"select_party_helpers",[BranchOperations.Change(s,"party:formation","formation",formation)],s.Entities.Where(e=>e.Kind is "combat" or "roster" or "work_post" or "operator_duty").Select(e=>e.Id).Concat(helpers.SelectMany(id=>new[]{"workpost:"+id,"duty:"+id})).Append("combat:workshop").ToArray(),new{helpers}));
    }
    public async ValueTask<CommandResult> RequestDepartureAsync(string actor)
    {
        var s=await repository.SnapshotAsync();var old=Read(s,actor);if(old.Membership!=Membership.Joined)throw new InvalidOperationException("人物当前没有随队身份。");return await Write(s,old with{Availability=ActorAvailability.PendingSafeDeparture},"request_safe_departure");
    }
    public async ValueTask<CommandResult> FinishDepartureAsync(string actor)
    {
        var s=await repository.SnapshotAsync();var old=Read(s,actor);if(old.Availability!=ActorAvailability.PendingSafeDeparture)throw new InvalidOperationException("尚无待确认的安全离队请求。");
        if(s.Entities.Any(e=>e.Id=="workpost:"+actor&&e.Kind=="work_post"||e.Id=="duty:"+actor&&e.Kind=="operator_duty"))throw new InvalidOperationException("先交接尚未结束的真实工作岗位与值守职责。");
        var returned=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==actor).Select(i=>new ItemChange(i.Id,i.DefinitionId,i.OwnerKind,actor,"player","hero",null)).ToImmutableArray();
        var selected=BranchOperations.Read<PartyFormation>(s,"party:formation")??new([]);return await Write(s,old with{Membership=Membership.Contact,Availability=ActorAvailability.LocalContact,Location=Identity(actor).HomeScene,LastDecision="safe_departure"},"finish_safe_departure",returned,selected with{Helpers=selected.Helpers.Remove(actor)});
    }
}
