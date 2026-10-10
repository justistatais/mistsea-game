using System.Text.Json;

namespace Mist.Tests;

public static class TestRegistry
{
    private static readonly List<(string Task, string Name, Action Run)> Cases = new();
    public static void Add(string task, string name, Action action) => Cases.Add((task, name, action));
    public static void Require(bool condition, string message = "assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    public static int Run(string[] args)
    {
        if (args.Contains("--fail-probe")) { Console.Error.WriteLine("intentional test runner failure"); return 7; }
        string? selected = args.FirstOrDefault(a => a.StartsWith("--task="))?.Split('=', 2)[1];
        var results = new List<object>();
        int failed = 0;
        foreach (var test in Cases.Where(c => selected is null || c.Task == selected))
        {
            try { test.Run(); results.Add(new { task = test.Task, name = test.Name, result = "passed" }); }
            catch (Exception error) { failed++; results.Add(new { task = test.Task, name = test.Name, result = "failed", detail = error.ToString() }); }
        }
        using var native = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"); native.Open();
        Console.WriteLine(JsonSerializer.Serialize(new { scope = "executed C# code tests", executed = results.Count, failed, results,
            sqlite_version = native.ServerVersion, dotnet_runtime = System.Environment.Version.ToString(),
            process_architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), human_signoff = (string?)null }, new JsonSerializerOptions { WriteIndented = true }));
        return results.Count == 0 ? 2 : failed == 0 ? 0 : 1;
    }
}
