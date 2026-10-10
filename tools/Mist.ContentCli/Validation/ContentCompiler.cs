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
        string scopedPersonalSource=Path.Combine(root,"content/source/scoped_personal_work/learner_package.json");
        var scopedPersonal=Mist.Core.ScopedPersonalWorks.ScopedPersonalPackage.ParseJson(File.ReadAllText(scopedPersonalSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(scopedPersonalSource)))DenyAuthorAnswers(projection.RootElement);
        string scopedPersonalKey="scoped-personal:"+scopedPersonal.Version,scopedPersonalHash=CatalogImporter.HashFile(scopedPersonalSource);
        if(history.TryGetValue(scopedPersonalKey,out var priorScopedPersonal)&&priorScopedPersonal!=scopedPersonalHash)throw new ContentException("Same scoped personal version changed.");
        string oldCityLifeSource=Path.Combine(root,"content/source/optional_local_works/old_city_life_contract.json");
        var oldCityLife=Mist.Core.OptionalLocalWorks.OldCityLifeContract.ParseJson(File.ReadAllText(oldCityLifeSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(oldCityLifeSource)))DenyAuthorAnswers(projection.RootElement);
        string oldCityLifeKey="old-city-life:"+oldCityLife.Version,oldCityLifeHash=CatalogImporter.HashFile(oldCityLifeSource);
        if(history.TryGetValue(oldCityLifeKey,out var priorOldCityLife)&&priorOldCityLife!=oldCityLifeHash)throw new ContentException("Same old-city life version changed.");
        string r3ShiftSource=Path.Combine(root,"content/source/r3_shift_work/learner_package.json");
        var r3Shift=Mist.Core.R3ShiftWork.R3ShiftPackage.ParseJson(File.ReadAllText(r3ShiftSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(r3ShiftSource)))DenyAuthorAnswers(projection.RootElement);
        string r3ShiftKey="r3-shift:"+r3Shift.Version,r3ShiftHash=CatalogImporter.HashFile(r3ShiftSource);
        if(history.TryGetValue(r3ShiftKey,out var priorR3Shift)&&priorR3Shift!=r3ShiftHash)throw new ContentException("Same R3 shift version changed.");
        string vesselLifeSource=Path.Combine(root,"content/source/vessel_life/learner_package.json");
        var vesselLife=Mist.Core.VesselLife.VesselLifePackage.ParseJson(File.ReadAllText(vesselLifeSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(vesselLifeSource)))DenyAuthorAnswers(projection.RootElement);
        string vesselLifeKey="vesselLife:"+vesselLife.Version,vesselLifeHash=CatalogImporter.HashFile(vesselLifeSource);
        if(history.TryGetValue(vesselLifeKey,out var priorvesselLife)&&priorvesselLife!=vesselLifeHash)throw new ContentException("Same vesselLife version changed.");
        string linaPersonalSource=Path.Combine(root,"content/source/lina_personal/learner_package.json");
        var linaPersonal=Mist.Core.LinaPersonalWorks.LinaWorkPackage.Parse(File.ReadAllText(linaPersonalSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(linaPersonalSource)))DenyAuthorAnswers(projection.RootElement);
        string linaPersonalKey="linaPersonal:"+linaPersonal.Version,linaPersonalHash=CatalogImporter.HashFile(linaPersonalSource);
        if(history.TryGetValue(linaPersonalKey,out var priorlinaPersonal)&&priorlinaPersonal!=linaPersonalHash)throw new ContentException("Same linaPersonal version changed.");
        string optionalContractsSource=Path.Combine(root,"content/source/optional_encounter_contracts/learner_package.json");
        var optionalContracts=Mist.Core.OptionalEncounterContracts.OptionalContractPackage.Parse(File.ReadAllText(optionalContractsSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(optionalContractsSource)))DenyAuthorAnswers(projection.RootElement);
        string optionalContractsKey="optionalContracts:"+optionalContracts.ContentVersion,optionalContractsHash=CatalogImporter.HashFile(optionalContractsSource);
        if(history.TryGetValue(optionalContractsKey,out var prioroptionalContracts)&&prioroptionalContracts!=optionalContractsHash)throw new ContentException("Same optionalContracts version changed.");
        string settlementSupportSource=Path.Combine(root,"content/source/optional_local_works/settlement_support_contracts.json");
        var settlementSupport=Mist.Core.OptionalLocalWorks.SettlementSupportContract.ParseJson(File.ReadAllText(settlementSupportSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(settlementSupportSource)))DenyAuthorAnswers(projection.RootElement);
        string settlementSupportKey="settlementSupport:"+settlementSupport.Version,settlementSupportHash=CatalogImporter.HashFile(settlementSupportSource);
        if(history.TryGetValue(settlementSupportKey,out var priorsettlementSupport)&&priorsettlementSupport!=settlementSupportHash)throw new ContentException("Same settlementSupport version changed.");
        string fieldRelaySource=Path.Combine(root,"content/source/optional_local_works/field_relay_contract.json");
        var fieldRelay=Mist.Core.OptionalLocalWorks.FieldRelayContract.ParseJson(File.ReadAllText(fieldRelaySource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(fieldRelaySource)))DenyAuthorAnswers(projection.RootElement);
        string fieldRelayKey="field-relay-contract:"+fieldRelay.Version,fieldRelayHash=CatalogImporter.HashFile(fieldRelaySource);
        if(history.TryGetValue(fieldRelayKey,out var priorFieldRelay)&&priorFieldRelay!=fieldRelayHash)throw new ContentException("Same field relay contract version changed.");
        string firstApproachSource=Path.Combine(root,"content/source/first_approaches/learner_package.json");
        var firstApproach=Mist.Core.FirstApproaches.FirstApproachPackage.Parse(File.ReadAllText(firstApproachSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(firstApproachSource)))DenyAuthorAnswers(projection.RootElement);
        foreach(var m in firstApproach.Materials)
        {
            if(!catalog.Scenes.Any(c=>c.Id==m.Scene))throw new ContentException("Early approach uses unregistered scene.");
            string key="first-approach:"+m.Id+"@"+m.Version,hash=CatalogImporter.Hash(string.Join("\n\n",m.Paragraphs.Select(p=>p.En))+"\n"+string.Join("\n\n",m.Paragraphs.Select(p=>p.Zh)));
            if(history.TryGetValue(key,out var old)&&old!=hash)throw new ContentException("Same approach material version changed.");
        }
        string r2LifeSource=Path.Combine(root,"content/source/r2_life/learner_contract.json");
        var r2Life=Mist.Core.R2Life.R2LifeContract.Parse(File.ReadAllText(r2LifeSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(r2LifeSource)))DenyAuthorAnswers(projection.RootElement);
        string r2LifeKey="r2-life-contract:"+r2Life.ContentVersion,r2LifeHash=CatalogImporter.HashFile(r2LifeSource);
        if(history.TryGetValue(r2LifeKey,out var oldContract)&&oldContract!=r2LifeHash)throw new ContentException("Same R2 local contract version changed.");
        string r1LifeSource=Path.Combine(root,"content/source/r1_life/learner_package.json");
        var r1Life=Mist.Core.R1Life.R1LifePackage.Parse(File.ReadAllText(r1LifeSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(r1LifeSource)))DenyAuthorAnswers(projection.RootElement);
        if(r1Life.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Id)))throw new ContentException("R1 optional space is not registered.");
        foreach(var m in r1Life.Materials)
        {
            string key="r1-life:"+m.Id+"@"+m.Version,hash=CatalogImporter.Hash(m.English+"\n"+m.Chinese);
            if(history.TryGetValue(key,out var old)&&old!=hash)throw new ContentException("Same local material ID/version changed: "+m.Id);
        }
        string vesselSource=Path.Combine(root,"content/source/vessel/vessel_interior.json");Mist.Core.VesselInterior.VesselInteriorPackage? vessel=null;
        if(File.Exists(vesselSource))
        {
            vessel=StrictJson.Deserialize<Mist.Core.VesselInterior.VesselInteriorPackage>(File.ReadAllText(vesselSource));Mist.Core.VesselInterior.VesselInteriorRules.Validate(vessel);
            if(vessel.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Id)))throw new ContentException("Ship interior has an unregistered scene.");
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(vesselSource));DenyAuthorAnswers(playerProjection.RootElement);
        }
        string chapterTwoSource=Path.Combine(root,"content/source/chapter_two/learner_package.json");Mist.Core.ChapterTwo.ChapterTwoPackage? chapterTwo=null;
        if(File.Exists(chapterTwoSource))
        {
            chapterTwo=Mist.Core.ChapterTwo.ChapterTwoPackage.ParseJson(File.ReadAllText(chapterTwoSource));
            if(chapterTwo.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Geometry.Id)))throw new ContentException("Second chapter has an unregistered scene.");
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(chapterTwoSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var material in chapterTwo.Materials)
            {
                string key="chapter-two:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same second-chapter material ID/version changed text: "+material.Id);
            }
        }
        string chapterThreeSource=Path.Combine(root,"content/source/chapter_three/learner_package.json");Mist.Core.ChapterThree.ChapterThreePackage? chapterThree=null;
        if(File.Exists(chapterThreeSource))
        {
            chapterThree=Mist.Core.ChapterThree.ChapterThreePackage.ParseJson(File.ReadAllText(chapterThreeSource));
            if(chapterThree.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Geometry.Id)))throw new ContentException("Third chapter has an unregistered scene.");
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(chapterThreeSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var material in chapterThree.Materials)
            {
                string key="chapter-three:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same third-chapter material ID/version changed text: "+material.Id);
            }
        }
        string chapterFourSource=Path.Combine(root,"content/source/chapter_four/learner_package.json");Mist.Core.ChapterFour.ChapterFourPackage? chapterFour=null;
        if(File.Exists(chapterFourSource))
        {
            chapterFour=Mist.Core.ChapterFour.ChapterFourPackage.ParseJson(File.ReadAllText(chapterFourSource));
            if(chapterFour.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Geometry.Id)))throw new ContentException("Fourth chapter has an unregistered scene.");
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(chapterFourSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var material in chapterFour.Materials)
            {
                string key="chapter-four:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same fourth-chapter material ID/version changed text: "+material.Id);
            }
        }
        string studySource=Path.Combine(root,"content/source/chapter_four/optional_inquiry.json");
        var study=Mist.Core.ChapterFour.StudyInquiryPackage.ParseJson(File.ReadAllText(studySource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(studySource)))DenyAuthorAnswers(projection.RootElement);
        foreach(var m in study.Materials)
        {
            string key="r4-study:"+m.Id+"@"+m.Version,hash=CatalogImporter.Hash(m.English+"\n"+m.Chinese);
            if(history.TryGetValue(key,out var old)&&old!=hash)throw new ContentException("Same public inquiry source changed: "+m.Id);
        }
        string creditSource=Path.Combine(root,"content/source/chapter_four/optional_credit.json");
        var credit=Mist.Core.ChapterFour.ExhibitionCreditPackage.ParseJson(File.ReadAllText(creditSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(creditSource)))DenyAuthorAnswers(projection.RootElement);
        foreach(var m in credit.Materials)
        {
            string key="r4-credit:"+m.Id+"@"+m.Version,hash=CatalogImporter.Hash(m.English+"\n"+m.Chinese);
            if(history.TryGetValue(key,out var old)&&old!=hash)throw new ContentException("Same exhibition source changed: "+m.Id);
        }
        string chapterFiveSource=Path.Combine(root,"content/source/chapter_five/learner_package.json");Mist.Core.ChapterFive.ChapterFivePackage? chapterFive=null;
        if(File.Exists(chapterFiveSource))
        {
            chapterFive=Mist.Core.ChapterFive.ChapterFivePackage.ParseJson(File.ReadAllText(chapterFiveSource));
            if(chapterFive.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Geometry.Id)))throw new ContentException("Fifth chapter has an unregistered scene.");
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(chapterFiveSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var material in chapterFive.Materials)
            {
                string key="chapter-five:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same fifth-chapter material ID/version changed text: "+material.Id);
            }
        }
        string chapterSixSource=Path.Combine(root,"content/source/chapter_six/learner_package.json");Mist.Core.ChapterSix.ChapterSixPackage? chapterSix=null;
        if(File.Exists(chapterSixSource))
        {
            chapterSix=Mist.Core.ChapterSix.ChapterSixPackage.ParseJson(File.ReadAllText(chapterSixSource));
            if(chapterSix.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Geometry.Id)))throw new ContentException("Sixth chapter has an unregistered scene.");
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(chapterSixSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var material in chapterSix.Materials)
            {
                string key="chapter-six:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same sixth-chapter material ID/version changed text: "+material.Id);
            }
        }
        string finaleSource=Path.Combine(root,"content/source/finale/learner_package.json");Mist.Core.Finale.FinalePackage? finale=null;
        if(File.Exists(finaleSource))
        {
            finale=Mist.Core.Finale.FinalePackage.ParseJson(File.ReadAllText(finaleSource));
            if(finale.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Geometry.Id)))throw new ContentException("Finale has an unregistered scene.");
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(finaleSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var material in finale.Materials)
            {
                string key="finale:"+material.Id+"@"+material.Version,hash=CatalogImporter.Hash(material.English+"\n"+material.Chinese);
                if(history.TryGetValue(key,out string? old)&&old!=hash)throw new ContentException("Same finale material ID/version changed text: "+material.Id);
            }
        }
        var regionalVoyages=new List<(string Source,string Target,Mist.Core.Voyage.RegionalVoyageDefinition Definition)>();
        string regionalVoyageSource=Path.Combine(root,"content/source/voyage/r2_r3_voyage.json");
        if(chapterThree is not null&&File.Exists(regionalVoyageSource))
        {
            var definition=StrictJson.Deserialize<Mist.Core.Voyage.RegionalVoyageDefinition>(File.ReadAllText(regionalVoyageSource));Mist.Core.Voyage.RegionalVoyageRules.Validate(definition);
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(regionalVoyageSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var source in definition.Sources)if(CatalogImporter.HashFile(Path.Combine(root,source.Path))!=source.Sha256)throw new ContentException("Regional voyage design source changed: "+source.Path);
            if(chapterTwo is null)throw new ContentException("Regional voyage source geometry is missing.");
            Mist.Core.Voyage.VoyageRules.SafeBerth(chapterThree.Scenes.Single(s=>s.Geometry.Id==definition.To+"-S01").Geometry,definition.BerthSpot);
            regionalVoyages.Add((regionalVoyageSource,"voyage/r2_r3_voyage.json",definition));
        }
        string fourthVoyageSource=Path.Combine(root,"content/source/voyage/r3_r4_voyage.json");
        if(chapterFour is not null&&File.Exists(fourthVoyageSource))
        {
            if(chapterThree is null)throw new ContentException("Fourth chapter voyage source geometry is missing.");
            var definition=StrictJson.Deserialize<Mist.Core.Voyage.RegionalVoyageDefinition>(File.ReadAllText(fourthVoyageSource));Mist.Core.Voyage.RegionalVoyageRules.Validate(definition);
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(fourthVoyageSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var source in definition.Sources)if(CatalogImporter.HashFile(Path.Combine(root,source.Path))!=source.Sha256)throw new ContentException("Regional voyage design source changed: "+source.Path);
            Mist.Core.Voyage.VoyageRules.SafeBerth(chapterFour.Scenes.Single(s=>s.Geometry.Id==definition.To+"-S01").Geometry,definition.BerthSpot);
            regionalVoyages.Add((fourthVoyageSource,"voyage/r3_r4_voyage.json",definition));
        }
        string fifthVoyageSource=Path.Combine(root,"content/source/voyage/r4_r5_voyage.json");
        if(chapterFive is not null&&File.Exists(fifthVoyageSource))
        {
            if(chapterFour is null)throw new ContentException("Fifth chapter voyage source geometry is missing.");
            var definition=StrictJson.Deserialize<Mist.Core.Voyage.RegionalVoyageDefinition>(File.ReadAllText(fifthVoyageSource));Mist.Core.Voyage.RegionalVoyageRules.Validate(definition);
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(fifthVoyageSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var source in definition.Sources)if(CatalogImporter.HashFile(Path.Combine(root,source.Path))!=source.Sha256)throw new ContentException("Regional voyage design source changed: "+source.Path);
            Mist.Core.Voyage.VoyageRules.SafeBerth(chapterFive.Scenes.Single(s=>s.Geometry.Id==definition.To+"-S01").Geometry,definition.BerthSpot);
            regionalVoyages.Add((fifthVoyageSource,"voyage/r4_r5_voyage.json",definition));
        }
        string sixthVoyageSource=Path.Combine(root,"content/source/voyage/r5_r6_voyage.json");
        if(chapterSix is not null&&File.Exists(sixthVoyageSource))
        {
            if(chapterFive is null)throw new ContentException("Sixth chapter voyage source geometry is missing.");
            var definition=StrictJson.Deserialize<Mist.Core.Voyage.RegionalVoyageDefinition>(File.ReadAllText(sixthVoyageSource));Mist.Core.Voyage.RegionalVoyageRules.Validate(definition);
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(sixthVoyageSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var source in definition.Sources)if(CatalogImporter.HashFile(Path.Combine(root,source.Path))!=source.Sha256)throw new ContentException("Regional voyage design source changed: "+source.Path);
            Mist.Core.Voyage.VoyageRules.SafeBerth(chapterSix.Scenes.Single(s=>s.Geometry.Id==definition.To+"-S01").Geometry,definition.BerthSpot);
            regionalVoyages.Add((sixthVoyageSource,"voyage/r5_r6_voyage.json",definition));
        }
        var regionalAudio=new List<Mist.Core.Assets.RegionalAudioBinding>();
        string regionalAudioIndex=Path.Combine(output,"audio/regional_generation.json");
        if(File.Exists(regionalAudioIndex))
        {
            var materials=(chapterTwo?.Materials??[]).Concat(chapterThree?.Materials??[]).Concat(chapterFour?.Materials??[]).Concat(chapterFive?.Materials??[]).Concat(chapterSix?.Materials??[]).Concat(finale?.Materials??[]).Concat(r1Life.Materials).ToDictionary(m=>m.Id,StringComparer.Ordinal);
            var bindings=StrictJson.Deserialize<Mist.Core.Assets.RegionalAudioBinding[]>(File.ReadAllText(regionalAudioIndex));Unique(bindings.Select(b=>b.MaterialId));
            foreach(var binding in bindings)
            {
                if(!materials.TryGetValue(binding.MaterialId,out var material))
                {
                    var original=campaign?.Materials.SingleOrDefault(m=>m.Id==binding.MaterialId);
                    if(original is null)throw new ContentException("Unregistered candidate audio source: "+binding.MaterialId);
                    string campaignFile=Path.Combine(output,binding.AudioPath);
                    if(!File.Exists(campaignFile))throw new ContentException("Campaign recording is missing.");
                    var campaignWave=Mist.Core.Assets.CampaignSpeechProjection.Validate(original,binding,File.ReadAllBytes(campaignFile));
                    regionalAudio.Add(binding);audioAssets.Add(new(binding.AudioPath,campaignWave.Sha256,campaignWave.Bytes,"audio"));audioBindings.Add(new(binding.MaterialId,binding.MaterialVersion,binding.TextSha256,binding.AudioPath,binding.AudioSha256));continue;
                }
                if(material.Kind!="listening_script")throw new ContentException("Only actual learner listening scripts can bind a regional candidate recording.");
                string file=Path.Combine(output,binding.AudioPath);if(!File.Exists(file))throw new ContentException("Regional candidate recording is missing.");
                var actual=Mist.Core.Assets.RegionalAudioRules.Validate(material,binding,File.ReadAllBytes(file));
                regionalAudio.Add(binding);audioAssets.Add(new(binding.AudioPath,actual.Sha256,actual.Bytes,"audio"));audioBindings.Add(new(binding.MaterialId,binding.MaterialVersion,binding.TextSha256,binding.AudioPath,binding.AudioSha256));
            }
        }
        string voyageSource=Path.Combine(root,"content/source/voyage/r1_r2_voyage.json");Mist.Core.Voyage.VoyagePackage? voyage=null;
        if(File.Exists(voyageSource))
        {
            voyage=StrictJson.Deserialize<Mist.Core.Voyage.VoyagePackage>(File.ReadAllText(voyageSource));Mist.Core.Voyage.VoyageRules.Validate(voyage);
            using var playerProjection=JsonDocument.Parse(File.ReadAllText(voyageSource));DenyAuthorAnswers(playerProjection.RootElement);
            foreach(var source in voyage.Sources)if(CatalogImporter.HashFile(Path.Combine(root,source.Path))!=source.Sha256)throw new ContentException("Voyage design source changed: "+source.Path);
            if(chapterTwo is null)throw new ContentException("First voyage requires the actual destination geometry package.");
            Mist.Core.Voyage.VoyageRules.SafeBerth(chapterTwo.Scenes.Single(s=>s.Geometry.Id=="R2-S01").Geometry,voyage.DestinationDuty.BerthSpot);
        }
        var blockers = ImmutableArray.CreateBuilder<string>();
        foreach (var m in catalog.Materials.Where(m => m.Modality == "listening"))
            if (m.AudioAsset is "missing" or "not_applicable" || !File.Exists(Path.Combine(output, m.AudioAsset))) blockers.Add("critical_audio_missing:" + m.Id + "@" + m.Version);
        blockers.Add("human_editorial_and_listening_review_missing");
        blockers.Add("empirical_6.5_calibration_missing");
        string lexiconSource=Path.Combine(root,"content/source/lexicon/learner_lexicon.json");
        if(!File.Exists(lexiconSource))throw new ContentException("Offline learner lexicon is missing.");
        var lexicon=Mist.Core.Learning.RegionalLexicon.Parse(File.ReadAllText(lexiconSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(lexiconSource)))DenyAuthorAnswers(projection.RootElement);
        foreach(var context in lexicon.Entries.SelectMany(e=>e.Contexts).DistinctBy(c=>c.SourceFile))
        {
            string source=Path.GetFullPath(Path.Combine(root,context.SourceFile));
            if(!source.StartsWith(Path.GetFullPath(Path.Combine(root,"content/source"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(source)||CatalogImporter.HashFile(source)!=context.SourceFileSha256)
                throw new ContentException("Lexicon prose source fingerprint changed: "+context.SourceFile);
        }
        string lexiconKey="lexicon:"+lexicon.ContentVersion,lexiconHash=CatalogImporter.HashFile(lexiconSource);
        if(history.TryGetValue(lexiconKey,out var oldLexicon)&&oldLexicon!=lexiconHash)throw new ContentException("Same lexicon version changed prose or senses.");
        blockers.Add("lexicon_human_semantic_review_pronunciation_and_calibration_pending");
        blockers.Add("publisher_signature_and_rights_review_missing");
        if(campaign is not null){blockers.Add("campaign_editorial_and_target_calibration_missing");foreach(var m in campaign.Materials.Where(m=>m.Modality=="listening_script"&&!regionalAudio.Any(a=>a.MaterialId==m.Id&&a.MaterialVersion==m.Version)))blockers.Add("campaign_audio_missing:"+m.Id+"@"+m.Version);}
        if(chapter is not null)blockers.Add("chapter_one_editorial_and_target_calibration_missing");
        if(vessel is not null)blockers.Add("vessel_interior_native_acceptance_missing");
        if(chapterTwo is not null){blockers.Add("chapter_two_editorial_and_target_calibration_missing");foreach(var m in chapterTwo.Materials.Where(m=>m.Kind=="listening_script"&&!regionalAudio.Any(a=>a.MaterialId==m.Id&&a.MaterialVersion==m.Version)))blockers.Add("chapter_two_audio_missing:"+m.Id+"@"+m.Version);}
        if(chapterThree is not null){blockers.Add("chapter_three_editorial_and_target_calibration_missing");foreach(var m in chapterThree.Materials.Where(m=>m.Kind=="listening_script"&&!regionalAudio.Any(a=>a.MaterialId==m.Id&&a.MaterialVersion==m.Version)))blockers.Add("chapter_three_audio_missing:"+m.Id+"@"+m.Version);}
        if(chapterFour is not null){blockers.Add("chapter_four_editorial_and_target_calibration_missing");foreach(var m in chapterFour.Materials.Where(m=>m.Kind=="listening_script"&&!regionalAudio.Any(a=>a.MaterialId==m.Id&&a.MaterialVersion==m.Version)))blockers.Add("chapter_four_audio_missing:"+m.Id+"@"+m.Version);}
        if(chapterFive is not null){blockers.Add("chapter_five_editorial_and_target_calibration_missing");foreach(var m in chapterFive.Materials.Where(m=>m.Kind=="listening_script"&&!regionalAudio.Any(a=>a.MaterialId==m.Id&&a.MaterialVersion==m.Version)))blockers.Add("chapter_five_audio_missing:"+m.Id+"@"+m.Version);}
        if(chapterSix is not null){blockers.Add("chapter_six_editorial_and_target_calibration_missing");foreach(var m in chapterSix.Materials.Where(m=>m.Kind=="listening_script"&&!regionalAudio.Any(a=>a.MaterialId==m.Id&&a.MaterialVersion==m.Version)))blockers.Add("chapter_six_audio_missing:"+m.Id+"@"+m.Version);}
        if(finale is not null){blockers.Add("finale_editorial_and_target_calibration_missing");foreach(var m in finale.Materials.Where(m=>m.Kind=="listening_script"&&!regionalAudio.Any(a=>a.MaterialId==m.Id&&a.MaterialVersion==m.Version)))blockers.Add("finale_audio_missing:"+m.Id+"@"+m.Version);}
        if(regionalAudio.Count>0)blockers.Add("regional_candidate_audio_audition_alignment_and_voice_rights_pending");
        if(regionalVoyages.Count>0)blockers.Add("regional_public_landing_duty_editorial_review_missing");
        if(voyage is not null)blockers.Add("voyage_local_duty_editorial_review_missing");
        blockers.Add("r1_life_original_editorial_language_calibration_and_audio_audition_pending");
        blockers.Add("exhibition_credit_original_editorial_and_native_validation_pending");
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
        {
            string target=Path.Combine(output,"r1_life/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(r1LifeSource,target,true);
            assets=assets.Add(new("r1_life/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in r1Life.Materials)history.TryAdd("r1-life:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        {
}
        {
            string target=Path.Combine(output,"vessel_life_player.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(vesselLifeSource,target,true);
            assets=assets.Add(new("vessel_life_player.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(vesselLifeKey,vesselLifeHash);
        }
        {
            string target=Path.Combine(output,"lina_personal_player.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(linaPersonalSource,target,true);
            assets=assets.Add(new("lina_personal_player.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(linaPersonalKey,linaPersonalHash);
        }
        {
            string target=Path.Combine(output,"optional_encounter_contracts/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(optionalContractsSource,target,true);
            assets=assets.Add(new("optional_encounter_contracts/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(optionalContractsKey,optionalContractsHash);
        }
        {
            string target=Path.Combine(output,"settlement_support_contracts.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(settlementSupportSource,target,true);
            assets=assets.Add(new("settlement_support_contracts.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(settlementSupportKey,settlementSupportHash);
        }
        {
}
        {
            string target=Path.Combine(output,"r3_shift_work/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(r3ShiftSource,target,true);
            assets=assets.Add(new("r3_shift_work/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(r3ShiftKey,r3ShiftHash);
        }
        {
}
        {
            string target=Path.Combine(output,"old_city_life_contract.json");File.Copy(oldCityLifeSource,target,true);
            assets=assets.Add(new("old_city_life_contract.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(oldCityLifeKey,oldCityLifeHash);
        }
        {
}
        {
            string target=Path.Combine(output,"scoped_personal_work/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(scopedPersonalSource,target,true);
            assets=assets.Add(new("scoped_personal_work/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(scopedPersonalKey,scopedPersonalHash);
        }
        {
            string relayTarget=Path.Combine(output,"field_relay_contract.json");File.Copy(fieldRelaySource,relayTarget,true);
            assets=assets.Add(new("field_relay_contract.json",CatalogImporter.HashFile(relayTarget),new FileInfo(relayTarget).Length,"runtime_rules"));history.TryAdd(fieldRelayKey,fieldRelayHash);
            string target=Path.Combine(output,"first_approaches/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(firstApproachSource,target,true);
            assets=assets.Add(new("first_approaches/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in firstApproach.Materials)history.TryAdd("first-approach:"+m.Id+"@"+m.Version,CatalogImporter.Hash(string.Join("\n\n",m.Paragraphs.Select(p=>p.En))+"\n"+string.Join("\n\n",m.Paragraphs.Select(p=>p.Zh))));
        }
        {
            string target=Path.Combine(output,"r2_life/learner_contract.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(r2LifeSource,target,true);
            assets=assets.Add(new("r2_life/learner_contract.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(r2LifeKey,r2LifeHash);
        }
        if(vessel is not null)
        {
            string vesselTarget=Path.Combine(output,"vessel/vessel_interior.json");Directory.CreateDirectory(Path.GetDirectoryName(vesselTarget)!);File.Copy(vesselSource,vesselTarget,true);
            assets=assets.Add(new("vessel/vessel_interior.json",CatalogImporter.HashFile(vesselTarget),new FileInfo(vesselTarget).Length,"runtime_rules"));
        }
        if(chapterTwo is not null)
        {
            string target=Path.Combine(output,"chapter_two/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(chapterTwoSource,target,true);
            assets=assets.Add(new("chapter_two/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in chapterTwo.Materials)history.TryAdd("chapter-two:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        if(voyage is not null)
        {
            string target=Path.Combine(output,"voyage/r1_r2_voyage.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(voyageSource,target,true);
            assets=assets.Add(new("voyage/r1_r2_voyage.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
        }
        if(chapterThree is not null)
        {
            string target=Path.Combine(output,"chapter_three/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(chapterThreeSource,target,true);
            assets=assets.Add(new("chapter_three/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in chapterThree.Materials)history.TryAdd("chapter-three:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        if(chapterFour is not null)
        {
            string target=Path.Combine(output,"chapter_four/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(chapterFourSource,target,true);
            assets=assets.Add(new("chapter_four/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in chapterFour.Materials)history.TryAdd("chapter-four:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        {
            string target=Path.Combine(output,"chapter_four/optional_credit.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(creditSource,target,true);
            assets=assets.Add(new("chapter_four/optional_credit.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in credit.Materials)history.TryAdd("r4-credit:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        {
            string target=Path.Combine(output,"chapter_four/optional_inquiry.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(studySource,target,true);
            assets=assets.Add(new("chapter_four/optional_inquiry.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in study.Materials)history.TryAdd("r4-study:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        if(chapterFive is not null)
        {
            string target=Path.Combine(output,"chapter_five/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(chapterFiveSource,target,true);
            assets=assets.Add(new("chapter_five/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in chapterFive.Materials)history.TryAdd("chapter-five:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        if(chapterSix is not null)
        {
            string target=Path.Combine(output,"chapter_six/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(chapterSixSource,target,true);
            assets=assets.Add(new("chapter_six/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in chapterSix.Materials)history.TryAdd("chapter-six:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        if(finale is not null)
        {
            string target=Path.Combine(output,"finale/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(finaleSource,target,true);
            assets=assets.Add(new("finale/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in finale.Materials)history.TryAdd("finale:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\n"+m.Chinese));
        }
        foreach(var definition in regionalVoyages)
        {
            string target=Path.Combine(output,definition.Target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(definition.Source,target,true);
            assets=assets.Add(new(definition.Target,CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
        }
        if(regionalAudio.Count>0)
        {
            string target=Path.Combine(output,"audio/regional_runtime_bindings.json");File.WriteAllText(target,StrictJson.Serialize(regionalAudio),new System.Text.UTF8Encoding(false));
            assets=assets.Add(new("audio/regional_runtime_bindings.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
        }
        string lexiconTarget=Path.Combine(output,"lexicon/learner_lexicon.json");Directory.CreateDirectory(Path.GetDirectoryName(lexiconTarget)!);File.Copy(lexiconSource,lexiconTarget,true);
        assets=assets.Add(new("lexicon/learner_lexicon.json",CatalogImporter.HashFile(lexiconTarget),new FileInfo(lexiconTarget).Length,"runtime_rules"));
        history.TryAdd(lexiconKey,lexiconHash);
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
