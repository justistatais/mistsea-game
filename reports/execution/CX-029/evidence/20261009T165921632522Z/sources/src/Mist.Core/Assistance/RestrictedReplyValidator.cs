using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mist.Core.Contracts;

namespace Mist.Core.Assistance;

// Implements the frozen Runtime_Reply_v0.1 structure and exact scoped citations. It does
// not prove semantic truth, detect arbitrary secrets in prose, or approve a world action.
public static class RestrictedReplyValidator
{
    public static string Mode(AssistancePurpose purpose)=>purpose switch{AssistancePurpose.Explain=>"explain",AssistancePurpose.Feedback=>"feedback",AssistancePurpose.IntentCandidate=>"intent_candidate",_=>throw new InvalidDataException("unknown_purpose")};
    public static CandidateValidation Validate(ScopedAssistanceRequest request,string json,int maximumBytes=65536)
    {
        try
        {
            if(Encoding.UTF8.GetByteCount(json)>maximumBytes)return new(false,"response_too_large",null);
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=16});var root=document.RootElement;StrictJson.CheckDuplicates(root);
            Object(root,"request_id","mode","status","items");string requestId=Text(root,"request_id",64,64);
            if(!Regex.IsMatch(requestId,"^[0-9a-f]{64}$",RegexOptions.CultureInvariant)||requestId!=request.RequestId||Text(root,"mode",1,32)!=Mode(request.Purpose))return new(false,"request_or_purpose_mismatch",null);
            string status=Choice(root,"status","candidate","unassessed","needs_clarification");var items=root.GetProperty("items");if(items.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("invalid_items");
            int maximum=request.Purpose==AssistancePurpose.IntentCandidate?1:8;if(items.GetArrayLength()>maximum||status=="candidate"&&items.GetArrayLength()==0||status!="candidate"&&items.GetArrayLength()!=0)throw new InvalidDataException("invalid_item_count");
            var explanations=ImmutableArray.CreateBuilder<ExplanationCandidate>();var feedback=ImmutableArray.CreateBuilder<FeedbackCandidate>();var intents=ImmutableArray.CreateBuilder<IntentCandidate>();
            foreach(var item in items.EnumerateArray())
            {
                switch(request.Purpose)
                {
                    case AssistancePurpose.Explain:
                        Object(item,"text","citation");var citation=item.GetProperty("citation");Object(citation,"asset_id","version","segment_id","start","end","quote");
                        var c=new SourceCitation(Text(citation,"asset_id",1,200),Text(citation,"version",1,200),Text(citation,"segment_id",1,200),Integer(citation,"start"),Integer(citation,"end"),Text(citation,"quote",0,1800));
                        var source=request.Sources.SingleOrDefault(s=>s.AssetId==c.AssetId&&s.Version==c.Version&&s.SegmentId==c.SegmentId&&c.Start>=s.Start&&c.End<=s.End);
                        if(source is null||c.End<=c.Start||UnicodeOffsets.Slice(source.Text,c.Start-source.Start,c.End-source.Start)!=c.Quote)throw new InvalidDataException("citation_not_in_projection");
                        explanations.Add(new(Text(item,"text",1,1800),c));break;
                    case AssistancePurpose.Feedback:
                        Object(item,"start","end","quote","category","certainty","explanation","suggestion");int start=Integer(item,"start"),end=Integer(item,"end");string quote=Text(item,"quote",0,1800);
                        if(request.Draft is null||end<=start||UnicodeOffsets.Slice(request.Draft.Text,start,end)!=quote)throw new InvalidDataException("feedback_quote_mismatch");
                        var suggestion=item.GetProperty("suggestion");if(suggestion.ValueKind is not(JsonValueKind.String or JsonValueKind.Null))throw new InvalidDataException("invalid_suggestion");
                        feedback.Add(new(start,end,quote,Choice(item,"category","meaning","organization","vocabulary","grammar","spelling","style"),Choice(item,"certainty","issue","possible","preference"),Text(item,"explanation",0,1800),suggestion.ValueKind==JsonValueKind.Null?null:Text(item,"suggestion",0,1800)));break;
                    case AssistancePurpose.IntentCandidate:
                        Object(item,"route","main_generator","beacon_plan","unresolved");var unknown=item.GetProperty("unresolved");if(unknown.ValueKind!=JsonValueKind.Array||unknown.GetArrayLength()>8)throw new InvalidDataException("invalid_unresolved");
                        var fields=ImmutableArray.CreateBuilder<string>();foreach(var field in unknown.EnumerateArray()){if(field.ValueKind!=JsonValueKind.String||field.GetString()!.EnumerateRunes().Count()>400)throw new InvalidDataException("invalid_unresolved");fields.Add(field.GetString()!);}
                        intents.Add(new(Choice(item,"route","A","B","C","unknown"),Choice(item,"main_generator","off","on","unspecified"),Choice(item,"beacon_plan","repair_then_test","test_before_departure","already_tested_claim","unspecified"),fields.ToImmutable()));break;
                    default:throw new InvalidDataException("unknown_purpose");
                }
            }
            return new(true,"structure_and_scope_only",new(requestId,request.Purpose,status,explanations.ToImmutable(),feedback.ToImmutable(),intents.ToImmutable()));
        }
        catch(Exception error) when(error is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {return new(false,"invalid_candidate",null);}
    }
    private static void Object(JsonElement element,params string[] fields)
    {
        if(element.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("invalid_object");var actual=element.EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);if(!actual.SetEquals(fields))throw new InvalidDataException("unknown_or_missing_field");
    }
    private static string Text(JsonElement element,string name,int minimum,int maximum)
    {
        var field=element.GetProperty(name);if(field.ValueKind!=JsonValueKind.String)throw new InvalidDataException("invalid_text");string text=field.GetString()!;int count=text.EnumerateRunes().Count();if(count<minimum||count>maximum)throw new InvalidDataException("text_bounds");return text;
    }
    private static int Integer(JsonElement element,string name)
    {
        var field=element.GetProperty(name);if(field.ValueKind!=JsonValueKind.Number||!field.TryGetInt32(out int value)||value<0)throw new InvalidDataException("invalid_offset");return value;
    }
    private static string Choice(JsonElement element,string name,params string[] allowed)
    {string value=Text(element,name,0,100);if(!allowed.Contains(value,StringComparer.Ordinal))throw new InvalidDataException("unknown_choice");return value;}
    public static string EscapeForHtml(string text)=>text.Replace("&","&amp;",StringComparison.Ordinal).Replace("<","&lt;",StringComparison.Ordinal).Replace(">","&gt;",StringComparison.Ordinal).Replace("\"","&quot;",StringComparison.Ordinal).Replace("'","&#39;",StringComparison.Ordinal);
}
