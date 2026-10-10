using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.Content;

namespace Mist.Core.NPC;

public enum KnowledgeKind { Observation,Attributed,Inference,Disputed }
public sealed record FactDefinition(string Id,string Text,string SourceRoot,string TimeScope,KnowledgeKind Kind,bool Personal,ImmutableArray<string> Release);
public sealed record ActorIdentity(string Id,string Name,string NameEnglish,string Role,string HomeScene,string? RecruitGate,ImmutableArray<string> Topics);
public sealed record KnowledgeEvidence(string FactId,string SourceRoot,string ReceiptId,string TimeScope,KnowledgeKind Kind,bool Revised=false);
public sealed record KnowledgeState(ImmutableArray<KnowledgeEvidence> Evidence);
public sealed record SafeTopic(string Id,string Text,string TimeScope,KnowledgeKind Kind,ImmutableArray<string> SourceRoots);
public sealed class KnowledgeProjection(RuntimeCatalog catalog)
{
    public const string NeutralReply="I can discuss the records you have verified. Please choose an available topic or request a source check.";
    public ImmutableDictionary<string,ActorIdentity> Actors {get;}=catalog.Definitions["npcs"].Select(d=>
    {
        using var doc=JsonDocument.Parse(d.PayloadJson);var j=doc.RootElement;
        return new ActorIdentity(d.Id,d.Name,j.GetProperty("name_en").GetString()!,j.GetProperty("role").GetString()!,j.GetProperty("home_scene").GetString()!,j.GetProperty("recruit_gate").GetString(),j.GetProperty("known_topics").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray());
    }).ToImmutableDictionary(x=>x.Id);
    public ImmutableDictionary<string,FactDefinition> Facts {get;}=ReadFacts(catalog);
    private static ImmutableDictionary<string,FactDefinition> ReadFacts(RuntimeCatalog c)
    {
        using var doc=JsonDocument.Parse(c.Definitions["npc_facts"][0].PayloadJson);
        return doc.RootElement.EnumerateArray().Select(j=>new FactDefinition(j.GetProperty("id").GetString()!,j.GetProperty("text").GetString()!,j.GetProperty("source").GetString()!,j.GetProperty("time_scope").GetString()!,j.GetProperty("kind").GetString()=="claim_attributed"?KnowledgeKind.Attributed:KnowledgeKind.Observation,j.GetProperty("privacy").GetString()=="personal",j.GetProperty("release").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray())).ToImmutableDictionary(x=>x.Id);
    }
    public ImmutableArray<SafeTopic> Topics(string actor,KnowledgeState npc,KnowledgeState player,IReadOnlySet<string> permittedPersonalFacts)
    {
        if(!Actors.ContainsKey(actor))throw new InvalidOperationException("人物身份未登记。");
        // Truth and chapter number are deliberately absent. Both people must actually know this fact.
        return Facts.Values.Where(f=>(!f.Personal||permittedPersonalFacts.Contains(f.Id))&&npc.Evidence.Any(e=>e.FactId==f.Id&&!e.Revised)&&player.Evidence.Any(e=>e.FactId==f.Id&&!e.Revised))
            .OrderBy(f=>f.Id,StringComparer.Ordinal).Select(f=>new SafeTopic(f.Id,f.Text,f.TimeScope,f.Kind,npc.Evidence.Where(e=>e.FactId==f.Id&&!e.Revised).Select(e=>e.SourceRoot).Distinct(StringComparer.Ordinal).ToImmutableArray())).ToImmutableArray();
    }
    public static int IndependentSources(KnowledgeState s,string fact)=>s.Evidence.Where(e=>e.FactId==fact&&!e.Revised).Select(e=>e.SourceRoot).Distinct(StringComparer.Ordinal).Count();
    public static string Reply(string guessedTopic,ImmutableArray<SafeTopic> topics)=>topics.SingleOrDefault(t=>t.Id==guessedTopic)?.Text??NeutralReply;
}
