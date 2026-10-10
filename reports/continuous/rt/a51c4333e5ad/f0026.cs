using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Core.Contracts;

namespace Mist.Core.Assets;

public sealed record AssetRef(string Id, string Version, string Sha256);
public sealed record SourceParagraph(string Id, string Text, string Sha256);
public sealed record OriginalScript(AssetRef Source, ImmutableArray<SourceParagraph> Paragraphs);
public sealed record SpeakerRule(string LiteralPrefix, string SpeakerId, string VoiceId);
public sealed record SpeechSegment(string ParagraphId, string SpeakerId, string VoiceId, int SourceStartScalar, int SourceEndScalar, string Text, string Sha256);
public sealed record SpeechProjection(AssetRef Source, AssetRef Projection, ImmutableArray<SpeakerRule> Rules, ImmutableArray<SpeechSegment> Segments);
public enum AudioStage { ScriptOnly, Rendered, Auditioned }
public sealed record WaveFacts(string Sha256, long Bytes, long DurationMicroseconds, int Channels, int SampleRate, int BitsPerSample);
public sealed record AudioRegistration(string Id, AssetRef? Asset, AssetRef Source, AssetRef Speech, AudioStage Stage, string? RelativePath, long? Bytes, long? DurationMicroseconds, string VoiceRightsId, string? HumanAuditionEvidence);
public sealed record SubtitleCue(string ParagraphId, int SourceStartScalar, int SourceEndScalar, long StartMicroseconds, long EndMicroseconds, string Text, string Sha256);
public sealed record SubtitleTrack(AssetRef Asset, AssetRef Source, AssetRef Speech, AssetRef Audio, ImmutableArray<SubtitleCue> Cues);
public sealed record AssetIssue(string Code, string AssetId, string Detail);
public sealed record CriticalDetail(string Id, string ParagraphId, int SourceStartScalar, int SourceEndScalar, string Category, string Text, string Sha256);
public sealed record ListeningAuditPlan(AssetRef Source, AssetRef Speech, AssetRef Audio, ImmutableArray<CriticalDetail> CriticalDetails, string Status, ImmutableArray<string> Pending);

