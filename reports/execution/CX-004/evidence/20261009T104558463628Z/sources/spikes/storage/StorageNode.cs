using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public partial class StorageNode : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        try
        {
            if (args.Contains("--native-diagnostic"))
            {
                using var connection = StorageHarness.Open(":memory:");
                GD.Print("MISTSEA_NATIVE_AVAILABLE");
                GetTree().Quit();
                return;
            }
            var output = System.Environment.GetEnvironmentVariable("MISTSEA_PROBE_OUTPUT") ?? Path.Combine(OS.GetUserDataDir(), "storage-probe", Guid.NewGuid().ToString("N"));
            var report = StorageHarness.Run(Path.Combine(output, "sqlite"));
            File.WriteAllText(Path.Combine(output, "storage_result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MISTSEA_STORAGE_RESULTS " + JsonSerializer.Serialize(report));
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr("MISTSEA_STORAGE_ERROR " + error);
            GetTree().Quit(args.Contains("--native-diagnostic") ? 3 : 1);
        }
    }
}
