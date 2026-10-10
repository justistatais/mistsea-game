using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.Content;
using Mist.Core.Progression;

namespace Mist.Core.Skills;

public sealed record TalentDefinition(string Id,string Name,string Tree,bool Active,int MinimumLevel,int RequiredTreePoints,ImmutableArray<string> Prerequisites,bool Capstone,int Ap,int Focus,int Cooldown);
public sealed record BuildState(Attributes Attributes,ImmutableArray<string> Learned,ImmutableArray<string> Active);
public static class TalentRules
{
    public static ImmutableDictionary<string,TalentDefinition> Read(RuntimeCatalog catalog)=>catalog.Definitions["talents"].Select(d=>
    {
        using var doc=JsonDocument.Parse(d.PayloadJson);var j=doc.RootElement;if(j.GetProperty("point_cost").GetInt32()!=1||j.GetProperty("max_rank").GetInt32()!=1)throw new InvalidDataException("Talent budget changed.");
        bool active=j.GetProperty("kind").GetString()=="active";
        return new TalentDefinition(d.Id,d.Name,j.GetProperty("tree").GetString()!,active,j.GetProperty("minimum_level").GetInt32(),j.GetProperty("tree_points_before").GetInt32(),j.GetProperty("prerequisites").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray(),j.GetProperty("capstone").GetBoolean(),active?j.GetProperty("ap").GetInt32():0,active?j.GetProperty("focus").GetInt32():0,active?j.GetProperty("cooldown_personal_turns").GetInt32():0);
    }).ToImmutableDictionary(t=>t.Id);
    public static void Validate(int level,BuildState build,IReadOnlyDictionary<string,TalentDefinition> definitions)
    {
        build.Attributes.Validate(level);var names=build.Learned.ToHashSet(StringComparer.Ordinal);
        if(names.Count!=build.Learned.Length||names.Count>ProgressionRules.SkillBudget(level)||names.Any(n=>!definitions.ContainsKey(n)))throw new InvalidOperationException("技能未知、重复或超出点数。");
        if(build.Active.Length>4||build.Active.Distinct().Count()!=build.Active.Length||build.Active.Any(n=>!names.Contains(n)||!definitions[n].Active)||build.Active.Count(n=>definitions[n].Capstone)>1)throw new InvalidOperationException("主动槽非法；最多四个和一个终阶主动。");
        var acquired=new HashSet<string>();var pending=names.ToHashSet();
        while(pending.Count>0)
        {
            bool progress=false;foreach(string id in pending.Order(StringComparer.Ordinal).ToArray())
            {
                var d=definitions[id];if(level<d.MinimumLevel||d.Prerequisites.Any(p=>!acquired.Contains(p))||acquired.Count(p=>definitions[p].Tree==d.Tree)<d.RequiredTreePoints)continue;
                acquired.Add(id);pending.Remove(id);progress=true;
            }
            if(!progress)throw new InvalidOperationException("等级、直接前置或先前树内投入未满足。");
        }
    }
}
