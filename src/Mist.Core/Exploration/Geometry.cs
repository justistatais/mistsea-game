using System.Collections.Immutable;

namespace Mist.Core.Exploration;

public readonly record struct Point2(double X, double Y)
{
    public double Distance(Point2 other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}
public readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(Point2 p, double margin = 0) => p.X >= Left + margin && p.X <= Right - margin && p.Y >= Top + margin && p.Y <= Bottom - margin;
    public bool OverlapsCircle(Point2 p, double radius) => p.Distance(new(Math.Clamp(p.X, Left, Right), Math.Clamp(p.Y, Top, Bottom))) <= radius;
}
public sealed record Port(string Id, string TargetScene, string TargetPort, Point2 Stand);
public sealed record Spot(string Id, Point2 Stand, string Purpose);
public sealed record SceneGeometry(string Id, Bounds Walk, ImmutableArray<Bounds> Blocked, ImmutableArray<Bounds> Danger, ImmutableArray<Port> Ports, ImmutableArray<Spot> Spots);
public sealed class NavigationGrid(SceneGeometry scene, double radius = 1.2)
{
    public double Radius => radius; // Internal graybox body radius, checked against all registered stand points.
    public bool CanStand(Point2 point, bool avoidDanger = false) => scene.Walk.Contains(point, radius) && !scene.Blocked.Any(b => b.OverlapsCircle(point, radius)) && (!avoidDanger || !scene.Danger.Any(d => d.OverlapsCircle(point, radius)));
    public bool SegmentClear(Point2 from, Point2 to, bool avoidDanger = false)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(from.Distance(to) / 0.4));
        for (int i = 0; i <= steps; i++) if (!CanStand(new(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps), avoidDanger)) return false;
        return true;
    }
    public ImmutableArray<Point2> FindPath(Point2 from, Point2 to, bool avoidDanger = false)
    {
        if (!CanStand(from, avoidDanger) || !CanStand(to, avoidDanger)) return [];
        if (SegmentClear(from, to, avoidDanger)) return [to];
        var start = ((int)Math.Round(from.X), (int)Math.Round(from.Y));
        var target = ((int)Math.Round(to.X), (int)Math.Round(to.Y));
        var frontier = new PriorityQueue<(int X, int Y), double>(); frontier.Enqueue(start, 0);
        var cost = new Dictionary<(int, int), double> { [start] = 0 }; var previous = new Dictionary<(int, int), (int, int)>();
        (int X, int Y)[] directions = [(1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)];
        while (frontier.TryDequeue(out var current, out _))
        {
            if (current == target)
            {
                if (!SegmentClear(new(target.Item1,target.Item2),to,avoidDanger)) return [];
                var reversed = new List<Point2> { to }; var node = target;
                while (node != start) { reversed.Add(new(node.Item1, node.Item2)); node = previous[node]; }
                reversed.Reverse();
                if (!SegmentClear(from, reversed[0], avoidDanger)) return [];
                return reversed.ToImmutableArray();
            }
            foreach (var d in directions)
            {
                var next = (current.X + d.X, current.Y + d.Y); var point = new Point2(next.Item1, next.Item2);
                if (!SegmentClear(new(current.X, current.Y), point, avoidDanger)) continue;
                double candidate = cost[current] + Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (cost.TryGetValue(next, out double known) && candidate >= known) continue;
                cost[next] = candidate; previous[next] = current; frontier.Enqueue(next, candidate + point.Distance(to));
            }
        }
        return [];
    }
    public static Point2 ScreenToWorld(Point2 screen, Bounds viewport) => new(100 * (screen.X - viewport.Left) / (viewport.Right - viewport.Left), 100 * (screen.Y - viewport.Top) / (viewport.Bottom - viewport.Top));
    public static Point2 WorldToScreen(Point2 world, Bounds viewport) => new(viewport.Left + world.X / 100 * (viewport.Right - viewport.Left), viewport.Top + world.Y / 100 * (viewport.Bottom - viewport.Top));
}
