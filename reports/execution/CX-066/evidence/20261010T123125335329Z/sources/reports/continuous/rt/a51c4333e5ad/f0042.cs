using System.Text.Json;
using Mist.Core.Learning;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Learning;

// Read the actual learner source and reconstruct only the corresponding reader's
// displayed English. This helper neither issues permissions nor loads author cards.
public static class RegionalLexicalSourceProjection
{
    public static (string Body,string Paragraph,string Version) Read(JsonElement document,LexicalContext context)
    {
        string path=context.SourceFile;
        if(path.EndsWith("vessel_life/learner_package.json",StringComparison.Ordinal))
        {
            var script=document.GetProperty("scripts").EnumerateArray().Single(s=>context.MaterialId.StartsWith(s.GetProperty("id").GetString()+":",StringComparison.Ordinal));
            string id=script.GetProperty("id").GetString()!;var lines=script.GetProperty("paragraphs").EnumerateArray().ToArray();bool replay=context.MaterialId==id+":replay";
            string Text(JsonElement line)=>replay?line.GetProperty("actor").GetString()+": "+line.GetProperty("en").GetString():line.GetProperty("en").GetString()!;
            var paragraph=lines.Single(p=>p.GetProperty("id").GetString()==context.ParagraphId);
            Require(context.AccessClass==(replay?"completed_replay_permission":"current_line_permission"));
            Require(replay||context.MaterialId==id+":"+context.ParagraphId);
            string body=replay?string.Join("\n\n",lines.Select(Text)):Text(paragraph);
            return(body,Text(paragraph),Version(script,document));
        }
        if(path.EndsWith("scoped_personal_work/learner_package.json",StringComparison.Ordinal))
        {
            var response=document.GetProperty("responses").EnumerateArray().Single(r=>r.GetProperty("id").GetString()==context.MaterialId);
            Require(context.ParagraphId==context.MaterialId+"-original"&&context.AccessClass=="current_received_personal_reply");
            string text=response.GetProperty("en").GetString()!;return(text,text,Version(response,document));
        }
        JsonElement material;
        if(path.EndsWith("optional_local_works/field_relay_contract.json",StringComparison.Ordinal))
        {Require(context.MaterialId=="R6-FIELD-RELAY-R-SCOPE");material=document;}
        else if(path.EndsWith("optional_local_works/settlement_support_contracts.json",StringComparison.Ordinal)||path.EndsWith("optional_local_works/old_city_life_contract.json",StringComparison.Ordinal))
        {
            string prefix=path.EndsWith("settlement_support_contracts.json",StringComparison.Ordinal)?"settlement:":"old-city-life:";
            Require(context.MaterialId.StartsWith(prefix,StringComparison.Ordinal));string id=context.MaterialId[prefix.Length..];
            material=document.GetProperty("sections").EnumerateArray().Single(s=>s.GetProperty("id").GetString()==id);
        }
        else material=document.GetProperty("materials").EnumerateArray().Single(m=>m.GetProperty("id").GetString()==context.MaterialId);
        if(material.TryGetProperty("english",out var english))
        {string text=english.GetString()!;Require(context.ParagraphId==context.MaterialId+".P1");return(text,text,Version(material,document));}
        var paragraphs=material.GetProperty("paragraphs").EnumerateArray().ToArray();
        string English(JsonElement p)=>p.TryGetProperty("en",out var en)?en.GetString()!:p.GetProperty("text").GetString()!;
        string separator=path.EndsWith("part05-Original_Materials_v0.1.json",StringComparison.Ordinal)?"\n":"\n\n";
        return(string.Join(separator,paragraphs.Select(English)),English(paragraphs.Single(p=>p.GetProperty("id").GetString()==context.ParagraphId)),Version(material,document));
    }
    private static string Version(JsonElement material,JsonElement document)
    {
        foreach(var element in new[]{material,document})
        {if(element.TryGetProperty("content_version",out var content))return content.GetString()!;if(element.TryGetProperty("version",out var version))return version.GetString()!;}
        return "0.1";
    }
}
