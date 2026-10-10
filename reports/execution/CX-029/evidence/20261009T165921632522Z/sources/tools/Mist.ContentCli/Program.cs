using Mist.ContentCli.Validation;
using Mist.Core.Contracts;

try
{
    if (args.Length < 3 || args[0] != "compile")
    {
        Console.Error.WriteLine("Usage: Mist.ContentCli compile <project-root> <output-directory> [--formal]"); return 2;
    }
    var result = ContentCompiler.Compile(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), "mistsea-internal-0.1", args.Contains("--formal"));
    Console.WriteLine(StrictJson.Serialize(result));
    return result.Status == "formal_release_blocked" ? 3 : 0;
}
catch (Exception error) { Console.Error.WriteLine("CONTENT_REJECTED: " + error); return 1; }
