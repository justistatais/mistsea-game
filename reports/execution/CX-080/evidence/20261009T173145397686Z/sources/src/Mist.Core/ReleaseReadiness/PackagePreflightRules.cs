using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Mist.Core.ReleaseReadiness;

public enum PackageEntryKind { Executable,ResourcePack,DotnetRuntime,SQLiteNative,ContentRelease,Dictionary,Translation,Audio,License,Manifest,AuthorAnswer,PrivateKey,ProviderCredential,ProfileSave,TestFixture,CalibrationHoldout }
public sealed record PackageEntry(string Path,PackageEntryKind Kind,string Sha256,long Bytes);
public sealed record PackagePreflight(bool StructurallyAllowed,ImmutableArray<string> Missing,ImmutableArray<string> Forbidden)
{
    public bool FormalReleaseApproved=>false;
    public bool ActualArtifactAuditStillRequired=>true;
}
public static class PackagePreflightRules
{
    private static bool PortablePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||path.StartsWith('/')||path.Contains('\\')||path.Contains(':')||path.Any(c=>c<' '||c is '<' or '>' or '"' or '|' or '?' or '*'))return false;
        foreach(string component in path.Split('/'))
        {
            if(component is "" or "." or ".."||component.EndsWith('.')||component.EndsWith(' '))return false;
            string stem=component.Split('.')[0].ToUpperInvariant();if(stem is "CON" or "PRN" or "AUX" or "NUL"||stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3] is >= '1' and <= '9')return false;
        }return true;
    }
    public static PackagePreflight Check(ImmutableArray<PackageEntry> entries,bool requiresPlayerSdk,bool requiresPlayerPython)
    {
        var missing=ImmutableArray.CreateBuilder<string>();var forbidden=ImmutableArray.CreateBuilder<string>();if(requiresPlayerSdk)forbidden.Add("player_sdk_dependency");if(requiresPlayerPython)forbidden.Add("player_python_dependency");
        if(entries.IsDefault)entries=[];var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in entries)
        {
            bool safe=PortablePath(entry.Path);if(!Enum.IsDefined(entry.Kind))forbidden.Add("unknown_package_entry_kind");
            if(!safe||!paths.Add(entry.Path))forbidden.Add("unsafe_or_duplicate_path");if(entry.Bytes<=0||!Regex.IsMatch(entry.Sha256,"^[0-9a-f]{64}$",RegexOptions.CultureInvariant))missing.Add("actual_asset_integrity");
            if(entry.Kind is PackageEntryKind.AuthorAnswer or PackageEntryKind.PrivateKey or PackageEntryKind.ProviderCredential or PackageEntryKind.ProfileSave or PackageEntryKind.TestFixture or PackageEntryKind.CalibrationHoldout)forbidden.Add(entry.Kind.ToString());
        }
        foreach(var kind in new[]{PackageEntryKind.Executable,PackageEntryKind.ResourcePack,PackageEntryKind.DotnetRuntime,PackageEntryKind.SQLiteNative,PackageEntryKind.ContentRelease,PackageEntryKind.Dictionary,PackageEntryKind.Translation,PackageEntryKind.Audio,PackageEntryKind.License,PackageEntryKind.Manifest})if(!entries.Any(e=>e.Kind==kind))missing.Add(kind.ToString());
        return new(missing.Count==0&&forbidden.Count==0,missing.Distinct(StringComparer.Ordinal).ToImmutableArray(),forbidden.Distinct(StringComparer.Ordinal).ToImmutableArray());
    }
}
