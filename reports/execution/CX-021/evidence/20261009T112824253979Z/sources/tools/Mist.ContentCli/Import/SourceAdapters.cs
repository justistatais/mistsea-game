using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Core.Content;
using Mist.Core.Contracts;

namespace Mist.ContentCli.Import;

public interface ISourceAdapter { int Part { get; } string Member { get; } JsonElement Read(string root); }
public abstract class SourceAdapter(int part, string member, string keys) : ISourceAdapter
{
    public int Part => part;
    public string Member => member;
    public JsonElement Read(string root)
    {
        string path = Path.Combine(root, "reference", $"part{Part:00}", Member);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        StrictJson.CheckDuplicates(doc.RootElement);
        var expected = keys.Split('|').ToHashSet(StringComparer.Ordinal);
        var actual = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected)) throw new ContentException($"Part{Part:00}: missing or unknown source fields: " + string.Join(',', actual.SymmetricExcept(expected)));
        return doc.RootElement.Clone();
    }
}
internal static class SetExtensions
{
    public static IEnumerable<T> SymmetricExcept<T>(this HashSet<T> a, HashSet<T> b) => a.Except(b).Concat(b.Except(a));
}
public sealed class Part03Adapter() : SourceAdapter(3, "Exploration_Layout_Reference_v0.1.json", "meta|regions|slice|world_travel|guard_bans");
public sealed class Part04Adapter() : SourceAdapter(4, "data/Quest_Event_Reference_v0.1.json", "meta|task_lifecycle|resolution_outcomes|derived_active_status|objective_types|quests|q102|event_templates|event_slots_note|work_orders|revision_bridge");
public sealed class Part05Adapter() : SourceAdapter(5, "data/English_Learning_Reference_v0.1.json", "meta|target|skills|curriculum|material_specs|content_budget|review_policy|assessment");
public sealed class Part06Adapter() : SourceAdapter(6, "data/Puzzle_Reference_v0.1.json", "meta|target|families|units|chapter_bindings|complexity_levels|complexity_note|budget|outcome_categories|support|banned_gates|exports|not_exports|constraint_registry|constraint_registry_note|worked_hint_example");
public sealed class Part07Adapter() : SourceAdapter(7, "data/Character_Progression_Reference_v0.1.json", "meta|level|formula|combat_contract|ranks|chapter_budgets|main_rewards|skills|items|modules|consumables|builds|engineering|economy|hotel|validation_limits|companions|calibration_sources|starter_owned|guaranteed_gear_unlocks|field_actions|trade_rules|schema_contract_notes");
public sealed class Part08Adapter() : SourceAdapter(8, "data/Combat_Reference_v0.1.json", "meta|turn|difficulty|actions|companions|companion_variants|archetypes|encounters|scenarios|builds|core|limits|inherited");
public sealed class Part09Adapter() : SourceAdapter(9, "data/NPC_Faction_Reference_v0.1.json", "meta|factions|profiles|supporting_profiles|arcs|personal_nodes|main_reactions|facts|banter_seeds|regional_mandates|cooperation_scopes|relationship|recruitment|reward_budget|ownership");
public sealed class Part10Adapter() : SourceAdapter(10, "data/UI_UX_Reference_v0.1.json", "version|target_band|scope|navigation|screens|components|bindings|tokens|save_slots|input_modes|readonly_in_combat|view_only_actions|forbidden_commit_dependencies|ui_prototype|layouts");
public sealed class Part11Adapter() : SourceAdapter(11, "data/Content_AI_Reference_v0.1.json", "meta|target|content_counts|state_axes|human_gates|runtime_write_domains|band_output_enabled|max_primary_feedback_items|max_expanded_feedback_items|max_runtime_context_segments|max_runtime_attempts|max_generation_candidates_per_job_initial|production_batch_initial|cost_model");

