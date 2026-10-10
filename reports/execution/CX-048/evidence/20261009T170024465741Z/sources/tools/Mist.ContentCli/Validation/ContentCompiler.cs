using System.Collections.Immutable;
using System.Text.Json;
using Mist.ContentCli.Import;
using Mist.Core.Content;
using Mist.Core.Contracts;

namespace Mist.ContentCli.Validation;

public sealed record CompileResult(string Status, string ReleaseId, ImmutableArray<string> Errors, ImmutableArray<string> FormalBlockers);
public static class ContentCompiler
{
    public static void ValidateCatalog(RuntimeCatalog catalog, IReadOnlyDictionary<string, string>? existingVersions = null)
    {
        catalog.Target.Validate();
        if (catalog.SchemaVersion != 1 || string.IsNullOrWhiteSpace(catalog.ReleaseId)) throw new ContentException("Unsupported catalog schema/release.");
        Unique(catalog.Scenes.Select(s => s.Id)); Unique(catalog.Quests.Select(q => q.Id)); Unique(catalog.Materials.Select(m => m.Id));
        foreach (var category in catalog.Definitions.Values) Unique(category.Select(d => d.Id));
        var scenes = catalog.Scenes.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var quests = catalog.Quests.ToDictionary(q => q.Id, StringComparer.Ordinal);
        var materialTasks = quests.Keys.ToHashSet(StringComparer.Ordinal);
        foreach(var puzzle in catalog.Definitions["puzzles"])
        {
            using var definition=JsonDocument.Parse(puzzle.PayloadJson);
            materialTasks.Add(definition.RootElement.GetProperty("quest").GetString()!);
        }
        if (quests.Count != 32) throw new ContentException("The source contract requires 32 main containers.");
        foreach (var q in catalog.Quests)
        {
            q.Reward.Bundle.Validate();
            if (q.Reward.QuestId != q.Id || q.Reward.Bundle.Entitlement != q.Id + ":resolution") throw new ContentException("Reward entitlement mismatch.");
            if (q.Scenes.Any(s => !scenes.Contains(s)) || q.ActivationAfter.Concat(q.CloseAfter).Any(d => !quests.ContainsKey(d))) throw new ContentException("Dangling quest/scene reference: " + q.Id);
        }
        var visiting = new HashSet<string>(); var visited = new HashSet<string>();
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new ContentException("Quest condition graph cycle at " + id);
            foreach (string parent in quests[id].ActivationAfter.Concat(quests[id].CloseAfter).Distinct()) Visit(parent);
            visiting.Remove(id); visited.Add(id);
        }
        foreach (var q in quests.Values) Visit(q.Id);
        foreach (var c in catalog.Connections) if (!scenes.Contains(c.A) || !scenes.Contains(c.B)) throw new ContentException("Dangling connection.");
        foreach (var m in catalog.Materials)
        {
            if (m.TargetBand != 6.5m || m.Version.Length == 0 || m.FamilyId.Length == 0 || m.QuestId is not null && !materialTasks.Contains(m.QuestId)) throw new ContentException("Material identity/target/reference invalid: " + m.Id);
            Unique(m.Paragraphs.Select(p => p.Id));
            if (m.Paragraphs.Length == 0 || m.Paragraphs.Any(p => CatalogImporter.Hash(p.Text) != p.Sha256) || CatalogImporter.Hash(string.Join("\n", m.Paragraphs.Select(p => p.Text))) != m.BodySha256) throw new ContentException("Material text checksum mismatch: " + m.Id);
            if (existingVersions is not null && existingVersions.TryGetValue(m.Id + "@" + m.Version, out string? known) && known != m.BodySha256) throw new ContentException("Same material ID/version changed text: " + m.Id);
            if (m.Id.StartsWith("SYNTHETIC", StringComparison.OrdinalIgnoreCase)) throw new ContentException("SYNTHETIC materials belong only in test fixtures.");
        }
        foreach (var definition in catalog.Definitions.Values.SelectMany(x => x))
        {
            using var json = JsonDocument.Parse(definition.PayloadJson); StrictJson.CheckDuplicates(json.RootElement); DenyAuthorAnswers(json.RootElement);
        }
    }
    private static void Unique(IEnumerable<string> ids)
    {
        var list = ids.ToArray();
        if (list.Any(string.IsNullOrWhiteSpace) || list.Distinct(StringComparer.Ordinal).Count() != list.Length) throw new ContentException("Empty or duplicate content IDs.");
    }
    private static void DenyAuthorAnswers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject())
            {
                if (p.Name is "illustrative_response" or "author_answer" or "answer_key" or "essential_features" or "meaning_requirements") throw new ContentException("Author solution entered player projection: " + p.Name);
                DenyAuthorAnswers(p.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) DenyAuthorAnswers(item);
    }
    public static CompileResult Compile(string root, string output, string releaseId, bool formal)
    {
        var catalog = CatalogImporter.Import(root, releaseId);
        var audioAssets=ImmutableArray.CreateBuilder<ReleaseAsset>(); var audioBindings=ImmutableArray.CreateBuilder<AudioBinding>();
        string audioIndex=Path.Combine(output,"audio/generation.json");
        if(File.Exists(audioIndex))
        {
            using var audio=JsonDocument.Parse(File.ReadAllText(audioIndex)); StrictJson.CheckDuplicates(audio.RootElement);
            foreach(var track in audio.RootElement.EnumerateArray())
            {
                string id=track.GetProperty("material_id").GetString()!,version=track.GetProperty("material_version").GetString()!,path=track.GetProperty("audio_path").GetString()!,textHash=track.GetProperty("text_sha256").GetString()!,hash=track.GetProperty("audio_sha256").GetString()!;
                var material=catalog.Materials.Single(m=>m.Id==id&&m.Version==version);
                if(material.BodySha256!=textHash||!path.StartsWith("audio/",StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\')||Path.IsPathRooted(path))throw new ContentException("Generated audio text/version/path mismatch.");
                string file=Path.Combine(output,path); if(!File.Exists(file)||CatalogImporter.HashFile(file)!=hash)throw new ContentException("Generated audio checksum mismatch.");
                catalog=catalog with { Materials=catalog.Materials.SetItem(catalog.Materials.IndexOf(material),material with { AudioAsset=path }) };
                audioAssets.Add(new(path,hash,new FileInfo(file).Length,"audio")); audioBindings.Add(new(id,version,textHash,path,hash));
            }
        }
        string historyPath = Path.Combine(root, "content/manifests/material_history.json");
        string dictionaryIndex=Path.Combine(output,"audio/dictionary_generation.json");
        if(File.Exists(dictionaryIndex))
        {
            using var clips=JsonDocument.Parse(File.ReadAllText(dictionaryIndex));StrictJson.CheckDuplicates(clips.RootElement);
            foreach(var clip in clips.RootElement.EnumerateArray())
            {
                string word=clip.GetProperty("word").GetString()!,path=clip.GetProperty("audio_path").GetString()!,hash=clip.GetProperty("audio_sha256").GetString()!;
                if(!System.Text.RegularExpressions.Regex.IsMatch(word,"^[a-z]{1,24}$")||path!="audio/dictionary-"+word+".wav")throw new ContentException("Dictionary audio path invalid.");
                string file=Path.Combine(output,path);if(!File.Exists(file)||CatalogImporter.HashFile(file)!=hash)throw new ContentException("Dictionary audio checksum mismatch.");
                audioAssets.Add(new(path,hash,new FileInfo(file).Length,"dictionary_audio"));
            }
        }
        var history = File.Exists(historyPath) ? StrictJson.Deserialize<Dictionary<string, string>>(File.ReadAllText(historyPath)) : new Dictionary<string, string>();
        ValidateCatalog(catalog, history);
        string campaignSource=Path.Combine(root,"content/source/campaign/first_entry.json");Mist.Core.Campaign.CampaignPackage? campaign=null;
        if(File.Exists(campaignSource))
        {
            campaign=StrictJson.Deserialize<Mist.Core.Campaign.CampaignPackage>(File.ReadAllText(campaignSource));Mist.Core.Campaign.CampaignRules.Validate(campaign);
            foreach(var material in campaign.Materials)
            {
                string key="campaign:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same campaign material ID/version changed text: "+material.Id);
            }
        }
        string chapterSource=Path.Combine(root,"content/source/chapter_one/chapter_one.json");Mist.Core.ChapterOne.ChapterOnePackage? chapter=null;
        if(File.Exists(chapterSource))
        {
            chapter=StrictJson.Deserialize<Mist.Core.ChapterOne.ChapterOnePackage>(File.ReadAllText(chapterSource));Mist.Core.ChapterOne.ChapterOneRules.Validate(chapter);
            foreach(var material in chapter.Materials)
            {
                string key="chapter-one:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same chapter material ID/version changed text: "+material.Id);
            }
        }
        var blockers = ImmutableArray.CreateBuilder<string>();
        foreach (var m in catalog.Materials.Where(m => m.Modality == "listening"))
            if (m.AudioAsset is "missing" or "not_applicable" || !File.Exists(Path.Combine(output, m.AudioAsset))) blockers.Add("critical_audio_missing:" + m.Id + "@" + m.Version);
        blockers.Add("human_editorial_and_listening_review_missing");
        blockers.Add("empirical_6.5_calibration_missing");
        blockers.Add("publisher_signature_and_rights_review_missing");
        if(campaign is not null){blockers.Add("campaign_editorial_and_target_calibration_missing");foreach(var m in campaign.Materials.Where(m=>m.Modality=="listening_script"))blockers.Add("campaign_audio_missing:"+m.Id+"@"+m.Version);}
        if(chapter is not null)blockers.Add("chapter_one_editorial_and_target_calibration_missing");
        if (formal) return new("formal_release_blocked", releaseId, [], blockers.ToImmutable());
        Directory.CreateDirectory(output);
        string catalogPath = Path.Combine(output, "runtime_catalog.json");
        File.WriteAllText(catalogPath, StrictJson.Serialize(catalog), new System.Text.UTF8Encoding(false));
        var asset = new ReleaseAsset("runtime_catalog.json", CatalogImporter.HashFile(catalogPath), new FileInfo(catalogPath).Length, "runtime_rules");
        var assets=ImmutableArray.Create(asset).AddRange(audioAssets);
        if(campaign is not null)
        {
            string campaignTarget=Path.Combine(output,"campaign/first_entry.json");Directory.CreateDirectory(Path.GetDirectoryName(campaignTarget)!);File.Copy(campaignSource,campaignTarget,true);
            assets=assets.Add(new("campaign/first_entry.json",CatalogImporter.HashFile(campaignTarget),new FileInfo(campaignTarget).Length,"runtime_rules"));
            foreach(var m in campaign.Materials)history.TryAdd("campaign:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        if(chapter is not null)
        {
            string chapterTarget=Path.Combine(output,"chapter_one/chapter_one.json");Directory.CreateDirectory(Path.GetDirectoryName(chapterTarget)!);File.Copy(chapterSource,chapterTarget,true);
            assets=assets.Add(new("chapter_one/chapter_one.json",CatalogImporter.HashFile(chapterTarget),new FileInfo(chapterTarget).Length,"runtime_rules"));
            foreach(var m in chapter.Materials)history.TryAdd("chapter-one:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        var manifest = new ReleaseManifest(1, releaseId, "mist-rules-1", "Godot-4.7.2-net10-schema1", new(), "internal_candidate", assets, [], [], "unsigned-internal", null, audioBindings.ToImmutable());
        File.WriteAllText(Path.Combine(output, "manifest.json"), StrictJson.Serialize(manifest), new System.Text.UTF8Encoding(false));
        Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
        foreach (var m in catalog.Materials) history.TryAdd(m.Id + "@" + m.Version, m.BodySha256);
        File.WriteAllText(historyPath, StrictJson.Serialize(history));
        var result = new CompileResult("internal_candidate", releaseId, [], blockers.ToImmutable());
        File.WriteAllText(Path.Combine(output, "compile_result.json"), StrictJson.Serialize(result));
        return result;
    }
}
