using System.Buffers.Binary;
using System.Collections.Immutable;
using Mist.Application.Assets;
using Mist.Core.Assets;
using Mist.Core.Contracts;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Assets;

public static class AssetsCases
{
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private static OriginalScript Script(params string[] paragraphs)
    {
        var items=paragraphs.Select((p,i)=>new SourceParagraph("p"+(i+1),p,AssetText.Hash(p))).ToImmutableArray();
        return new(new("engineering-script","1",AssetText.Hash(string.Join("\n",paragraphs))),items);
    }
    // These PCM bytes are a declared engineering parser fixture, not narration or a listening asset.
    private static byte[] Wave(int seconds=6)
    {
        int data=8000*2*seconds;var b=new byte[44+data];"RIFF"u8.CopyTo(b);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4),36+data);"WAVEfmt "u8.CopyTo(b.AsSpan(8));BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16),16);BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(22),1);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(24),8000);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(28),16000);BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(32),2);BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(34),16);"data"u8.CopyTo(b.AsSpan(36));BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(40),data);return b;
    }
    private static AudioRegistration Audio(OriginalScript script,SpeechProjection speech,WaveFacts facts)=>new("engineering-audio",new("engineering-audio","1",facts.Sha256),script.Source,speech.Projection,AudioStage.Rendered,"audio/engineering-parser-fixture.wav",facts.Bytes,facts.DurationMicroseconds,"VOICE-RIGHTS-PENDING",null);
    private static SubtitleTrack Subtitles(OriginalScript script,SpeechProjection speech,AudioRegistration audio)
    {
        long length=audio.DurationMicroseconds!.Value/speech.Segments.Length;
        var cues=speech.Segments.Select((s,i)=>new SubtitleCue(s.ParagraphId,s.SourceStartScalar,s.SourceEndScalar,i*length,(i+1)*length,s.Text,s.Sha256)).ToImmutableArray();
        return Track(script,speech,audio,cues);
    }
    private static SubtitleTrack Track(OriginalScript script,SpeechProjection speech,AudioRegistration audio,ImmutableArray<SubtitleCue> cues)=>new(new("engineering-subtitles","1",AssetText.Hash(StrictJson.Serialize(cues))),script.Source,speech.Projection,audio.Asset!,cues);
    private static AssetRef Ref(string id,string version="1")=>new(id,version,AssetText.Hash(id+"@"+version));
    private static AssetDependency D(AssetRef reference,DependencyUse use)=>new(reference,use);
    private static AssetDirectory DirectoryFixture()
    {
        var text=Ref("text");var speech=Ref("speech");var audio=Ref("audio");var task=Ref("task");var exposure=Ref("subtitle-default");var decoration=Ref("decoration");
        AssetEntry E(AssetRef reference,AssetKind kind,params AssetDependency[] deps)=>new(reference,kind,"RIGHTS-CANDIDATE",deps.ToImmutableArray());
        return new([
            E(text,AssetKind.Original),E(task,AssetKind.TaskRule),E(exposure,AssetKind.ExposurePolicy),E(decoration,AssetKind.Decoration),
            E(speech,AssetKind.Speech,D(text,DependencyUse.OriginalText)),E(audio,AssetKind.Audio,D(text,DependencyUse.OriginalText),D(speech,DependencyUse.SpokenProjection)),
            E(Ref("subtitle"),AssetKind.Subtitle,D(text,DependencyUse.OriginalText),D(speech,DependencyUse.SpokenProjection),D(audio,DependencyUse.AudioBytes)),
            E(Ref("translation"),AssetKind.Translation,D(text,DependencyUse.OriginalText)),E(Ref("lexicon"),AssetKind.Lexicon,D(text,DependencyUse.OriginalText)),
            E(Ref("hint"),AssetKind.Hint,D(text,DependencyUse.OriginalText),D(task,DependencyUse.TaskRules)),
            E(Ref("calibration"),AssetKind.Calibration,D(text,DependencyUse.OriginalText),D(task,DependencyUse.TaskRules),D(exposure,DependencyUse.ExposurePolicy),D(audio,DependencyUse.AudioBytes)),
            E(Ref("audition"),AssetKind.Audition,D(audio,DependencyUse.AudioBytes)),E(Ref("decoration-consumer"),AssetKind.Visual,D(decoration,DependencyUse.Decoration))
        ],[new("RIGHTS-CANDIDATE","1","project_original_candidate","Declared engineering fixture source; not production ownership evidence","pending human verification","internal candidate only",["definition_en","meaning_zh","example_original"],"MistSea candidate",null,"pending_human_rights_review")]);
    }
    public static void Register()
    {
        Add("CX-066","Frozen six-turn Q102 original stays exact; raw speaker labels are detected and reviewed regeneration gets explicit source ranges",()=>
        {
            var material=Q102.Q102Cases.Catalog().Materials.Single(m=>m.Id=="Q102-L-REVISION");string before=StrictJson.Serialize(material);var service=new AssetPreparationService();var p=service.PrepareQ102Speech(material,"candidate-1");
            Require(p.Projection.Segments.Length==6&&p.ExistingRawSpeechIssues.Length==6&&p.CriticalDetails.Length==17);SpeechProjectionRules.Validate(p.Original,p.Projection);
            Require(p.Projection.Segments.Select(s=>s.SpeakerId).SequenceEqual(new[]{"dispatcher","electrician","dispatcher","electrician","dispatcher","electrician"}));
            Require(p.Projection.Segments.Select(s=>s.VoiceId).Distinct().Count()==2&&p.Projection.Segments.All(s=>!s.Text.StartsWith("Dispatcher:",StringComparison.Ordinal)&&!s.Text.StartsWith("Electrician:",StringComparison.Ordinal)));
            Require(StrictJson.Serialize(material)==before&&p.Original.Source.Sha256==material.BodySha256&&p.Pending.Length>0&&p.Projection.Projection.Sha256!=material.BodySha256);
            foreach(var s in p.Projection.Segments){var source=material.Paragraphs.Single(x=>x.Id==s.ParagraphId);Require(s.Text==UnicodeOffsets.Slice(source.Text,s.SourceStartScalar,s.SourceEndScalar));}
        });
        Add("CX-066","Source-range speech rejects removed negation, altered quantities, stale source and masked speaker; Unicode scalar ranges stay exact",()=>
        {
            var script=Script("Dispatcher: Do not exceed 1.2 m. 🙂 Keep it off.","Electrician: Only if both checks pass.");var speech=SpeechProjectionRules.Create(script,"1",AssetPreparationService.Q102Speakers);SpeechProjectionRules.Validate(script,speech);
            var first=speech.Segments[0];Require(first.SourceStartScalar==12&&first.Text.Contains("🙂",StringComparison.Ordinal));
            foreach(string text in new[]{first.Text.Replace("not ","",StringComparison.Ordinal),first.Text.Replace("1.2","2.1",StringComparison.Ordinal)}) Throws<InvalidDataException>(()=>SpeechProjectionRules.Validate(script,speech with{Segments=speech.Segments.SetItem(0,first with{Text=text,Sha256=AssetText.Hash(text)})}));
            Throws<InvalidDataException>(()=>SpeechProjectionRules.Validate(script,speech with{Source=script.Source with{Version="2"}}));
            Throws<InvalidDataException>(()=>SpeechProjectionRules.Create(script,"1",[new("","narrator","v"),new("Dispatcher:","dispatcher","v")]));
            Throws<InvalidDataException>(()=>SpeechProjectionRules.Create(script,"1",[new("Dispatcher:","dispatcher","v")]));
            Throws<InvalidDataException>(()=>SpeechProjectionRules.Create(Script("Do not exceed 1.2 m."),"1",[new("Do not ","invented-role","v")]));
            Throws<InvalidDataException>(()=>AssetText.Original(script with{Paragraphs=script.Paragraphs.SetItem(0,script.Paragraphs[0] with{Text=script.Paragraphs[0].Text+" "})}));
        });
        Add("CX-066","Script-only audio has null hash/bytes/duration; JSON audition claims and invented timecodes cannot certify a recording",()=>
        {
            var script=Script("Keep it off.");var speech=SpeechProjectionRules.Create(script,"1",[new("","narrator","v")]);var only=new AudioRegistration("not-rendered",null,script.Source,speech.Projection,AudioStage.ScriptOnly,null,null,null,"pending",null);AudioSubtitleRules.Audio(script,speech,only,null);
            Throws<InvalidDataException>(()=>AudioSubtitleRules.Audio(script,speech,only with{Asset=Ref("fake")},null));Throws<InvalidDataException>(()=>AudioSubtitleRules.Audio(script,speech,only with{DurationMicroseconds=1000},null));
            var facts=WaveInspector.Inspect(Wave());var audio=Audio(script,speech,facts);AudioSubtitleRules.Audio(script,speech,audio,facts);
            Throws<InvalidDataException>(()=>AudioSubtitleRules.Audio(script,speech,audio with{Stage=AudioStage.Auditioned,HumanAuditionEvidence="human_approved:true"},facts));
            Throws<InvalidDataException>(()=>AudioSubtitleRules.Audio(script,speech,audio with{Asset=audio.Asset! with{Sha256=AssetText.Hash("fake bytes")}},facts));
            Throws<InvalidDataException>(()=>AudioSubtitleRules.Subtitles(script,speech,only,null,Subtitles(script,speech,audio)));
        });
        Add("CX-066","Actual WAVE inspection uses byte frames and rejects truncation/inconsistent headers; parser fixtures do not become narration",()=>
        {
            var bytes=Wave();var facts=WaveInspector.Inspect(bytes);Require(facts.Bytes==bytes.Length&&facts.DurationMicroseconds==6_000_000&&facts.Sha256==AssetText.Hash(bytes)&&facts.SampleRate==8000&&facts.Channels==1);
            Throws<InvalidDataException>(()=>WaveInspector.Inspect(bytes.AsSpan(0,bytes.Length-1)));
            var invalid=bytes.ToArray();BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(28),17000);Throws<InvalidDataException>(()=>WaveInspector.Inspect(invalid));
            var empty=Wave(0);Throws<InvalidDataException>(()=>WaveInspector.Inspect(empty));
        });
        Add("CX-066","Subtitle validation binds actual measured audio plus full ordered original ranges and rejects overlaps, missing words and stale hashes",()=>
        {
            var script=Script("Dispatcher: Do not exceed 1.2 m.","Electrician: 🙂 Only if both checks pass.");var speech=SpeechProjectionRules.Create(script,"1",AssetPreparationService.Q102Speakers);var facts=WaveInspector.Inspect(Wave());var audio=Audio(script,speech,facts);var subs=Subtitles(script,speech,audio);AudioSubtitleRules.Subtitles(script,speech,audio,facts,subs);
            var first=subs.Cues[0];var second=subs.Cues[1];
            foreach(var cues in new[]{subs.Cues.RemoveAt(0),subs.Cues.SetItem(1,second with{StartMicroseconds=first.EndMicroseconds-1}),subs.Cues.SetItem(1,second with{EndMicroseconds=facts.DurationMicroseconds+1}),subs.Cues.SetItem(0,first with{SourceStartScalar=first.SourceStartScalar+1}),subs.Cues.SetItem(0,first with{Text=first.Text.Replace("not ","",StringComparison.Ordinal)}),subs.Cues.Reverse().ToImmutableArray()}) Throws<InvalidDataException>(()=>AudioSubtitleRules.Subtitles(script,speech,audio,facts,Track(script,speech,audio,cues)));
            Throws<InvalidDataException>(()=>AudioSubtitleRules.Subtitles(script,speech,audio,facts,subs with{Audio=subs.Audio with{Version="old"}}));
            Throws<InvalidDataException>(()=>AudioSubtitleRules.Subtitles(script,speech,audio,facts,subs with{Asset=subs.Asset with{Sha256=AssetText.Hash("different cues")}}));
            int middle=first.SourceStartScalar+5;string a=UnicodeOffsets.Slice(script.Paragraphs[0].Text,first.SourceStartScalar,middle),b=UnicodeOffsets.Slice(script.Paragraphs[0].Text,middle,first.SourceEndScalar);
            var split=ImmutableArray.Create(first with{SourceEndScalar=middle,EndMicroseconds=1_000_000,Text=a,Sha256=AssetText.Hash(a)},first with{SourceStartScalar=middle,StartMicroseconds=1_000_000,Text=b,Sha256=AssetText.Hash(b)},second);AudioSubtitleRules.Subtitles(script,speech,audio,facts,Track(script,speech,audio,split));
        });
        Add("CX-066","Critical listening checklist retains exact negation/quantity anchors and stays pending human full audition",()=>
        {
            var script=Script("Do not exceed 1.2 m unless cleared.");var speech=SpeechProjectionRules.Create(script,"1",[new("","narrator","v")]);var facts=WaveInspector.Inspect(Wave());var audio=Audio(script,speech,facts);
            var anchors=ImmutableArray.Create(new CriticalDetail("negation","p1",3,6,"negation","not",AssetText.Hash("not")),new CriticalDetail("quantity","p1",14,19,"number","1.2 m",AssetText.Hash("1.2 m")));
            var plan=AudioSubtitleRules.AuditPlan(script,speech,audio,facts,anchors);Require(plan.Status=="pending_human_full_audition"&&plan.Pending.Length==3&&plan.CriticalDetails.SequenceEqual(anchors));
            Throws<InvalidDataException>(()=>AudioSubtitleRules.AuditPlan(script,speech,audio,facts,anchors.SetItem(1,anchors[1] with{Text="2.1 m",Sha256=AssetText.Hash("2.1 m")})));
        });
        Add("CX-066","Rights references and permitted dictionary fields are checked while complete candidate provenance never self-approves voice or publication rights",()=>
        {
            var directory=DirectoryFixture();var audit=AssetDirectoryRules.Rights(directory);Require(audit.StructuralIssues.Any(i=>i.Code=="voice_consent_missing")&&audit.HumanPending.Length==directory.Entries.Length);
            var rights=directory.Rights[0];AssetDirectoryRules.FieldPermission(rights,"example_original");Throws<InvalidDataException>(()=>AssetDirectoryRules.FieldPermission(rights,"copied_commercial_example"));
            var missing=directory with{Entries=directory.Entries.SetItem(0,directory.Entries[0] with{RightsId="unknown"})};Require(AssetDirectoryRules.Rights(missing).StructuralIssues.Any(i=>i.Code=="rights_reference_missing"));
            var asserted=directory with{Rights=directory.Rights.SetItem(0,rights with{ReviewStatus="approved",VoiceConsentReference="claimed"})};Require(AssetDirectoryRules.Rights(asserted).StructuralIssues.Any(i=>i.Code=="rights_structure_or_claim"));
            foreach(string path in new[]{"../original.json","audio/../escape.wav","C:/outside.wav","audio\\escape.wav","fonts/candidate.ttf"}) Throws<InvalidDataException>(()=>AudioSubtitleRules.SafeAssetPath(path));
        });
        Add("CX-066","Original, audio and subtitle-exposure changes invalidate the actual dependent review set without changing old records or blessing replacements",()=>
        {
            var directory=DirectoryFixture();string before=StrictJson.Serialize(directory);AssetDirectoryRules.Validate(directory);
            var original=AssetDirectoryRules.Propagate(directory,[new(Ref("text"),Ref("text","2"))]);Require(original.Invalidations.Select(i=>i.Asset.Id).ToHashSet().SetEquals(new[]{"speech","audio","subtitle","translation","lexicon","hint","calibration","audition"})&&original.Invalidations.All(i=>i.Status=="pending_review"));
            var audio=AssetDirectoryRules.Propagate(directory,[new(Ref("audio"),Ref("audio","2"))]);Require(audio.Invalidations.Select(i=>i.Asset.Id).ToHashSet().SetEquals(new[]{"subtitle","calibration","audition"}));
            var exposure=AssetDirectoryRules.Propagate(directory,[new(Ref("subtitle-default"),Ref("subtitle-default","2"))]);Require(exposure.Invalidations.Select(i=>i.Asset.Id).SequenceEqual(new[]{"calibration"}));
            var decoration=AssetDirectoryRules.Propagate(directory,[new(Ref("decoration"),Ref("decoration","2"))]);Require(decoration.Invalidations.Length==0&&StrictJson.Serialize(directory)==before);
            Throws<InvalidDataException>(()=>AssetDirectoryRules.Propagate(directory,[new(Ref("text"),Ref("text") with{Sha256=AssetText.Hash("silently changed")})]));
        });
        Add("CX-066","Asset graph rejects omitted/stale dependency declarations and cycles; repeated template failures pause batch instead of regenerating",()=>
        {
            var directory=DirectoryFixture();int index=Array.FindIndex(directory.Entries.ToArray(),e=>e.Kind==AssetKind.Subtitle);
            Throws<InvalidDataException>(()=>AssetDirectoryRules.Validate(directory with{Entries=directory.Entries.SetItem(index,directory.Entries[index] with{Dependencies=directory.Entries[index].Dependencies.RemoveAt(2)})}));
            int speech=Array.FindIndex(directory.Entries.ToArray(),e=>e.Kind==AssetKind.Speech);Throws<InvalidDataException>(()=>AssetDirectoryRules.Validate(directory with{Entries=directory.Entries.SetItem(speech,directory.Entries[speech] with{Dependencies=[D(Ref("text","2"),DependencyUse.OriginalText)]})}));
            var a=new AssetEntry(Ref("a"),AssetKind.Decoration,"r",[D(Ref("b"),DependencyUse.Decoration)]);var b=new AssetEntry(Ref("b"),AssetKind.Decoration,"r",[D(Ref("a"),DependencyUse.Decoration)]);Throws<InvalidDataException>(()=>AssetDirectoryRules.Validate(new([a,b],[])));
            var paused=AssetDirectoryRules.Batch([("six-turn","role_label","a"),("six-turn","role_label","b"),("other","different","c")]);Require(paused.Paused&&paused.RepeatedCodes.SequenceEqual(new[]{"six-turn:role_label"}));Require(!AssetDirectoryRules.Batch([("six-turn","role_label","a"),("six-turn","role_label","a")]).Paused);
        });
        Add("CX-066","Existing real Q102 WAVE is audited as raw generation only; new projection export cannot overwrite artifacts or escape output directory",()=>
        {
            var material=Q102.Q102Cases.Catalog().Materials.Single(m=>m.Id=="Q102-L-REVISION");var service=new AssetPreparationService();string path=Path.Combine(ProjectRoot,"game/content",material.AudioAsset);string originalHash=AssetText.Hash(File.ReadAllBytes(path));
            var audit=Done(service.AuditExistingRawRenderingAsync(material,Path.Combine(ProjectRoot,"game/content"),material.AudioAsset,originalHash));Require(audit.ActualAudio.Bytes==new FileInfo(path).Length&&audit.ActualAudio.DurationMicroseconds>0&&audit.Issues.Length==6&&audit.GenerationInput=="raw_original_paragraphs_with_role_labels");
            Throws<InvalidDataException>(()=>Done(service.AuditExistingRawRenderingAsync(material,Path.Combine(ProjectRoot,"game/content"),material.AudioAsset,AssetText.Hash("different audio"))));
            string output=Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"asset-candidate-"+Guid.NewGuid());var candidate=service.PrepareQ102Speech(material,"candidate-1");string exported=Done(service.ExportNewArtifactAsync(output,"q102/regenerate-projection.json",candidate));Require(File.Exists(exported)&&StrictJson.Serialize(StrictJson.Deserialize<RegenerationPreparation>(File.ReadAllText(exported)))==StrictJson.Serialize(candidate));
            Throws<IOException>(()=>Done(service.ExportNewArtifactAsync(output,"q102/regenerate-projection.json",candidate)));Throws<InvalidDataException>(()=>Done(service.ExportNewArtifactAsync(output,"../escape.json",candidate)));Require(AssetText.Hash(File.ReadAllBytes(path))==originalHash);
            var parserScript=Script("Parser fixture, not narration.");var parserSpeech=SpeechProjectionRules.Create(parserScript,"1",[new("","narrator","v")]);var parserBytes=Wave();var parserFacts=WaveInspector.Inspect(parserBytes);var parserAudio=Audio(parserScript,parserSpeech,parserFacts);
            Directory.CreateDirectory(Path.Combine(output,"audio"));string parserPath=Path.Combine(output,"audio/engineering-parser-fixture.wav");File.WriteAllBytes(parserPath,parserBytes);
            Require(Done(service.ValidateSubtitlesAsync(parserScript,parserSpeech,parserAudio,Subtitles(parserScript,parserSpeech,parserAudio),output))==parserFacts);
            parserBytes[^1]=1;File.WriteAllBytes(parserPath,parserBytes);Throws<InvalidDataException>(()=>Done(service.ValidateAudioAsync(parserScript,parserSpeech,parserAudio,output)));
        });
    }
}
