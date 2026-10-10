using Godot;
using Mist.Application;
using Mist.Application.Storage;
using Mist.Core.Contracts;

public partial class Root
{
    private readonly List<double> _performanceFrameMs=new();
    private long? _performancePreviousTick;
    private bool _performanceRecording;
    private void CapturePerformanceFrame()
    {
        if(!_performanceRecording)return;long now=System.Diagnostics.Stopwatch.GetTimestamp();if(_performancePreviousTick is{} before)_performanceFrameMs.Add((now-before)*1000d/System.Diagnostics.Stopwatch.Frequency);_performancePreviousTick=now;
    }
    private async Task RunPerformanceCandidate()
    {
        string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??throw new InvalidOperationException("Performance candidate requires an isolated output directory.");
        int failures=0;var writes=new List<object>();
        try
        {
            if(DisplayServer.GetName()=="headless"||System.Environment.GetEnvironmentVariable("MISTSEA_PERFORMANCE_PROFILE")!="1")throw new InvalidOperationException("Native performance candidate requires an explicitly isolated native profile.");
            for(int i=0;i<30;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if((await _store.SnapshotAsync()).Balances.GetValueOrDefault("experience")!=0)throw new InvalidOperationException("Performance candidate must use a fresh zero-XP campaign profile.");
            string longText=string.Join(" ",Enumerable.Repeat("neutral benchmark word",3000));await _campaignClient!.ShowPerformanceText(longText);
            _performanceFrameMs.Clear();_performancePreviousTick=null;_performanceRecording=true;
            var duration=System.Diagnostics.Stopwatch.StartNew();
            for(int task=0;task<10;task++)
            {
                string id="performance-fixture:"+task;DraftView? head=null;
                for(int revision=0;revision<2;revision++)
                {
                    var session=_store.Session;var wall=System.Diagnostics.Stopwatch.StartNew();var enqueue=System.Diagnostics.Stopwatch.StartNew();
                    var pending=_store.SaveAsync(new(session.Profile,session.Branch,session.Load,id,head?.Id,head?.Revision??0,longText+" revision "+revision,"explicit-neutral-performance-fixture-1"));double enqueueMs=enqueue.Elapsed.TotalMilliseconds;var ack=await pending;head=await _store.GetDraftAsync(id);
                    writes.Add(new{task,revision=ack.Revision,enqueue_ms=enqueueMs,total_ack_ms=wall.Elapsed.TotalMilliseconds,body_chars=head?.Body?.Length});
                    if(head?.Body!=longText+" revision "+revision)throw new InvalidDataException("Acknowledged performance draft body differs.");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            while(duration.Elapsed.TotalSeconds<8)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            _performanceRecording=false;if(!await _campaignClient!.SaveBeforeClose())throw new InvalidOperationException("Native editor close-save did not acknowledge.");var snapshot=await _store.SnapshotAsync();
            var sorted=_performanceFrameMs.Order().ToArray();if(sorted.Length<60)throw new InvalidOperationException("Too few actual native frames for an observation.");
            long files=Directory.EnumerateFiles(_store.RootPath,"*.sqlite",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
            var result=new{scope="native first scene and 9000-word editor with 20 acknowledged SQLite fixture draft revisions; candidate measurement",build=BuildIdentity.Version,platform=OS.GetName(),renderer=DisplayServer.GetName(),processor_count=System.Environment.ProcessorCount,runtime=System.Environment.Version.ToString(),frames=sorted.Length,mean_frame_interval_ms=sorted.Average(),p95_frame_interval_ms=sorted[(int)Math.Floor((sorted.Length-1)*.95)],p99_frame_interval_ms=sorted[(int)Math.Floor((sorted.Length-1)*.99)],observed_frame_cadence_fps=1000d/sorted.Average(),working_set_bytes=System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,managed_heap_bytes=GC.GetTotalMemory(false),sqlite_bytes=files,writes,retained_fixture_heads=snapshot.DraftHeads.Count(h=>h.TaskId.StartsWith("performance-fixture:",StringComparison.Ordinal)),experience=snapshot.Balances.GetValueOrDefault("experience"),not_run=new[]{"78 playable scenes on-demand loading","long native NPC lease session","final release build benchmark","physical power loss","human performance acceptance"},human_signoff=(string?)null};
            Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"native_performance.json"),StrictJson.Serialize(result));GD.Print("MISTSEA_NATIVE_PERFORMANCE frames="+sorted.Length);
        }
        catch(Exception error){failures++;Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"native_performance_failure.json"),error.ToString());GD.PrintErr("MISTSEA_NATIVE_PERFORMANCE_ERROR "+error);}
        finally{_performanceRecording=false;}
        GetTree().Quit(failures==0?0:1);
    }
}
