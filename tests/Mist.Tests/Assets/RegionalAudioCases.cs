using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.Assets;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Assets;

public static class RegionalAudioCases
{
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private static RegionalAudioBinding[] Bindings()=>StrictJson.Deserialize<RegionalAudioBinding[]>(File.ReadAllText(Path.Combine(Root,"game/content/audio/regional_generation.json")));
    private static (string Version,ImmutableArray<ChapterParagraph> Paragraphs) Material(string id)
    {
        foreach(string directory in new[]{"r1_life","chapter_two","chapter_three","chapter_four","chapter_five","chapter_six","finale"})
        {
            using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"content/source",directory,"learner_package.json")));
            foreach(var m in source.RootElement.GetProperty("materials").EnumerateArray())if(m.GetProperty("id").GetString()==id)
                return(m.GetProperty("content_version").GetString()!,m.GetProperty("paragraphs").EnumerateArray().Select(p=>new ChapterParagraph(p.GetProperty("id").GetString()!,p.GetProperty("en").GetString()!,p.GetProperty("zh").GetString()!)).ToImmutableArray());
        }
        using var campaign=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"content/source/campaign/first_entry.json")));
        foreach(var m in campaign.RootElement.GetProperty("materials").EnumerateArray())if(m.GetProperty("id").GetString()==id&&m.GetProperty("modality").GetString()=="listening_script")return(m.GetProperty("version").GetString()!,[new(id+"-original",m.GetProperty("english").GetString()!,m.GetProperty("chinese").GetString()!)]);
        throw new InvalidOperationException("Unregistered regional candidate original: "+id);
    }
    public static void Register()
    {
        Add("CX-066","Thirty-two actual campaign and regional WAVE candidates bind exact original speech slices and measured PCM without human approval",()=>
        {
            var bindings=Bindings();Require(bindings.Length==32&&bindings.Select(b=>b.MaterialId).Distinct().Count()==32);
            foreach(var b in bindings){var m=Material(b.MaterialId);var facts=RegionalAudioRules.Validate(b.MaterialId,m.Version,m.Paragraphs,b,File.ReadAllBytes(Path.Combine(Root,"game/content",b.AudioPath)));Require(facts.DurationMicroseconds>0&&facts.Bytes==b.Bytes&&facts.SampleRate>0&&b.ReviewStatus=="rendered_candidate_not_auditioned");}
        });
        Add("CX-066","Real rendered candidate rejects changed waveform, wrong source version and spoken role-label reinsertion",()=>
        {
            var b=Bindings()[0];var m=Material(b.MaterialId);var bytes=File.ReadAllBytes(Path.Combine(Root,"game/content",b.AudioPath));bytes[^1]^=1;
            Throws<InvalidDataException>(()=>RegionalAudioRules.Validate(b.MaterialId,m.Version,m.Paragraphs,b,bytes));
            var actual=File.ReadAllBytes(Path.Combine(Root,"game/content",b.AudioPath));Throws<InvalidDataException>(()=>RegionalAudioRules.Validate(b.MaterialId,"wrong-original-version",m.Paragraphs,b,actual));
            var wrong=b with{Segments=b.Segments.SetItem(0,b.Segments[0] with{Text=b.Segments[0].LiteralPrefix+" "+b.Segments[0].Text})};Throws<InvalidDataException>(()=>RegionalAudioRules.Validate(b.MaterialId,m.Version,m.Paragraphs,wrong,actual));
        });
        Add("CX-066","Actual audio hashes and local voice metadata cannot grant auditioned status or voice redistribution approval",()=>
        {
            var b=Bindings()[0];var m=Material(b.MaterialId);var actual=File.ReadAllBytes(Path.Combine(Root,"game/content",b.AudioPath));
            Throws<InvalidDataException>(()=>RegionalAudioRules.Validate(b.MaterialId,m.Version,m.Paragraphs,b with{ReviewStatus="auditioned"},actual));
            Throws<InvalidDataException>(()=>RegionalAudioRules.Validate(b.MaterialId,m.Version,m.Paragraphs,b with{RedistributionRights="approved"},actual));
        });
        Add("CX-066","Multi-role local recordings reject dropped turns, changed offsets, arbitrary speaker labels and reordered original paragraphs",()=>
        {
            var b=Bindings().Single(x=>x.MaterialId=="R1-ROOM-L-GUESTS");var m=Material(b.MaterialId);var bytes=File.ReadAllBytes(Path.Combine(Root,"game/content",b.AudioPath));
            Require(b.Segments.Length==6&&b.Segments.All(s=>!s.Text.Contains(s.LiteralPrefix,StringComparison.Ordinal)));
            void Reject(RegionalAudioBinding candidate)=>Throws<InvalidDataException>(()=>RegionalAudioRules.Validate(b.MaterialId,m.Version,m.Paragraphs,candidate,bytes));
            Reject(b with{Segments=b.Segments.RemoveAt(1)});
            Reject(b with{Segments=b.Segments.SetItem(1,b.Segments[1] with{SourceStartScalar=b.Segments[1].SourceStartScalar+1})});
            Reject(b with{Segments=b.Segments.SetItem(0,b.Segments[0] with{LiteralPrefix="Please:",SpeakerId="Please"})});
            Reject(b with{Segments=b.Segments.Reverse().ToImmutableArray()});
        });
    }
}
