using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mist.ContentCli.Import;
using Mist.ContentCli.Validation;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Infrastructure.Content;

namespace Mist.Tests.Content;

public static class ContentCases
{
    public static string ProjectRoot()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "CODEX_START_HERE.md"))) return path.FullName;
        throw new InvalidOperationException("Project root not found.");
    }
    private static string Temp(string name)
    {
        string path = Path.Combine(System.Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT") ?? Path.GetTempPath(), name + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path;
    }
    private static RuntimeCatalog Catalog() => CatalogImporter.Import(ProjectRoot(), "mistsea-internal-0.1");
    private static string CloneSources()
    {
        string root = Temp("source-clone");
        foreach (string relative in CatalogImporter.Adapters.Select(a => $"reference/part{a.Part:00}/" + a.Member).Append("reference/part05/data/Original_Materials_v0.1.json").Append("reference/part10/data/Q102_Material_Reference_v0.1.json"))
        {
            string destination = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(ProjectRoot(), relative), destination);
        }
        return root;
    }
    public static void Register()
    {
        TestRegistry.Add("CX-009", "Nine source adapters preserve English text and real reward mapping", () =>
        {
            var catalog = Catalog();
            TestRegistry.Require(catalog.Sources.Length == 9 && catalog.Quests.Length == 32);
            TestRegistry.Require(catalog.Quests.Sum(q => q.Reward.Bundle.Experience) == 19140 && catalog.Quests.Single(q => q.Id == "Q-102").Reward.Bundle.Experience == 160);
            using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectRoot(), "reference/part05/data/Original_Materials_v0.1.json")));
            foreach (var m in original.RootElement.GetProperty("materials").EnumerateArray())
            {
                var projected = catalog.Materials.Single(x => x.Id == m.GetProperty("id").GetString()); int index = 0;
                foreach (var p in m.GetProperty("paragraphs").EnumerateArray()) TestRegistry.Require(p.GetProperty("text").GetString() == projected.Paragraphs[index++].Text);
            }
            string serialized = StrictJson.Serialize(catalog);
            TestRegistry.Require(!serialized.Contains("illustrative_response") && !serialized.Contains("answer_key") && !serialized.Contains("meaning_requirements"));
        });
        TestRegistry.Add("CX-009", "Unknown source fields and unmapped legacy rewards are rejected", () =>
        {
            string root = CloneSources(), exploration = Path.Combine(root, "reference/part03/Exploration_Layout_Reference_v0.1.json");
            var node = JsonNode.Parse(File.ReadAllText(exploration))!.AsObject(); node["new_unknown_coins"] = 999; File.WriteAllText(exploration, node.ToJsonString());
            TestRegistry.Throws<ContentException>(() => new Part03Adapter().Read(root));
            root = CloneSources(); string rewards = Path.Combine(root, "reference/part07/data/Character_Progression_Reference_v0.1.json");
            node = JsonNode.Parse(File.ReadAllText(rewards))!.AsObject(); node["main_rewards"]!.AsArray().RemoveAt(0); File.WriteAllText(rewards, node.ToJsonString());
            TestRegistry.Throws<ContentException>(() => CatalogImporter.Import(root, "bad-reward-map"));
        });
        TestRegistry.Add("CX-010", "Compiler refuses changed same-version text, broken graph and SYNTHETIC", () =>
        {
            var catalog = Catalog(); ContentCompiler.ValidateCatalog(catalog);
            var material = catalog.Materials[0]; var text = material.Paragraphs[0] with { Text = "Changed.", Sha256 = CatalogImporter.Hash("Changed.") };
            var paragraphs = material.Paragraphs.SetItem(0, text); var changed = material with { Paragraphs = paragraphs, BodySha256 = CatalogImporter.Hash(string.Join("\n", paragraphs.Select(p => p.Text))) };
            TestRegistry.Throws<ContentException>(() => ContentCompiler.ValidateCatalog(catalog with { Materials = catalog.Materials.SetItem(0, changed) }, new Dictionary<string, string> { [material.Id + "@" + material.Version] = material.BodySha256 }));
            TestRegistry.Throws<ContentException>(() => ContentCompiler.ValidateCatalog(catalog with { Materials = catalog.Materials.SetItem(0, material with { Id = "SYNTHETIC-ONLY" }) }));
            var q = catalog.Quests[0]; TestRegistry.Throws<ContentException>(() => ContentCompiler.ValidateCatalog(catalog with { Quests = catalog.Quests.SetItem(0, q with { ActivationAfter = [q.Id] }) }));
        });
        TestRegistry.Add("CX-010", "Formal build is blocked by missing audio, editorial/calibration and signature", () =>
        {
            var result = ContentCompiler.Compile(ProjectRoot(), Temp("formal-block"), "mistsea-internal-0.1", true);
            TestRegistry.Require(result.Status == "formal_release_blocked" && result.FormalBlockers.Any(b => b.StartsWith("critical_audio_missing:")) && result.FormalBlockers.Contains("empirical_6.5_calibration_missing"));
            var target = Catalog().Target with { Reading = 7m }; TestRegistry.Throws<InvalidDataException>(() => target.Validate());
        });
        TestRegistry.Add("CX-021", "Unsigned internal package requires explicit allowance; hashes are not signatures", () =>
        {
            var (manifest, bytes) = PackageData(); string zip = Package(manifest, bytes);
            var installer = new PackageInstaller(Temp("install"), new Dictionary<string, RSA>());
            TestRegistry.Throws<ContentException>(() => installer.Install(zip, false));
            var result = installer.Install(zip, true); TestRegistry.Require(result.TrustStatus == "explicit_unsigned_internal_candidate");
            TestRegistry.Require(installer.ResolveExactRelease(manifest.ReleaseId) == result.Directory);
            installer.Quarantine(manifest.ReleaseId, "test isolation"); TestRegistry.Throws<ContentException>(() => installer.ResolveExactRelease(manifest.ReleaseId));
        });
        TestRegistry.Add("CX-021", "Actual RSA test signature is verified and wrong key rejected", () =>
        {
            var (manifest, bytes) = PackageData(); using var key = RSA.Create(2048); using var other = RSA.Create(2048);
            manifest = manifest with { PublisherKeyId = "TEST-ONLY-KEY" };
            manifest = manifest with { Signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(PackageInstaller.CanonicalSignedText(manifest)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
            string zip = Package(manifest, bytes);
            TestRegistry.Throws<ContentException>(() => new PackageInstaller(Temp("bad-key"), new Dictionary<string, RSA> { ["TEST-ONLY-KEY"] = other }).Install(zip, false));
            TestRegistry.Require(new PackageInstaller(Temp("test-key"), new Dictionary<string, RSA> { ["TEST-ONLY-KEY"] = key }).Install(zip, false).TrustStatus == "trusted_publisher_signature");
        });
        TestRegistry.Add("CX-021", "Traversal, executable assets, hash changes and absent audio binding are rejected", () =>
        {
            var (manifest, bytes) = PackageData();
            foreach (string path in new[] { "../outside.json", "C:/outside.json", "evil.exe" })
            {
                var unsafeManifest = manifest with { Assets = [manifest.Assets[0] with { Path = path }] };
                TestRegistry.Throws<ContentException>(() => new PackageInstaller(Temp("unsafe"), new Dictionary<string, RSA>()).Install(Package(unsafeManifest, bytes), true));
            }
            TestRegistry.Throws<ContentException>(() => new PackageInstaller(Temp("tampered"), new Dictionary<string, RSA>()).Install(Package(manifest, bytes.Concat(new byte[] { 1 }).ToArray()), true));
            var catalog = Catalog(); var material = catalog.Materials[0] with { AudioAsset = "audio/old-version.wav" }; catalog = catalog with { Materials = catalog.Materials.SetItem(0, material) };
            bytes = Encoding.UTF8.GetBytes(StrictJson.Serialize(catalog)); manifest = manifest with { Assets = [manifest.Assets[0] with { Sha256 = Hex(bytes), Bytes = bytes.Length }] };
            TestRegistry.Throws<ContentException>(() => new PackageInstaller(Temp("audio-mismatch"), new Dictionary<string, RSA>()).Install(Package(manifest, bytes), true));
        });
        TestRegistry.Add("CX-021", "Present audio cannot be bound to the wrong text version", () =>
        {
            var catalog = Catalog(); var material = catalog.Materials[0] with { AudioAsset = "audio/test.wav" }; catalog = catalog with { Materials = catalog.Materials.SetItem(0, material) };
            byte[] bytes = Encoding.UTF8.GetBytes(StrictJson.Serialize(catalog)), testAudio = Encoding.ASCII.GetBytes("SYNTHETIC AUDIO FIXTURE - NOT A PRODUCTION TRACK");
            var asset = new ReleaseAsset("runtime_catalog.json", Hex(bytes), bytes.Length, "runtime_rules");
            var manifest = new ReleaseManifest(1, catalog.ReleaseId, "mist-rules-1", "Godot-4.7.2-net10-schema1", new(), "internal_candidate", [asset, new("audio/test.wav", Hex(testAudio), testAudio.Length, "audio")], [], [], "unsigned-internal", null, [new(material.Id, "old-version", material.BodySha256, "audio/test.wav", Hex(testAudio))]);
            string zip = Package(manifest, bytes, testAudio);
            TestRegistry.Throws<ContentException>(() => new PackageInstaller(Temp("wrong-audio-version"), new Dictionary<string, RSA>()).Install(zip, true));
        });
    }
    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static (ReleaseManifest Manifest, byte[] Catalog) PackageData()
    {
        var catalog = Catalog(); byte[] bytes = Encoding.UTF8.GetBytes(StrictJson.Serialize(catalog));
        return (new(1, catalog.ReleaseId, "mist-rules-1", "Godot-4.7.2-net10-schema1", new(), "internal_candidate", [new("runtime_catalog.json", Hex(bytes), bytes.Length, "runtime_rules")], [], [], "unsigned-internal", null, []), bytes);
    }
    private static string Package(ReleaseManifest manifest, byte[] bytes, byte[]? extraAudio = null)
    {
        string path = Path.Combine(Temp("package"), "content.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var stream = zip.CreateEntry("manifest.json").Open()) { byte[] json = Encoding.UTF8.GetBytes(StrictJson.Serialize(manifest)); stream.Write(json); }
        using (var stream = zip.CreateEntry(manifest.Assets[0].Path).Open()) stream.Write(bytes);
        if (extraAudio is not null) using (var stream = zip.CreateEntry(manifest.Assets[1].Path).Open()) stream.Write(extraAudio);
        return path;
    }
}
