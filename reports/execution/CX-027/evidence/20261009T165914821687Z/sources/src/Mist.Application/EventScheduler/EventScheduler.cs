using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Application.EventScheduler;

public interface IEventLeaseStore
{
    ValueTask<bool> TryLeasesAsync(LoadContext load,string eventId,ImmutableArray<string> resources);
    ValueTask ReleaseLeasesAsync(LoadContext load,string eventId);
}
public enum EventLifecycle { Queued,Running,Paused,Completed,Cancelled }
public enum ScheduleBoundary { Safe,Reading,Writing,LanguageHelp,Confirmation,CombatDecision }
public sealed record EventDefinition(string Id,string Version,string UniqueKey,int Priority,ImmutableArray<string> Resources,string SafeEntry,int Seed,string? StageExpiry=null);
public sealed record EventSession(string Instance,EventDefinition Definition,EventLifecycle State,string Node,string? CancelReason=null,LoadContext? ActiveLoad=null);
public sealed class EventScheduler(IAdventureRepository repository,IEventLeaseStore leases)
{
    private static string Entity(string instance)=>"event:"+instance;
    private static string Stable(EventDefinition d)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(d.Id+":"+d.Version+":"+d.UniqueKey+":"+d.Seed))).ToLowerInvariant();
    private async ValueTask<CommandResult> Write(BranchSnapshot s,EventSession state,string action)=>await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"event:"+state.Instance+":"+action+":"+(BranchOperations.Entity(s,Entity(state.Instance))?.Revision??-1),action,[BranchOperations.Change(s,Entity(state.Instance),"event",state)],[Entity(state.Instance)],new{state.Instance,state.Node}));
    public async ValueTask<EventSession> QueueAsync(EventDefinition d)
    {
        if(string.IsNullOrWhiteSpace(d.Id)||string.IsNullOrWhiteSpace(d.Version)||string.IsNullOrWhiteSpace(d.UniqueKey)||d.Resources.Distinct().Count()!=d.Resources.Length||d.Resources.Any(r=>string.IsNullOrWhiteSpace(r)||r=="input:foreground"||r.StartsWith("NPC:",StringComparison.Ordinal)))throw new InvalidOperationException("事件需要稳定版本、发生键和无重复的占用声明；人物使用原始持久ID，前景由调度器统一占用。");
        var s=await repository.SnapshotAsync();string instance=Stable(d);var existing=BranchOperations.Read<EventSession>(s,Entity(instance));if(existing is not null)return existing;
        var queued=new EventSession(instance,d,EventLifecycle.Queued,"entry");var r=await Write(s,queued,"queue_event");if(r.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))throw new InvalidOperationException("事件排队未提交。");return queued;
    }
    public async ValueTask<EventSession?> StartNextAsync(ScheduleBoundary boundary,string? explicitInstance=null)
    {
        if(boundary!=ScheduleBoundary.Safe)return null;var s=await repository.SnapshotAsync();BranchOperations.Safe(s);
        var queue=s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Where(e=>e.State is EventLifecycle.Queued or EventLifecycle.Paused||e.State==EventLifecycle.Running&&e.ActiveLoad!=repository.Session.Load);
        if(explicitInstance is not null)queue=queue.Where(e=>e.Instance==explicitInstance);
        foreach(var candidate in queue.OrderByDescending(e=>e.Definition.Priority).ThenBy(e=>e.Instance,StringComparer.Ordinal))
        {
            var load=repository.Session.Load;var resources=candidate.Definition.Resources.Add("input:foreground");
            if(!await leases.TryLeasesAsync(load,candidate.Instance,resources))continue;
            try{var running=candidate with{State=EventLifecycle.Running,ActiveLoad=load};var result=await Write(s,running,"start_event");if(result.Status==CommitStatus.Committed)return running;}
            catch{await leases.ReleaseLeasesAsync(load,candidate.Instance);throw;}
            await leases.ReleaseLeasesAsync(load,candidate.Instance);
        }
        return null;
    }
    public async ValueTask<CommandResult> CheckpointAsync(string instance,string node,bool pause=false,bool complete=false)
    {
        if(string.IsNullOrWhiteSpace(node))throw new InvalidOperationException("对话检查点未登记。");var s=await repository.SnapshotAsync();var old=BranchOperations.Read<EventSession>(s,Entity(instance))??throw new InvalidOperationException("事件不存在。");
        if(old.State!=EventLifecycle.Running||old.ActiveLoad!=repository.Session.Load)throw new InvalidOperationException("该实例当前未取得前景。");var load=repository.Session.Load;
        var next=old with{Node=node,State=complete?EventLifecycle.Completed:pause?EventLifecycle.Paused:EventLifecycle.Running};var r=await Write(s,next,complete?"complete_event":pause?"pause_event":"event_checkpoint");
        if(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted){if(pause||complete)await leases.ReleaseLeasesAsync(load,instance);}return r;
    }
    public async ValueTask CleanStageAsync(string stage)
    {
        var s=await repository.SnapshotAsync();foreach(var old in s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Where(e=>e.Definition.StageExpiry==stage&&e.State is not(EventLifecycle.Completed or EventLifecycle.Cancelled)))
        {var r=await Write(await repository.SnapshotAsync(),old with{State=EventLifecycle.Cancelled,CancelReason="declared_stage_boundary"},"cancel_expired_event");if(r.Status==CommitStatus.Committed)await leases.ReleaseLeasesAsync(repository.Session.Load,old.Instance);}
    }
}
