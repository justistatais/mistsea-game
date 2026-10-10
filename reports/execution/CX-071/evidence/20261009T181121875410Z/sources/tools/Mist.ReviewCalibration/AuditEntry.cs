using System.Text;
using Mist.Application.ReviewCalibration;
using Mist.Core.Contracts;
using Mist.Core.ReviewCalibration;

namespace Mist.ReviewCalibration;

internal static class AuditEntry
{
    // This command only reads a named local file and emits JSON. No release ledger changes,
    // human signoff, network, model request, automatic band estimate or world writes exist.
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=2||args[0] is not("audit-source" or "audit-protocol")){Console.Error.WriteLine("Usage: Mist.ReviewCalibration audit-source|audit-protocol <local-json-file>");return 2;}
            string path=Path.GetFullPath(args[1]);if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("local_audit_input_too_large");string json=File.ReadAllText(path,new UTF8Encoding(false,true));
            if(args[0]=="audit-source"){Console.WriteLine(StrictJson.Serialize(OfflineAssessmentInventory.InspectSource(json).Report));return 0;}
            var protocol=CalibrationRules.ParseProtocol(json);var report=CalibrationRules.Inspect(protocol,[]);Console.WriteLine(StrictJson.Serialize(report));return report.Missing.Length==0?0:3;
        }
        catch(Exception error) when(error is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException){Console.Error.WriteLine("LOCAL_REVIEW_AUDIT_REJECTED: "+error.Message);return 1;}
    }
}
