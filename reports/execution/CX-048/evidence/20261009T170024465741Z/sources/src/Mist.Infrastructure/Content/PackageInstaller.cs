using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mist.Core.Content;
using Mist.Core.Contracts;

namespace Mist.Infrastructure.Content;

public sealed record InstallResult(string ReleaseId, string Directory, string TrustStatus);
public sealed class PackageInstaller
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<string, RSA> _trustedKeys;
    public PackageInstaller(string installRoot, IReadOnlyDictionary<string, RSA> trustedKeys)
    {
        _root = Path.GetFullPath(installRoot); Directory.CreateDirectory(_root); _trustedKeys = trustedKeys;
        if (File.GetAttributes(_root).HasFlag(FileAttributes.ReparsePoint)) throw new ContentException("Install root must not be a junction.");
    }
    public static string CanonicalSignedText(ReleaseManifest manifest) => StrictJson.Serialize(manifest with { Signature = null });
    public InstallResult Install(string zipPath, bool allowUnsignedInternal)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > 4096 || archive.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new ContentException("Excessive or duplicate ZIP entries.");
        var entry = archive.GetEntry("manifest.json") ?? throw new ContentException("Manifest missing.");
        if (entry.Length > 1024 * 1024) throw new ContentException("Manifest too large.");
        using var manifestStream = entry.Open();using var bounded=new MemoryStream();byte[] manifestBuffer=new byte[65536];int manifestRead;
        while((manifestRead=manifestStream.Read(manifestBuffer))>0){if(bounded.Length+manifestRead>1024*1024)throw new ContentException("Expanded manifest exceeds limit.");bounded.Write(manifestBuffer,0,manifestRead);}
        if(bounded.Length!=entry.Length)throw new ContentException("Expanded manifest size mismatch.");
        var manifest = StrictJson.Deserialize<ReleaseManifest>(new UTF8Encoding(false,true).GetString(bounded.ToArray()));
        if (manifest.SchemaVersion != 1 || manifest.BuildKind is not ("formal" or "internal_candidate") || manifest.AudioBindings.IsDefault || manifest.EngineContract != "Godot-4.7.2-net10-schema1" || !Regex.IsMatch(manifest.ReleaseId, "^[a-z0-9][a-z0-9.-]{0,79}$", RegexOptions.CultureInvariant)) throw new ContentException("Release/schema/engine contract invalid.");
        manifest.Target.Validate();
        bool signed = manifest.Signature is not null && _trustedKeys.TryGetValue(manifest.PublisherKeyId, out var key) && key.VerifyData(Encoding.UTF8.GetBytes(CanonicalSignedText(manifest)), Convert.FromBase64String(manifest.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (!signed && !(allowUnsignedInternal && manifest.BuildKind == "internal_candidate" && manifest.Signature is null)) throw new ContentException("Publisher signature is absent, unknown, or invalid. Asset hashes are not signatures.");
        if (manifest.BuildKind == "formal" && (manifest.CalibrationRecords.Length == 0 || manifest.EditorialRecords.Length == 0)) throw new ContentException("Formal review evidence missing.");
        if (manifest.Assets.Length == 0 || manifest.Assets.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Assets.Length) throw new ContentException("Empty or duplicate asset list.");
        long total = 0;
        foreach (var asset in manifest.Assets)
        {
            SafeAsset(asset); total = checked(total + asset.Bytes);
            if (total > 256 * 1024 * 1024) throw new ContentException("Expanded package too large.");
        }
        var expected = manifest.Assets.Select(a => a.Path).Append("manifest.json").ToHashSet(StringComparer.Ordinal);
        if (archive.Entries.Any(e => !expected.Contains(e.FullName)) || archive.Entries.Count != expected.Count) throw new ContentException("Undeclared file in package.");
        string stage = Path.Combine(_root, "stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            foreach (var asset in manifest.Assets)
            {
                var source = archive.GetEntry(asset.Path) ?? throw new ContentException("Asset missing: " + asset.Path);
                if (source.Length != asset.Bytes) throw new ContentException("Declared size mismatch.");
                string output = Path.GetFullPath(Path.Combine(stage, asset.Path));
                if (!output.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ContentException("Asset path escaped staging.");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var input = source.Open(); using var destination = new FileStream(output, FileMode.CreateNew);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536]; long count = 0; int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    count = checked(count + read); if (count > asset.Bytes) throw new ContentException("Expanded size exceeded declaration.");
                    destination.Write(buffer, 0, read); hash.AppendData(buffer, 0, read);
                }
                if (count != asset.Bytes || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != asset.Sha256) throw new ContentException("Asset checksum mismatch.");
                destination.Flush(flushToDisk: true);
            }
            File.WriteAllText(Path.Combine(stage, "manifest.json"), StrictJson.Serialize(manifest));
            var catalogAsset = manifest.Assets.SingleOrDefault(a => a.Path == "runtime_catalog.json") ?? throw new ContentException("Runtime catalog missing.");
            var catalog = StrictJson.Deserialize<RuntimeCatalog>(File.ReadAllText(Path.Combine(stage, catalogAsset.Path)));
            if (catalog.ReleaseId != manifest.ReleaseId || catalog.SchemaVersion != 1) throw new ContentException("Exact release binding mismatch.");
            foreach (var material in catalog.Materials)
            {
                if (material.AudioAsset is "missing" or "not_applicable") continue;
                if (!manifest.Assets.Any(a => a.Path == material.AudioAsset && a.Kind == "audio")) throw new ContentException("Audio dependency missing for " + material.Id + "@" + material.Version);
                var binding = manifest.AudioBindings.SingleOrDefault(a => a.MaterialId == material.Id && a.MaterialVersion == material.Version);
                if (binding is null || binding.TextSha256 != material.BodySha256 || binding.AudioPath != material.AudioAsset || !manifest.Assets.Any(a => a.Path == binding.AudioPath && a.Sha256 == binding.AudioSha256 && a.Kind == "audio")) throw new ContentException("Audio/text version binding mismatch.");
            }
            string final = Path.Combine(_root, manifest.ReleaseId);
            if (Directory.Exists(final)) throw new ContentException("Release ID already installed; never overwrite versioned content.");
            Directory.Move(stage, final);
            string pending = Path.Combine(_root, "current.pending.json");
            File.WriteAllText(pending, StrictJson.Serialize(new { release_id = manifest.ReleaseId }));
            File.Move(pending, Path.Combine(_root, "current.json"), overwrite: true);
            return new(manifest.ReleaseId, final, signed ? "trusted_publisher_signature" : "explicit_unsigned_internal_candidate");
        }
        catch (Exception error)
        {
            if (Directory.Exists(stage)) File.WriteAllText(Path.Combine(stage, "quarantine_reason.txt"), error.ToString());
            throw;
        }
    }
    public void Quarantine(string releaseId, string reason)
    {
        if (!Regex.IsMatch(releaseId, "^[a-z0-9][a-z0-9.-]{0,79}$")) throw new ContentException("Invalid release ID.");
        string path = Path.Combine(_root, releaseId);
        if (!Directory.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new ContentException("Release unavailable.");
        File.WriteAllText(Path.Combine(path, "QUARANTINED.txt"), reason + "\nProfile saves are preserved. Load requires a compatible repaired package or managed recovery candidate.");
    }
    public string ResolveExactRelease(string releaseId)
    {
        if (!Regex.IsMatch(releaseId, "^[a-z0-9][a-z0-9.-]{0,79}$")) throw new ContentException("Invalid release ID.");
        string path = Path.Combine(_root, releaseId);
        if (!Directory.Exists(path) || File.Exists(Path.Combine(path, "QUARANTINED.txt")) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new ContentException("Exact content release missing/quarantined; save cannot silently upgrade.");
        return path;
    }
    private static void SafeAsset(ReleaseAsset asset)
    {
        if (asset.Bytes is < 0 or > 33554432 || !Regex.IsMatch(asset.Sha256, "^[0-9a-f]{64}$")) throw new ContentException("Invalid size/hash.");
        if (asset.Path.Contains('\\') || asset.Path.Contains(':') || Path.IsPathRooted(asset.Path) || asset.Path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' '))) throw new ContentException("Unsafe package path.");
        string extension = Path.GetExtension(asset.Path).ToLowerInvariant();
        bool allowed = asset.Kind switch { "material" or "runtime_rules" or "localization" or "hints" => extension == ".json", "audio" or "dictionary_audio" => extension is ".wav" or ".ogg", "image" => extension is ".png" or ".webp", _ => false };
        if (!allowed) throw new ContentException("Executable or unsupported asset kind/extension.");
    }
}
