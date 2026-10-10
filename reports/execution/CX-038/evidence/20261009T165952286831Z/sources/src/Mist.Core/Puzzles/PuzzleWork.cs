using System.Collections.Immutable;
using Mist.Core.Exploration;

namespace Mist.Core.Puzzles;

public enum EvidenceMark { Unknown, Supports, Contradicts, NotStated, NeedsChecking }
public sealed record EvidenceAnnotation(string Material,string Paragraph,EvidenceMark Mark,string Note);
public sealed record MapAnnotation(string Scene,Point2 Position,string Note);
public sealed record OrderConstraint(string Before,string After);
public static class PuzzleWork
{
    public static ImmutableArray<string> Move(ImmutableArray<string> steps,int index,int delta)
    {
        if(steps.Distinct().Count()!=steps.Length||index<0||index>=steps.Length||delta is not(-1 or 1)||index+delta<0||index+delta>=steps.Length)throw new InvalidOperationException("Invalid step movement.");
        var list=steps.ToArray();(list[index],list[index+delta])=(list[index+delta],list[index]);return list.ToImmutableArray();
    }
    public static string[] CheckOrder(ImmutableArray<string> steps,IEnumerable<OrderConstraint> constraints)
    {
        if(steps.Distinct().Count()!=steps.Length)throw new InvalidOperationException("Duplicate step.");
        return constraints.Select(c=>steps.IndexOf(c.Before)<0||steps.IndexOf(c.After)<0?"unknown_step:"+c.Before+":"+c.After:steps.IndexOf(c.Before)>=steps.IndexOf(c.After)?"order_conflict:"+c.Before+":"+c.After:null).OfType<string>().ToArray();
    }
    public static string[] CheckAllocation(IReadOnlyDictionary<string,int> quantities,IReadOnlyDictionary<string,int?> capacity)
    {
        var issues=new List<string>();
        foreach(var item in quantities)
        {
            if(item.Value<0)issues.Add("negative_quantity:"+item.Key);
            else if(!capacity.TryGetValue(item.Key,out int? available)||available is null)issues.Add("capacity_unknown:"+item.Key);
            else if(item.Value>available)issues.Add("capacity_exceeded:"+item.Key);
        }
        return issues.ToArray();
    }
    public static bool CanAnnotate(MapAnnotation mark,IReadOnlyDictionary<string,SceneGeometry> scenes)=>scenes.TryGetValue(mark.Scene,out var scene)&&scene.Walk.Contains(mark.Position)&&!string.IsNullOrWhiteSpace(mark.Note);
}