public static class AssetText
{
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static int Scalars(string text)
    {
        int count=0;
        foreach(var _ in text.EnumerateRunes()) count++;
        // Decode separately to reject malformed UTF-16 rather than replacing it in a hash or range.
        UnicodeOffsets.ToUtf16(text,count);
        return count;
    }
    public static void Ref(AssetRef reference)
    {
        if(string.IsNullOrWhiteSpace(reference.Id)||string.IsNullOrWhiteSpace(reference.Version)||reference.Sha256.Length!=64||reference.Sha256.Any(c=>!(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Asset reference requires an ID, version and lower-case SHA-256.");
    }
    public static void Original(OriginalScript script)
    {
        Ref(script.Source);
        if(script.Paragraphs.IsDefaultOrEmpty||script.Paragraphs.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=script.Paragraphs.Length) throw new InvalidDataException("Original paragraphs must have unique stable IDs.");
        foreach(var p in script.Paragraphs)
        {
            if(string.IsNullOrWhiteSpace(p.Id)||string.IsNullOrWhiteSpace(p.Text)||p.Sha256!=Hash(p.Text)) throw new InvalidDataException("Original paragraph hash or ID is invalid.");
            Scalars(p.Text);
        }
        if(script.Source.Sha256!=Hash(string.Join("\n",script.Paragraphs.Select(p=>p.Text)))) throw new InvalidDataException("Original body hash differs from exact paragraph bytes; normalization is not implicit.");
    }
    public static void ChangedVersion(AssetRef oldReference,AssetRef newReference)
    {
        Ref(oldReference);Ref(newReference);
        if(oldReference.Id!=newReference.Id) throw new InvalidDataException("A change must name the same stable asset ID.");
        if(oldReference.Version==newReference.Version&&oldReference.Sha256!=newReference.Sha256) throw new InvalidDataException("Different bytes cannot retain the same ID and version.");
    }
}

public static class SpeechProjectionRules
{
    // Only exact, explicitly declared role labels are omitted. Spoken wording is an unchanged source slice.
    public static SpeechProjection Create(OriginalScript original,string projectionVersion,ImmutableArray<SpeakerRule> rules)
    {
        AssetText.Original(original);
        if(rules.IsDefaultOrEmpty||rules.Select(r=>r.LiteralPrefix).Distinct(StringComparer.Ordinal).Count()!=rules.Length||rules.Any(r=>string.IsNullOrWhiteSpace(r.SpeakerId)||string.IsNullOrWhiteSpace(r.VoiceId))) throw new InvalidDataException("Declare unique literal speaker rules and voice IDs.");
        if(rules.Any(r=>r.LiteralPrefix.Length>0&&(!r.LiteralPrefix.EndsWith(':')||r.LiteralPrefix.Length==1||r.LiteralPrefix[..^1].Any(c=>!(char.IsLetter(c)||c is ' ' or '-' or '_'))))) throw new InvalidDataException("Only a literal role-name label ending in ':' may be omitted; prose is not a speaker label.");
        if(rules.Any(r=>r.LiteralPrefix.Length==0)&&rules.Length!=1) throw new InvalidDataException("A narrator rule cannot mask unmatched dialogue labels.");
        var segments=ImmutableArray.CreateBuilder<SpeechSegment>();
        foreach(var paragraph in original.Paragraphs)
        {
            var matches=rules.Where(r=>paragraph.Text.StartsWith(r.LiteralPrefix,StringComparison.Ordinal)).ToArray();
            if(matches.Length!=1) throw new InvalidDataException("Exactly one declared speaker must match paragraph "+paragraph.Id);
            var rule=matches[0];int units=rule.LiteralPrefix.Length;
            if(units>0) while(units<paragraph.Text.Length&&char.IsWhiteSpace(paragraph.Text[units])) units++;
            int start=AssetText.Scalars(paragraph.Text[..units]),end=AssetText.Scalars(paragraph.Text);
            string spoken=UnicodeOffsets.Slice(paragraph.Text,start,end);
            if(string.IsNullOrWhiteSpace(spoken)) throw new InvalidDataException("An empty spoken turn is not valid.");
            segments.Add(new(paragraph.Id,rule.SpeakerId,rule.VoiceId,start,end,spoken,AssetText.Hash(spoken)));
        }
        var all=segments.ToImmutable();
        // Projection identity includes role/voice/range mapping as well as the exact spoken bytes.
        string hash=AssetText.Hash(StrictJson.Serialize(new{source=original.Source,rules,segments=all}));
        var projection=new SpeechProjection(original.Source,new(original.Source.Id+":speech",projectionVersion,hash),rules,all);
        AssetText.Ref(projection.Projection);return projection;
    }
    public static void Validate(OriginalScript original,SpeechProjection projection)
    {
        if(projection.Source!=original.Source) throw new InvalidDataException("Speech projection binds a different original version.");
        var expected=Create(original,projection.Projection.Version,projection.Rules);
        if(StrictJson.Serialize(expected)!=StrictJson.Serialize(projection)) throw new InvalidDataException("Speech projection differs from the complete declared source ranges, voices or hashes.");
    }
    public static ImmutableArray<AssetIssue> DetectRawRoleLabels(OriginalScript original,ImmutableArray<SpeakerRule> rules)
    {
        AssetText.Original(original);
        return original.Paragraphs.Where(p=>rules.Any(r=>r.LiteralPrefix.Length>0&&p.Text.StartsWith(r.LiteralPrefix,StringComparison.Ordinal)))
            .Select(p=>new AssetIssue("role_label_in_raw_speech",original.Source.Id,p.Id+": raw paragraph playback would speak its role label; use an explicit reviewed speech projection for a new rendering.")).ToImmutableArray();
    }
}

public static class WaveInspector
{
    // Inspect actual local bytes. There is no estimated duration or synthetic production-audio fallback.
    public static WaveFacts Inspect(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<44||!bytes[..4].SequenceEqual("RIFF"u8)||!bytes.Slice(8,4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("Expected an actual RIFF/WAVE asset.");
        long declared=(long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4,4))+8;
        if(declared!=bytes.Length) throw new InvalidDataException("WAVE RIFF length differs from actual bytes.");
        int channels=0,sampleRate=0,bits=0,align=0;long byteRate=0,dataBytes=-1;bool format=false;
        for(int position=12;position<bytes.Length;)
        {
            if(bytes.Length-position<8) throw new InvalidDataException("Truncated WAVE chunk header.");
            uint length=BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position+4,4));long next=(long)position+8+length+(length%2);
            if(next>bytes.Length||length>int.MaxValue) throw new InvalidDataException("Truncated WAVE chunk data.");
            var id=bytes.Slice(position,4);var body=bytes.Slice(position+8,(int)length);
            if(id.SequenceEqual("fmt "u8))
            {
                if(format||body.Length<16) throw new InvalidDataException("WAVE needs exactly one complete fmt chunk.");
                int kind=BinaryPrimitives.ReadUInt16LittleEndian(body[..2]);
                channels=BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2,2));sampleRate=BinaryPrimitives.ReadInt32LittleEndian(body.Slice(4,4));byteRate=BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(8,4));align=BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(12,2));bits=BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(14,2));
                if(kind is not (1 or 3)||channels<=0||sampleRate<=0||bits<=0||bits%8!=0||align!=channels*(bits/8)||byteRate!=(long)sampleRate*align||(kind==3&&bits is not (32 or 64))) throw new InvalidDataException("Unsupported or inconsistent PCM/IEEE-float WAVE format.");
                format=true;
            }
            if(id.SequenceEqual("data"u8))
            {
                if(dataBytes>=0) throw new InvalidDataException("Multiple data chunks require a separate measured decoder adapter.");
                dataBytes=length;
            }
            position=(int)next;
        }
        if(!format||dataBytes<=0||dataBytes%align!=0) throw new InvalidDataException("WAVE has no complete audio frames.");
        long duration=checked(dataBytes*1_000_000/byteRate);
        if(duration<=0) throw new InvalidDataException("WAVE duration must be measurable in microseconds.");
        return new(AssetText.Hash(bytes),bytes.Length,duration,channels,sampleRate,bits);
    }
}

