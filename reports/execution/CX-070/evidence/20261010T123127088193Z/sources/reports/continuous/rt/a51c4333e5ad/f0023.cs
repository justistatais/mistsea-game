using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mist.Core.Contracts;
using Mist.Core.ReviewCalibration;

namespace Mist.Application.ReviewCalibration;

public sealed record AssessmentQuestion(string Id,string Type,string Prompt,ImmutableArray<string> ResponseCategories);
public sealed record AssessmentRoute(string Id,string SourceVersion,AssessmentTaskKind Kind,decimal Target,string Prompt,ImmutableArray<string> Paragraphs,string? VisualDataJson,int? SuggestedMinimumWords,ImmutableArray<AssessmentQuestion> Questions)
{public bool WorldWriteCapability=>false;public bool FullBandEnabled=>false;public bool ContentCalibrated=>false;public bool TimerEnforced=>false;public bool AudioEvidenceAvailable=>false;public string DeliveryState=>"source_text_or_script_candidate";}
public sealed record AssessmentInventoryEntry(string Id,string Version,string Modality,string Family,int EstimatedWords,string BodyHash,decimal Target,string DeclaredDifficulty,string AudioStatus,double? ActualAudioSeconds);
public sealed record AssessmentInventoryReport(string SourceHash,ImmutableArray<AssessmentInventoryEntry> Materials,ImmutableArray<string> CompleteTaskRouteIds,ImmutableArray<string> Missing)
{public bool DifficultyEmpiricallyCalibrated=>false;public bool AudioAuditionComplete=>false;public bool ActualParticipantsRegistered=>false;public bool OfficialCertification=>false;public decimal? PredictedBand=>null;public string CountsMeaning=>"source_registrations_not_played_tasks_or_calibrated_content";}
// Source inspection preserves the frozen author pack. Routes contain only original paragraphs,
// writing prompts and their table data. Answer keys, illustrative answers and forbidden-invention
// annotations remain outside the player route DTO, and are never sent to a provider.
public sealed class OfflineAssessmentInventory
{
    public AssessmentInventoryReport Report {get;}
    public ImmutableDictionary<string,AssessmentRoute> Routes {get;}
    private OfflineAssessmentInventory(AssessmentInventoryReport report,ImmutableDictionary<string,AssessmentRoute> routes){Report=report;Routes=routes;}
    public static OfflineAssessmentInventory InspectSource(string json)
    {
        using var document=JsonDocument.Parse(json);StrictJson.CheckDuplicates(document.RootElement);var root=document.RootElement;string sourceHash=CalibrationRules.Hash(json);var materials=ImmutableArray.CreateBuilder<AssessmentInventoryEntry>();var routes=ImmutableDictionary.CreateBuilder<string,AssessmentRoute>(StringComparer.Ordinal);
        foreach(var material in root.GetProperty("materials").EnumerateArray())
        {
            string id=material.GetProperty("id").GetString()!,version=material.GetProperty("version").GetString()!,modality=material.GetProperty("modality").GetString()!,family=material.GetProperty("family_id").GetString()!,difficulty=material.GetProperty("difficulty_status").GetString()!;decimal target=material.GetProperty("target_band").GetDecimal();if(target!=6.5m)throw new InvalidDataException("source_target_changed");
            var paragraphs=material.GetProperty("paragraphs").EnumerateArray().Select(p=>p.GetProperty("text").GetString()!).ToImmutableArray();string body=string.Join("\n\n",paragraphs);int words=Regex.Matches(body,"[A-Za-z]+(?:['’-][A-Za-z]+)*",RegexOptions.CultureInvariant).Count;string audio=modality=="listening"?"audio_not_inspected_no_audition_evidence":"not_applicable";
            materials.Add(new(id,version,modality,family,words,CalibrationRules.Hash(body),target,difficulty,audio,null));
            if(id is "LAB-R-001" or "LAB-L-001")
            {
                var questions=material.GetProperty("items").EnumerateArray().Select(q=>new AssessmentQuestion(q.GetProperty("id").GetString()!,q.GetProperty("type").GetString()!,q.GetProperty("prompt").GetString()!,q.GetProperty("type").GetString()=="three_way"?["supported","contradicted","not_stated"]:[])).ToImmutableArray();
                routes.Add(id,new(id,version,id=="LAB-R-001"?AssessmentTaskKind.LongReading:AssessmentTaskKind.LongListening,target,"原始独立练习材料；缺音轨时按阅读脚本记录。",paragraphs,null,null,questions));
            }
        }
        var writing=root.GetProperty("writing");foreach(string key in new[]{"q102","visual","argument"})
        {
            var task=writing.GetProperty(key);string id=task.GetProperty("id").GetString()!;decimal target=task.GetProperty("target_band").GetDecimal();if(target!=6.5m)throw new InvalidDataException("source_target_changed");var kind=key=="visual"?AssessmentTaskKind.AcademicTask1:key=="argument"?AssessmentTaskKind.AcademicTask2:AssessmentTaskKind.FunctionalShort;
            string? visual=key=="visual"?StrictJson.Serialize(new{units=task.GetProperty("data_units").GetString(),columns=task.GetProperty("columns").Clone(),rows=task.GetProperty("rows").Clone()}):null;int? min=task.TryGetProperty("minimum_words",out var minimum)?minimum.GetInt32():null;
            routes.Add(id,new(id,"source-pack-sha256:"+sourceHash,kind,target,task.GetProperty("prompt").GetString()!,[],visual,min,[]));
        }
        var complete=routes.Values.Where(r=>r.Kind!=AssessmentTaskKind.FunctionalShort).Select(r=>r.Id).Order(StringComparer.Ordinal).ToImmutableArray();var report=new AssessmentInventoryReport(sourceHash,materials.ToImmutable(),complete,["two_independent_actual_editor_reviews","actual_final_listening_audio_and_full_audition","declared_conditions_and_section_reference","consented_actual_target_group_pilot","independent_writing_raters_and_adjudication_protocol","family_holdout_and_transfer_review","current_material_question_audio_support_version_bindings","native_assessment_ui_and_observation_adapter"]);
        return new(report,routes.ToImmutable());
    }
    public AssessmentRoute OpenLocalRoute(string id)=>Routes.TryGetValue(id,out var route)?route:throw new InvalidOperationException("unknown_assessment_source_route");
}
