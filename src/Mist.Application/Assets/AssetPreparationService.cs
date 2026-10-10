using System.Collections.Immutable;
using Mist.Core.Assets;
using Mist.Core.Content;
using Mist.Core.Contracts;

namespace Mist.Application.Assets;

public sealed record ExistingAudioAudit(AssetRef Source,string RelativePath,WaveFacts ActualAudio,string GenerationInput,string Status,ImmutableArray<AssetIssue> Issues,ImmutableArray<string> Pending);
public sealed record RegenerationPreparation(OriginalScript Original,SpeechProjection Projection,ImmutableArray<CriticalDetail> CriticalDetails,ImmutableArray<AssetIssue> ExistingRawSpeechIssues,ImmutableArray<string> Pending);

// Preparation and validation only. It has no world commands, speech renderer or publishing authority.
public sealed class AssetPreparationService
{
    public static readonly ImmutableArray<SpeakerRule> Q102Speakers=[new("Dispatcher:","dispatcher","VOICE-DISPATCH-A"),new("Electrician:","electrician","VOICE-TECH-B")];
    public static OriginalScript Original(MaterialDefinition material)
    {
        var original=new OriginalScript(new(material.Id,material.Version,material.BodySha256),material.Paragraphs.Select(p=>new SourceParagraph(p.Id,p.Text,p.Sha256)).ToImmutableArray());
        AssetText.Original(original);return original;
    }
    public RegenerationPreparation PrepareQ102Speech(MaterialDefinition material,string projectionVersion)
    {
        if(material.Id!="Q102-L-REVISION") throw new InvalidDataException("This preparation is bounded to the declared six-turn Q102 recording.");
        var original=Original(material);
        if(original.Paragraphs.Length!=6) throw new InvalidDataException("The six-turn recording shape changed; review the projection contract before rendering.");
        var projection=SpeechProjectionRules.Create(original,projectionVersion,Q102Speakers);
        var critical=ImmutableArray.CreateBuilder<CriticalDetail>();
        void Anchor(int paragraph,string phrase,string category)
        {
            var p=original.Paragraphs[paragraph];int offset=p.Text.IndexOf(phrase,StringComparison.Ordinal);
            if(offset<0||p.Text.IndexOf(phrase,offset+phrase.Length,StringComparison.Ordinal)>=0) throw new InvalidDataException("Declared Q102 audit phrase is absent or ambiguous: "+phrase);
            int start=AssetText.Scalars(p.Text[..offset]),end=start+AssetText.Scalars(phrase);
            critical.Add(new("Q102-critical-"+(critical.Count+1).ToString("D2"),p.Id,start,end,category,phrase,AssetText.Hash(phrase)));
        }
        Anchor(0,"does not support that account","negation");
        Anchor(0,"beacon itself still works","correction_subject");
        Anchor(0,"damaged part is the bracket","correction_subject");
        Anchor(1,"Do not look for a replacement beacon","negation");
        Anchor(1,"not permission to walk through a restricted work area","condition");
        Anchor(3,"Keep it off","negation");
        Anchor(3,"has not finished isolating the flooded cables","negation");
        Anchor(3,"own charged backup battery","correction_subject");
        Anchor(3,"separate from the workshop equipment","correction_subject");
        Anchor(3,"Once the new bracket is fitted and the battery connected","condition");
        Anchor(3,"check the beacon on the boat","correction_subject");
        Anchor(3,"not the same as a checked installation","negation");
        Anchor(4,"not yet issuing a departure instruction","time");
        Anchor(5,"not saying that every channel is suitable","negation");
        Anchor(5,"must do before leaving","time");
        Anchor(5,"If you are not sure what an instruction means","condition");
        Anchor(5,"rather than treating a guess as approval","condition");
        return new(original,projection,critical.ToImmutable(),SpeechProjectionRules.DetectRawRoleLabels(original,Q102Speakers),[
            "New rendering is explicitly requested preparation only; this method generates no audio and changes no original material or existing recording.",
            "VOICE-DISPATCH-A and VOICE-TECH-B are declared production slots, not available/approved voices; voice selection, permission and actual contrasting voices require review.",
            "The bounded critical-detail sheet is a candidate. Editorial completeness, every applicable quantity/name, pronunciation and a full actual-track audition remain pending.",
            "Subtitles require measured alignment against a newly rendered track and the exact source/speech/audio hashes; this preparation invents no timecodes.",
            "Human listening, localization, rights, native task trial and language calibration are not certified by this projection."]);
    }
    public async ValueTask<WaveFacts> InspectWaveAsync(string assetRoot,string relativePath,CancellationToken cancellationToken=default)
    {
        AudioSubtitleRules.SafeAssetPath(relativePath);string full=Contained(assetRoot,relativePath);
        RejectLinks(assetRoot,full);
        return WaveInspector.Inspect(await File.ReadAllBytesAsync(full,cancellationToken));
    }
    public async ValueTask<WaveFacts?> ValidateAudioAsync(OriginalScript original,SpeechProjection projection,AudioRegistration audio,string assetRoot,CancellationToken cancellationToken=default)
    {
        WaveFacts? measured=audio.Stage==AudioStage.ScriptOnly?null:await InspectWaveAsync(assetRoot,audio.RelativePath??throw new InvalidDataException("Rendered candidate has no actual path."),cancellationToken);
        AudioSubtitleRules.Audio(original,projection,audio,measured);return measured;
    }
    public async ValueTask<WaveFacts> ValidateSubtitlesAsync(OriginalScript original,SpeechProjection projection,AudioRegistration audio,SubtitleTrack subtitles,string assetRoot,CancellationToken cancellationToken=default)
    {
        var measured=await ValidateAudioAsync(original,projection,audio,assetRoot,cancellationToken);
        AudioSubtitleRules.Subtitles(original,projection,audio,measured,subtitles);return measured!;
    }
    public async ValueTask<ExistingAudioAudit> AuditExistingRawRenderingAsync(MaterialDefinition material,string assetRoot,string relativePath,string expectedAudioHash,CancellationToken cancellationToken=default)
    {
        var original=Original(material);var measured=await InspectWaveAsync(assetRoot,relativePath,cancellationToken);
        if(measured.Sha256!=expectedAudioHash) throw new InvalidDataException("Actual audio bytes differ from the declared generation hash.");
        var issues=SpeechProjectionRules.DetectRawRoleLabels(original,Q102Speakers);
        return new(original.Source,relativePath,measured,"raw_original_paragraphs_with_role_labels","rendered_candidate_not_human_auditioned",issues,[
            "This records the actual existing WAVE bytes; it does not claim this track was rendered from the corrected speech projection.",
            "Role-label diagnosis follows the declared raw-paragraph generator input. Full human listening must verify the actual output, voices, critical details and pronunciation.",
            "Speaker-label removal requires a separate explicit regeneration and review; the original script and existing track remain intact.",
            "No subtitle timing, human audition or voice redistribution approval is inferred from a hash/duration measurement."]);
    }
    public async ValueTask<string> ExportNewArtifactAsync<T>(string outputRoot,string relativePath,T candidate,CancellationToken cancellationToken=default)
    {
        AudioSubtitleRules.SafeAssetPath(relativePath);
        if(!relativePath.EndsWith(".json",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Export preparation as a new JSON artifact.");
        string full=Contained(outputRoot,relativePath);
        // Callers choose an explicit candidate-output directory. They cannot silently overwrite inputs.
        RejectLinks(outputRoot,full);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        RejectLinks(outputRoot,full);
        await using var stream=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.Asynchronous);
        await using var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false));
        await writer.WriteAsync(StrictJson.Serialize(candidate).AsMemory(),cancellationToken);
        return full;
    }
    private static string Contained(string root,string relative)
    {
        string basis=Path.GetFullPath(root);string full=Path.GetFullPath(Path.Combine(basis,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(basis.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Asset path escapes the declared directory.");
        return full;
    }
    private static void RejectLinks(string root,string full)
    {
        string basis=Path.GetFullPath(root);string? current=full;
        while(current is not null)
        {
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Asset input/output paths cannot traverse a symbolic link or junction.");
            if(string.Equals(current,basis,StringComparison.OrdinalIgnoreCase)) break;
            current=Path.GetDirectoryName(current);
        }
    }
}
