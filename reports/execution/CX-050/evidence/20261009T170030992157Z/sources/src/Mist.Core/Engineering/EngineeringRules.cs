using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.Content;

namespace Mist.Core.Engineering;

public enum WorkType { BaselineCheck,EnhancedInstall,TransitionSupply,OperatorAssignment,FieldTrial }
public enum WorkStage { Planned,Reserved,InTransit,Delivered,Installed,Verified,Cancelled }
public sealed record EngineeringSource(string Id,string Resource,int Quantity,int RequiredSteps,string Region);
public sealed record TowerWork(string Preparation,string Tower,WorkType Type,string Configuration,WorkStage Stage,ImmutableDictionary<string,int> Resources,string Crew,string[] Operators,int Step,string? Receipt=null);
public sealed record TowerReadiness(string Tower,bool Safe,bool Baseline,string? EnhancedVersion,bool Transition,bool Operators,bool Trial,string? TrialVersion);
public sealed class EngineeringRules
{
    public ImmutableDictionary<string,EngineeringSource> Sources {get;}
    public ImmutableDictionary<string,ImmutableDictionary<string,int>> Baseline {get;}
    public EngineeringRules(RuntimeCatalog catalog)
    {
        using var doc=JsonDocument.Parse(catalog.Definitions["progression_engineering"][0].PayloadJson);var j=doc.RootElement;
        var sources=j.GetProperty("procurement").EnumerateArray().Select(s=>new EngineeringSource(s.GetProperty("id").GetString()!,s.GetProperty("resource").GetString()!,s.GetProperty("quantity").GetInt32(),s.GetProperty("steps").GetArrayLength(),s.GetProperty("id").GetString() switch{"SUP-CTRL" or "SUP-TEST"=>"R4","SUP-COUP"=>"R3","SUP-PORT"=>"R1","SUP-GROVE"=>"R2","SUP-FRONTIER"=>"R6",_=>throw new ContentException("Unknown finite engineering source.")})).ToList();
        Baseline=j.GetProperty("towers").EnumerateArray().ToImmutableDictionary(t=>t.GetProperty("id").GetString()!,t=>t.GetProperty("base_requirement").EnumerateObject().ToImmutableDictionary(p=>p.Name,p=>p.Value.GetInt32()));
        foreach(var tower in Baseline)foreach(var resource in tower.Value)sources.Add(new("BASE-"+tower.Key+"-"+resource.Key,resource.Key,resource.Value,2,tower.Key[4..]));
        Sources=sources.ToImmutableDictionary(s=>s.Id);
    }
    public ImmutableDictionary<string,int> Required(string tower,WorkType type)
    {
        if(!Baseline.ContainsKey(tower))throw new InvalidOperationException("塔站未登记。");return type switch{WorkType.BaselineCheck=>Baseline[tower],WorkType.EnhancedInstall=>new Dictionary<string,int>{{"controller",1},{"coupler",2}}.ToImmutableDictionary(),WorkType.TransitionSupply=>new Dictionary<string,int>{{"transition_crate",3}}.ToImmutableDictionary(),WorkType.FieldTrial=>new Dictionary<string,int>{{"trial_fixture",1}}.ToImmutableDictionary(),WorkType.OperatorAssignment=>ImmutableDictionary<string,int>.Empty,_=>throw new InvalidOperationException("Unknown work type.")};
    }
    public static string Available(string resource)=>"engineering:available:"+resource;
    public static string Held(string key,string resource)=>"engineering:held:"+key+":"+resource;
    public static string Installed(string tower,string resource)=>"engineering:installed:"+tower+":"+resource;
}