public static class CatalogImporter
{
    public static readonly ISourceAdapter[] Adapters = [new Part03Adapter(), new Part04Adapter(), new Part05Adapter(), new Part06Adapter(), new Part07Adapter(), new Part08Adapter(), new Part09Adapter(), new Part10Adapter(), new Part11Adapter()];
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string S(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new ContentException("Required text: " + name);
    private static ImmutableArray<string> Strings(JsonElement element, string name) => element.GetProperty(name).EnumerateArray().Select(e => e.GetString() ?? throw new ContentException("Null ID.")).ToImmutableArray();
    public static RuntimeCatalog Import(string root, string releaseId)
    {
        var parts = Adapters.ToDictionary(a => a.Part, a => a.Read(root));
        ValidateTargets(parts);
        string importPath = Path.Combine(root, "content/source");
        Directory.CreateDirectory(importPath);
        foreach (var adapter in Adapters)
        {
            string sourceFile = Path.Combine(root, "reference", $"part{adapter.Part:00}", adapter.Member), destination = Path.Combine(importPath, $"part{adapter.Part:00}.json");
            if (File.Exists(destination) && HashFile(destination) != HashFile(sourceFile)) throw new ContentException("Frozen source import changed; create an explicit new import version.");
            if (!File.Exists(destination)) File.Copy(sourceFile, destination);
        }
        var scenes = parts[3].GetProperty("regions").EnumerateArray().SelectMany(r => r.GetProperty("scenes").EnumerateArray().Select(s => new SceneDefinition(S(s, "id"), S(r, "id"), S(s, "name"), S(s, "play_actions"), Strings(s, "story_nodes")))).ToImmutableArray();
        var connections = parts[3].GetProperty("regions").EnumerateArray().SelectMany(r => r.GetProperty("connections").EnumerateArray().Select(c => new ConnectionDefinition(S(c, "a"), S(c, "b"), S(c, "gate_contract"), S(c, "return_contract")))).ToImmutableArray();
        var rewardMap = parts[7].GetProperty("main_rewards").EnumerateArray().Select(r =>
        {
            if (r.GetProperty("language_multiplier").GetDecimal() != 1 || r.GetProperty("method_multiplier").GetDecimal() != 1 || S(r, "source") != "world_resolution_only") throw new ContentException("Reward source/multiplier changed.");
            var reward = new RewardBundle(r.GetProperty("xp").GetInt32(), r.GetProperty("marks").GetInt64(), r.GetProperty("parts").GetInt32(), S(r, "entitlement_key")); reward.Validate();
            return new RewardDefinition(S(r, "quest_id"), S(r, "old_reward_ref"), reward, Strings(r, "awarded_items"));
        }).ToDictionary(r => r.QuestId, StringComparer.Ordinal);
        var quests = parts[4].GetProperty("quests").EnumerateArray().Select(q =>
        {
            string id = S(q, "id"), legacy = S(q, "reward_ref");
            if (!rewardMap.TryGetValue(id, out var reward) || reward.LegacyReference != legacy) throw new ContentException("Unmapped legacy reward: " + legacy);
            return new QuestDefinition(id, S(q, "title"), Strings(q, "activation_after"), Strings(q, "additional_close_after"), Strings(q, "scene_refs"), S(q, "close_contract"), reward);
        }).ToImmutableArray();
        string materialPath = Path.Combine(root, "reference/part05/data/Original_Materials_v0.1.json");
        using var original = JsonDocument.Parse(File.ReadAllText(materialPath)); StrictJson.CheckDuplicates(original.RootElement);
        var materials = original.RootElement.GetProperty("materials").EnumerateArray().Select(m =>
        {
            var paragraphs = m.GetProperty("paragraphs").EnumerateArray().Select(p => new ParagraphDefinition(S(p, "id"), S(p, "text"), Hash(S(p, "text")))).ToImmutableArray();
            if (m.GetProperty("target_band").GetDecimal() != 6.5m) throw new ContentException("Material target changed.");
            string audio = m.GetProperty("audio_asset").ValueKind == JsonValueKind.Null ? "missing" : S(m, "audio_asset");
            string? quest = m.GetProperty("quest_ref").ValueKind == JsonValueKind.Null ? null : S(m, "quest_ref");
            return new MaterialDefinition(S(m, "id"), S(m, "version"), S(m, "title"), S(m, "modality"), 6.5m, S(m, "difficulty_status"), S(m, "family_id"), quest, paragraphs, audio, Hash(string.Join("\n", paragraphs.Select(p => p.Text))));
        }).ToImmutableArray();
        VerifyProjection(root, materials);
        var definitions = ImmutableDictionary.CreateBuilder<string, ImmutableArray<NamedDefinition>>();
        foreach (var binding in new (int Part, string Source, string Destination)[] {
            (5,"skills","language_skills"),(6,"units","puzzles"),(7,"skills","talents"),(7,"items","items"),(7,"modules","modules"),(7,"consumables","consumables"),
            (8,"actions","combat_actions"),(8,"encounters","encounters"),(8,"scenarios","combat_scenarios"),(9,"profiles","npcs"),(9,"supporting_profiles","supporting_npcs"),(9,"factions","factions"),(9,"arcs","private_arcs"),(9,"personal_nodes","personal_nodes"),(10,"screens","screens") })
        {
            var list = parts[binding.Part].GetProperty(binding.Source).EnumerateArray().Select(d => new NamedDefinition(S(d, "id"), d.TryGetProperty("name", out var name) ? name.GetString()! : d.TryGetProperty("title", out var title) ? title.GetString()! : S(d, "id"), binding.Destination, d.GetRawText())).ToImmutableArray();
            definitions.Add(binding.Destination, list);
        }
        // Explicit non-player schemas; worked_hint_example and illustrative_response are never projected.
        foreach (var key in new[] { "level", "formula", "engineering", "economy", "hotel", "starter_owned", "guaranteed_gear_unlocks", "trade_rules" }) definitions.Add("progression_" + key, [new(key, key, "rules", parts[7].GetProperty(key).GetRawText())]);
        foreach (var key in new[] { "turn", "core", "companions", "companion_variants", "limits" }) definitions.Add("combat_" + key, [new(key, key, "rules", parts[8].GetProperty(key).GetRawText())]);
        foreach (var key in new[] { "relationship", "recruitment", "reward_budget", "ownership", "regional_mandates", "cooperation_scopes", "facts", "main_reactions", "banter_seeds" }) definitions.Add("npc_" + key, [new(key, key, "rules", parts[9].GetProperty(key).GetRawText())]);
        definitions.Add("ui_tokens", [new("tokens", "tokens", "rules", parts[10].GetProperty("tokens").GetRawText())]);
        var slice = parts[3].GetProperty("slice");
        var provenance = Adapters.Select(a => { string relative = $"reference/part{a.Part:00}/" + a.Member; return new SourceProvenance(a.Part, relative, HashFile(Path.Combine(root, relative)), a.GetType().Name); }).ToImmutableArray();
        return new(1, releaseId, new(), provenance, scenes, connections, quests, materials, definitions.ToImmutable(), slice.GetProperty("geometry").GetRawText(), slice.GetProperty("initial_world").GetRawText(), slice.GetProperty("facts").GetRawText(), S(original.RootElement.GetProperty("writing").GetProperty("q102"), "prompt"));
    }
    private static void ValidateTargets(Dictionary<int, JsonElement> parts)
    {
        foreach (int part in new[] { 5, 6, 11 })
        {
            var t = parts[part].GetProperty("target");
            if (new[] { "reading", "listening", "writing" }.Any(k => t.GetProperty(k).GetDecimal() != 6.5m)) throw new ContentException("RLW target must remain 6.5.");
            string auto = part == 5 ? "automatic_mainline_band_escalation" : part == 6 ? "auto_escalation" : "auto_escalate";
            if (t.GetProperty(auto).GetBoolean()) throw new ContentException("Automatic band escalation must remain disabled.");
        }
        if (parts[10].GetProperty("target_band").GetDecimal() != 6.5m || parts[11].GetProperty("band_output_enabled").GetBoolean()) throw new ContentException("Uncalibrated band output is forbidden.");
    }
    private static void VerifyProjection(string root, ImmutableArray<MaterialDefinition> materials)
    {
        using var ui = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reference/part10/data/Q102_Material_Reference_v0.1.json")));
        foreach (var projection in ui.RootElement.GetProperty("materials").EnumerateArray())
        {
            string id = projection.TryGetProperty("material_id", out var property) ? property.GetString()! : S(projection, "id");
            var source = materials.Single(m => m.Id == id);
            var projected = projection.GetProperty("paragraphs").EnumerateArray().ToArray();
            if (projected.Length != source.Paragraphs.Length || projected.Where((p, i) => S(p, "text") != source.Paragraphs[i].Text).Any()) throw new ContentException("English text changed in UI projection: " + id);
        }
    }
}
