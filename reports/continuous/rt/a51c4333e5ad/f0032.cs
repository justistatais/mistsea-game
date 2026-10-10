using System.Collections.Immutable;

namespace Mist.Core.ReleaseReadiness;

public enum PerformanceOrigin { NativeGodotFrames,NativeApplicationOperations,ShellScriptDuration,ExplicitOfflineFixture }
public sealed record PerformanceMachine(string Os,string Cpu,string Gpu,int MemoryMiB,string Storage);
public sealed record PerformanceSeries(string Build,string Release,string Metric,string Unit,PerformanceMachine Machine,PerformanceOrigin Origin,ImmutableArray<double> Samples);
public sealed record PerformanceDistribution(int Count,double Minimum,double Median,double P95,double Maximum,bool NativeMeasurement,string? Limitation);
public static class PerformanceEvidenceRules
{
    public static PerformanceDistribution Summarize(PerformanceSeries series)
    {
        if(string.IsNullOrWhiteSpace(series.Build)||string.IsNullOrWhiteSpace(series.Release)||string.IsNullOrWhiteSpace(series.Machine.Os)||string.IsNullOrWhiteSpace(series.Machine.Cpu)||string.IsNullOrWhiteSpace(series.Machine.Gpu)||string.IsNullOrWhiteSpace(series.Machine.Storage)||series.Machine.MemoryMiB<=0||series.Samples.IsDefaultOrEmpty||series.Samples.Any(v=>!double.IsFinite(v)||v<=0))throw new InvalidDataException("invalid_performance_evidence");
        if(series.Unit!="milliseconds"||series.Metric is not("frame_time" or "menu_response" or "transaction_commit" or "draft_save"))throw new InvalidDataException("unsupported_metric_or_unit");
        if(series.Metric=="frame_time"&&series.Origin is not(PerformanceOrigin.NativeGodotFrames or PerformanceOrigin.ExplicitOfflineFixture))throw new InvalidDataException("script_duration_is_not_game_frame_time");
        if(series.Metric!="frame_time"&&series.Origin==PerformanceOrigin.NativeGodotFrames)throw new InvalidDataException("frame_series_is_not_operation_time");
        var sorted=series.Samples.Order().ToArray();double Rank(double percentile)=>sorted[Math.Clamp((int)Math.Ceiling(percentile*sorted.Length)-1,0,sorted.Length-1)];bool native=series.Origin is PerformanceOrigin.NativeGodotFrames or PerformanceOrigin.NativeApplicationOperations;
        double median=sorted[(sorted.Length-1)/2]/2+sorted[sorted.Length/2]/2;
        return new(sorted.Length,sorted[0],median,Rank(.95),sorted[^1],native,native?null:"fixture_or_script_not_native_game_performance");
    }
}
