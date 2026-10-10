using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Text;
using Mist.Core.Contracts;
using Mist.Core.ChapterTwo;

namespace Mist.Core.Assets;

public sealed record RegionalSpeechSegment(string ParagraphId,string LiteralPrefix,string SpeakerId,int SourceStartScalar,
    int SourceEndScalar,string Text,string Sha256);
public sealed record RegionalAudioBinding(string MaterialId,string MaterialVersion,string TextSha256,string SpeechSha256,
    string AudioVersion,string Voice,string AudioPath,ImmutableArray<RegionalSpeechSegment> Segments,string ReviewStatus,
    string RedistributionRights,string AudioSha256,long Bytes);

public static class RegionalAudioRules
{
    public static WaveFacts Validate(ChapterMaterial material,RegionalAudioBinding binding,ReadOnlySpan<byte> audio)
        =>Validate(material.Id,material.Version,material.Paragraphs,binding,audio);
    public static WaveFacts Validate(string id,string version,ImmutableArray<ChapterParagraph> paragraphs,RegionalAudioBinding b,ReadOnlySpan<byte> audio)
    {
        string original=string.Join("\n\n",paragraphs.Select(p=>p.English));
        if(b.MaterialId!=id||b.MaterialVersion!=version||b.TextSha256!=AssetText.Hash(original)||b.AudioVersion!="local-tts-1"||
           b.Voice!="Microsoft Zira Desktop - English (United States)"||b.ReviewStatus!="rendered_candidate_not_auditioned"||
           b.RedistributionRights!="pending_HV-004"||!Regex.IsMatch(b.AudioPath,"^audio/regional/[A-Za-z0-9_.-]+\\.wav$",RegexOptions.CultureInvariant)||
           b.Segments.IsDefaultOrEmpty)
            throw new InvalidDataException("Candidate speech must bind exact original bytes, declared local voice and pending human/rights scope.");
        if(id is "R1-ROOM-L-GUESTS" or "R1-SHOW-L-CREW")ValidateLocalTurns(id,paragraphs,b.Segments);
        else
        {
        if(b.Segments.Length!=paragraphs.Length)throw new InvalidDataException("Each original regional paragraph requires its declared speech turn.");
        for(int i=0;i<paragraphs.Length;i++)
        {
            var p=paragraphs[i];var segment=b.Segments[i];
            // Validate each exact turn independently. Continuation prose has an explicit narrator
            // rule, so it cannot mask a mismatched label in another paragraph.
            var source=new OriginalScript(new(id+":"+p.Id,version,AssetText.Hash(p.English)),[new(p.Id,p.English,AssetText.Hash(p.English))]);
            var speech=SpeechProjectionRules.Create(source,b.AudioVersion,[new(segment.LiteralPrefix,segment.SpeakerId,b.Voice)]).Segments.Single();
            if(segment.ParagraphId!=p.Id||segment.SourceStartScalar!=speech.SourceStartScalar||segment.SourceEndScalar!=speech.SourceEndScalar||segment.Text!=speech.Text||segment.Sha256!=speech.Sha256)
                throw new InvalidDataException("Candidate spoken slice differs from the exact declared source turn.");
        }
        }
        if(b.SpeechSha256!=AssetText.Hash(string.Join("\n\n",b.Segments.Select(s=>s.Text))))throw new InvalidDataException("Candidate speech concatenation hash mismatch.");
        var facts=WaveInspector.Inspect(audio);
        if(facts.Sha256!=b.AudioSha256||facts.Bytes!=b.Bytes||facts.DurationMicroseconds<=0)throw new InvalidDataException("Actual candidate WAVE bytes do not match the versioned declaration.");
        return facts;
    }
    private static void ValidateLocalTurns(string id,ImmutableArray<ChapterParagraph> paragraphs,ImmutableArray<RegionalSpeechSegment> segments)
    {
        string[] labels=id=="R1-ROOM-L-GUESTS"?["Repairer:","Innkeeper:","Traveller:"]:["Stage organiser:","Second performer:"];
        int index=0;
        foreach(var paragraph in paragraphs)
        {
            string text=paragraph.English;int cursor=0,total=AssetText.Scalars(text);
            while(cursor<total)
            {
                if(index>=segments.Length)throw new InvalidDataException("Local spoken turns omit original text.");
                var segment=segments[index++];int utf16=UnicodeOffsets.ToUtf16(text,cursor);
                var labelsHere=labels.Where(label=>text.AsSpan(utf16).StartsWith(label,StringComparison.Ordinal)).ToArray();
                if(labelsHere.Length!=1||segment.ParagraphId!=paragraph.Id||segment.LiteralPrefix!=labelsHere[0]||segment.SpeakerId!=labelsHere[0][..^1])
                    throw new InvalidDataException("Local dialogue requires an exact registered speaker at the current original offset.");
                int start=cursor+AssetText.Scalars(segment.LiteralPrefix);
                while(start<total&&UnicodeOffsets.Slice(text,start,start+1).EnumerateRunes().All(Rune.IsWhiteSpace))start++;
                int startUtf16=UnicodeOffsets.ToUtf16(text,start),nextUtf16=text.Length;
                foreach(string label in labels)
                {
                    int found=text.IndexOf(label,startUtf16,StringComparison.Ordinal);
                    if(found>=0&&found<nextUtf16)nextUtf16=found;
                }
                int end=AssetText.Scalars(text[..nextUtf16]);string actual=UnicodeOffsets.Slice(text,start,end);
                if(start>=end||string.IsNullOrWhiteSpace(actual)||segment.SourceStartScalar!=start||segment.SourceEndScalar!=end||segment.Text!=actual||segment.Sha256!=AssetText.Hash(actual))
                    throw new InvalidDataException("Local dialogue slice omits, repeats, reorders or changes original wording.");
                cursor=end;
            }
        }
        if(index!=segments.Length)throw new InvalidDataException("Local dialogue contains extra or reordered original turns.");
    }
}