public static class AudioSubtitleRules
{
    public static void Audio(OriginalScript script,SpeechProjection speech,AudioRegistration audio,WaveFacts? measured)
    {
        SpeechProjectionRules.Validate(script,speech);
        if(string.IsNullOrWhiteSpace(audio.Id)) throw new InvalidDataException("An audio candidate requires a stable ID.");
        if(audio.Source!=script.Source||audio.Speech!=speech.Projection) throw new InvalidDataException("Audio references do not bind the exact source and speech projection.");
        if(audio.Stage==AudioStage.ScriptOnly)
        {
            if(audio.Asset is not null||audio.RelativePath is not null||audio.Bytes is not null||audio.DurationMicroseconds is not null||measured is not null||audio.HumanAuditionEvidence is not null) throw new InvalidDataException("Script-only assets cannot claim audio hash, bytes, duration, measurement or audition.");
            return;
        }
        if(!Enum.IsDefined(audio.Stage)||audio.Asset is null||audio.Asset.Id!=audio.Id||measured is null||audio.Asset.Sha256!=measured.Sha256||audio.Bytes!=measured.Bytes||audio.DurationMicroseconds!=measured.DurationMicroseconds||string.IsNullOrWhiteSpace(audio.VoiceRightsId)) throw new InvalidDataException("Rendered audio requires actual bytes, exact hash/duration and a voice-rights reference.");
        AssetText.Ref(audio.Asset);
        SafeAssetPath(audio.RelativePath);
        // A string in a JSON file is never a human review authority.
        if(audio.Stage==AudioStage.Auditioned) throw new InvalidDataException("Auditioned status requires a separate trusted human registry adapter; this candidate does not certify it.");
        if(audio.HumanAuditionEvidence is not null) throw new InvalidDataException("Rendered candidates cannot carry an unverified human audition claim.");
    }
    public static void SafeAssetPath(string? relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains('\\')||relative.Contains(':')||relative.Split('/').Any(p=>p is ".." or "." or "")) throw new InvalidDataException("Use a contained relative asset path.");
        if(new[]{".ttf",".otf",".woff",".woff2"}.Contains(Path.GetExtension(relative),StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("The design handoff cannot supply font binaries.");
    }
    public static void Subtitles(OriginalScript script,SpeechProjection speech,AudioRegistration audio,WaveFacts? measured,SubtitleTrack subtitles)
    {
        Audio(script,speech,audio,measured);AssetText.Ref(subtitles.Asset);
        if(audio.Stage==AudioStage.ScriptOnly||measured is null) throw new InvalidDataException("Script-only assets cannot have measured subtitle timecodes.");
        if(subtitles.Source!=script.Source||subtitles.Speech!=speech.Projection||subtitles.Audio!=audio.Asset) throw new InvalidDataException("Subtitles reference stale source, speech or audio bytes.");
        if(subtitles.Cues.IsDefaultOrEmpty||subtitles.Asset.Sha256!=AssetText.Hash(StrictJson.Serialize(subtitles.Cues))) throw new InvalidDataException("Subtitle cue content hash is invalid.");
        int expectedParagraph=0;int expectedStart=speech.Segments[0].SourceStartScalar;long previousEnd=0;
        foreach(var cue in subtitles.Cues)
        {
            if(expectedParagraph>=speech.Segments.Length) throw new InvalidDataException("Extra subtitle paragraph.");
            var segment=speech.Segments[expectedParagraph];var paragraph=script.Paragraphs[expectedParagraph];
            if(cue.ParagraphId!=segment.ParagraphId||cue.SourceStartScalar!=expectedStart||cue.SourceEndScalar<=cue.SourceStartScalar||cue.SourceEndScalar>segment.SourceEndScalar) throw new InvalidDataException("Subtitle source ranges must cover each spoken paragraph in order without omissions or repetitions.");
            if(cue.StartMicroseconds<previousEnd||cue.StartMicroseconds<0||cue.EndMicroseconds<=cue.StartMicroseconds||cue.EndMicroseconds>measured.DurationMicroseconds) throw new InvalidDataException("Subtitle timecodes overlap, reverse or exceed measured audio duration.");
            if(cue.Text!=UnicodeOffsets.Slice(paragraph.Text,cue.SourceStartScalar,cue.SourceEndScalar)||cue.Sha256!=AssetText.Hash(cue.Text)) throw new InvalidDataException("Subtitle text differs from its exact original source range.");
            previousEnd=cue.EndMicroseconds;expectedStart=cue.SourceEndScalar;
            if(expectedStart==segment.SourceEndScalar)
            {
                expectedParagraph++;
                if(expectedParagraph<speech.Segments.Length) expectedStart=speech.Segments[expectedParagraph].SourceStartScalar;
            }
        }
        if(expectedParagraph!=speech.Segments.Length) throw new InvalidDataException("Subtitle track omits spoken paragraphs or source ranges.");
    }
    public static ListeningAuditPlan AuditPlan(OriginalScript script,SpeechProjection speech,AudioRegistration audio,WaveFacts measured,ImmutableArray<CriticalDetail> critical)
    {
        Audio(script,speech,audio,measured);
        string[] categories=["negation","number","condition","time","name","correction_subject"];
        if(critical.IsDefaultOrEmpty||critical.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=critical.Length) throw new InvalidDataException("Declare unique critical-detail anchors; automatic coverage is not inferred.");
        foreach(var detail in critical)
        {
            var paragraph=script.Paragraphs.SingleOrDefault(p=>p.Id==detail.ParagraphId)??throw new InvalidDataException("Unknown audit paragraph.");
            if(string.IsNullOrWhiteSpace(detail.Id)||!categories.Contains(detail.Category,StringComparer.Ordinal)||detail.SourceEndScalar<=detail.SourceStartScalar||detail.Text!=UnicodeOffsets.Slice(paragraph.Text,detail.SourceStartScalar,detail.SourceEndScalar)||detail.Sha256!=AssetText.Hash(detail.Text)) throw new InvalidDataException("Critical audit anchor is not an exact original range.");
        }
        return new(script.Source,speech.Projection,audio.Asset!,critical,"pending_human_full_audition",["Critical-anchor completeness requires editorial review; detected anchors do not prove semantic coverage.","Listen to the entire actual track and check every declared negation, number, condition, time, name and correction subject.","Alignment, byte inspection and text comparison are engineering checks; human listening, pronunciation, voices, mixing and rights remain pending."]);
    }
}

public enum AssetKind { Original, Speech, Audio, Subtitle, Translation, Lexicon, Hint, TaskRule, ExposurePolicy, Calibration, Visual, Decoration, Audition }
public enum DependencyUse { OriginalText, SpokenProjection, AudioBytes, TaskRules, ExposurePolicy, VisualData, Decoration }
public sealed record AssetDependency(AssetRef Source,DependencyUse Use);
public sealed record AssetEntry(AssetRef Asset,AssetKind Kind,string RightsId,ImmutableArray<AssetDependency> Dependencies);
public sealed record RightsRecord(string Id,string Version,string Origin,string Source,string License,string Scope,ImmutableArray<string> AllowedFields,string Attribution,string? VoiceConsentReference,string ReviewStatus);
public sealed record AssetDirectory(ImmutableArray<AssetEntry> Entries,ImmutableArray<RightsRecord> Rights);
public sealed record RightsAudit(ImmutableArray<AssetIssue> StructuralIssues,ImmutableArray<AssetIssue> HumanPending);
public sealed record AssetChange(AssetRef Before,AssetRef After);
public sealed record ReviewInvalidation(AssetRef Asset,string Status,ImmutableArray<string> Reasons);
public sealed record PropagationReport(ImmutableArray<ReviewInvalidation> Invalidations,ImmutableArray<string> Pending);
public sealed record BatchDecision(bool Paused,ImmutableArray<string> RepeatedCodes,ImmutableArray<string> Pending);

public static class AssetDirectoryRules
{
    public static void Validate(AssetDirectory directory)
    {
        if(directory.Entries.IsDefaultOrEmpty||directory.Entries.Select(e=>e.Asset.Id).Distinct(StringComparer.Ordinal).Count()!=directory.Entries.Length) throw new InvalidDataException("A directory snapshot must contain one current version per stable asset ID.");
        if(directory.Rights.IsDefault||directory.Rights.Select(r=>r.Id).Distinct(StringComparer.Ordinal).Count()!=directory.Rights.Length) throw new InvalidDataException("Rights directory IDs must be unique.");
        var entries=directory.Entries.ToDictionary(e=>e.Asset.Id,StringComparer.Ordinal);
        foreach(var entry in directory.Entries)
        {
            AssetText.Ref(entry.Asset);
            if(!Enum.IsDefined(entry.Kind)||entry.Dependencies.IsDefault||entry.Dependencies.Select(d=>d.Source.Id).Distinct(StringComparer.Ordinal).Count()!=entry.Dependencies.Length) throw new InvalidDataException("Invalid or duplicate asset dependencies.");
            foreach(var dependency in entry.Dependencies)
            {
                if(!Enum.IsDefined(dependency.Use)||!entries.TryGetValue(dependency.Source.Id,out var source)||source.Asset!=dependency.Source) throw new InvalidDataException("A dependency must bind an existing exact asset version/hash.");
                bool typeMatches=dependency.Use switch
                {
                    DependencyUse.OriginalText=>source.Kind==AssetKind.Original,
                    DependencyUse.SpokenProjection=>source.Kind==AssetKind.Speech,
                    DependencyUse.AudioBytes=>source.Kind==AssetKind.Audio,
                    DependencyUse.TaskRules=>source.Kind==AssetKind.TaskRule,
                    DependencyUse.ExposurePolicy=>source.Kind==AssetKind.ExposurePolicy,
                    DependencyUse.VisualData=>source.Kind==AssetKind.Visual,
                    DependencyUse.Decoration=>source.Kind==AssetKind.Decoration,
                    _=>false
                };
                if(!typeMatches) throw new InvalidDataException("Dependency-use type differs from its source asset kind.");
            }
            DependencyUse[] required=entry.Kind switch
            {
                AssetKind.Speech or AssetKind.Translation or AssetKind.Lexicon=>[DependencyUse.OriginalText],
                AssetKind.Audio=>[DependencyUse.OriginalText,DependencyUse.SpokenProjection],
                AssetKind.Subtitle=>[DependencyUse.OriginalText,DependencyUse.SpokenProjection,DependencyUse.AudioBytes],
                AssetKind.Audition=>[DependencyUse.AudioBytes],
                AssetKind.Hint=>[DependencyUse.OriginalText,DependencyUse.TaskRules],
                AssetKind.Calibration=>[DependencyUse.OriginalText,DependencyUse.TaskRules,DependencyUse.ExposurePolicy],
                _=>[]
            };
            if(required.Any(r=>!entry.Dependencies.Any(d=>d.Use==r))) throw new InvalidDataException("The asset omits a required declared dependency.");
        }
        var active=new HashSet<string>(StringComparer.Ordinal);var finished=new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if(finished.Contains(id)) return;
            if(!active.Add(id)) throw new InvalidDataException("Asset dependency cycle.");
            foreach(var d in entries[id].Dependencies) Visit(d.Source.Id);
            active.Remove(id);finished.Add(id);
        }
        foreach(var id in entries.Keys) Visit(id);
    }
    public static RightsAudit Rights(AssetDirectory directory)
    {
        Validate(directory);var problems=ImmutableArray.CreateBuilder<AssetIssue>();var pending=ImmutableArray.CreateBuilder<AssetIssue>();
        foreach(var entry in directory.Entries)
        {
            var record=directory.Rights.SingleOrDefault(r=>r.Id==entry.RightsId);
            if(record is null){problems.Add(new("rights_reference_missing",entry.Asset.Id,"No declared rights record."));continue;}
            if(string.IsNullOrWhiteSpace(record.Version)||record.Origin is not ("project_original_candidate" or "external")||string.IsNullOrWhiteSpace(record.Source)||string.IsNullOrWhiteSpace(record.License)||string.IsNullOrWhiteSpace(record.Scope)||string.IsNullOrWhiteSpace(record.Attribution)||record.AllowedFields.IsDefaultOrEmpty||record.AllowedFields.Any(string.IsNullOrWhiteSpace)||record.ReviewStatus!="pending_human_rights_review") problems.Add(new("rights_structure_or_claim",entry.Asset.Id,"Complete provenance, scope, attribution and field permissions; an untrusted approved status cannot grant rights."));
            if(entry.Kind==AssetKind.Audio&&string.IsNullOrWhiteSpace(record.VoiceConsentReference)) problems.Add(new("voice_consent_missing",entry.Asset.Id,"Voice use needs a declared permission/consent reference; local availability does not grant redistribution."));
            pending.Add(new("human_rights_review_pending",entry.Asset.Id,"Structural completeness does not verify ownership, license applicability, voice permission or publication approval."));
        }
        return new(problems.ToImmutable(),pending.ToImmutable());
    }
    public static void FieldPermission(RightsRecord record,string field)
    {
        if(record.AllowedFields.IsDefault||!record.AllowedFields.Contains(field,StringComparer.Ordinal)) throw new InvalidDataException("The rights record does not declare use of field: "+field);
        // This check is necessary, not a license certification. Publication remains human-gated.
    }
    public static PropagationReport Propagate(AssetDirectory directory,ImmutableArray<AssetChange> changes)
    {
        Validate(directory);var entries=directory.Entries.ToDictionary(e=>e.Asset.Id,StringComparer.Ordinal);
        if(changes.IsDefault||changes.Select(c=>c.Before.Id).Distinct(StringComparer.Ordinal).Count()!=changes.Length) throw new InvalidDataException("Changes must be distinct by stable asset ID.");
        var reasons=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);var queue=new Queue<(string Id,string Reason)>();
        foreach(var change in changes)
        {
            AssetText.ChangedVersion(change.Before,change.After);
            if(!entries.TryGetValue(change.Before.Id,out var old)||old.Asset!=change.Before) throw new InvalidDataException("Change does not bind the directory's current bytes.");
            if(change.Before==change.After) continue;
            string reason=old.Kind+" "+change.Before.Id+" changed from "+change.Before.Version+" to "+change.After.Version;
            queue.Enqueue((change.Before.Id,reason));
        }
        while(queue.TryDequeue(out var change))
        {
            foreach(var entry in directory.Entries.Where(e=>e.Dependencies.Any(d=>d.Source.Id==change.Id)))
            {
                // Decoration cannot affect English review unless it declares an informational dependency.
                if(entry.Dependencies.Where(d=>d.Source.Id==change.Id).All(d=>d.Use==DependencyUse.Decoration)&&entry.Kind!=AssetKind.Decoration) continue;
                if(!reasons.TryGetValue(entry.Asset.Id,out var set)){set=new(StringComparer.Ordinal);reasons.Add(entry.Asset.Id,set);}
                if(set.Add(change.Reason)) queue.Enqueue((entry.Asset.Id,change.Reason));
            }
        }
        return new(reasons.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new ReviewInvalidation(entries[p.Key].Asset,"pending_review",p.Value.Order(StringComparer.Ordinal).ToImmutableArray())).ToImmutableArray(),["The report does not rewrite original assets, old packages or world state, and does not re-approve descendants.","Any exemption requires separate reviewed evidence; package publishing, calibration and human signatures remain pending."]);
    }
    public static BatchDecision Batch(ImmutableArray<(string Template,string Code,string Asset)> issues)
    {
        var common=issues.GroupBy(i=>(i.Template,i.Code)).Where(g=>g.Select(i=>i.Asset).Distinct(StringComparer.Ordinal).Count()>1).Select(g=>g.Key.Template+":"+g.Key.Code).Order(StringComparer.Ordinal).ToImmutableArray();
        return new(common.Length>0,common,["Repeated template failures pause the candidate batch for review; this tool performs no automatic regeneration or human approval."]);
    }
}
