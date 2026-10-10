using Godot;
using Mist.Core.Campaign;
using Mist.Core.Contracts;
using Mist.Core.Assets;
using System.Text.Json;

public partial class CampaignClient
{
    private async Task ReleaseProbe()
    {
        var view=await _service.ViewAsync();
        bool debugDriverAbsent=typeof(Root).Assembly.GetType("Mist.DebugCandidates.Q102RouteCandidate") is null;
        var privateInstance=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        debugDriverAbsent&=new[]{"RunFirstTowerClientTests","RunFieldRelayClientTests","RunFinaleClientTests","RunR1LifeClientTests","RunR2LifeClientTests","RunChapterFourLocalClientTests","RunVesselLifeClientTests","RunOptionalEncounterClientTests","RunSettlementSupportClientTests","RunLinaPersonalWorkClientTests","RunR3ShiftWorkClientTests","RunOldCityLocalLifeClientTests","RunScopedPersonalClientTests"}.All(name=>typeof(Root).GetMethod(name,privateInstance) is null)&&typeof(CampaignClient).GetMethod("RunFirstRelayClientTests",privateInstance) is null;
        if(!debugDriverAbsent||view.Campaign.Scope!=CampaignRules.Scope||view.Actor.Scene!="R0-S01"||view.Snapshot.Balances.GetValueOrDefault("experience")!=0||view.Materials.Length!=0||view.Snapshot.Receipts.Any(r=>r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)))throw new InvalidOperationException("候选包的新档或开发驱动隔离检查失败。");
        var package=VerifyBundledContent();
        GD.Print("MISTSEA_RELEASE_PROBE "+StrictJson.Serialize(new{release=_catalog.ReleaseId,scope=view.Campaign.Scope,scene=view.Actor.Scene,experience=0,marks=view.Snapshot.Balances.GetValueOrDefault("marks"),receipt_count=view.Snapshot.Receipts.Length,debug_driver_absent=debugDriverAbsent,content=package,online_provider="disabled",human_release_approval="pending"}));
        GetTree().Quit();
    }
    private static object VerifyBundledContent()
    {
        byte[] manifestBytes=Godot.FileAccess.GetFileAsBytes("res://content/manifest.json");
        using var manifest=JsonDocument.Parse(manifestBytes);StrictJson.CheckDuplicates(manifest.RootElement);
        var paths=new HashSet<string>(StringComparer.Ordinal);int audio=0;
        foreach(var asset in manifest.RootElement.GetProperty("assets").EnumerateArray())
        {
            string path=asset.GetProperty("path").GetString()??throw new InvalidDataException("导出内容路径缺失。");
            if(path.Contains("..",StringComparison.Ordinal)||path.StartsWith('/')||!paths.Add(path))throw new InvalidDataException("导出内容路径重复或越界。");
            var bytes=Godot.FileAccess.GetFileAsBytes("res://content/"+path);
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            if(bytes.LongLength!=asset.GetProperty("bytes").GetInt64()||hash!=asset.GetProperty("sha256").GetString())throw new InvalidDataException("导出包内容与编译清单不符："+path);
            if(path.EndsWith(".wav",StringComparison.Ordinal))
            {
                var facts=WaveInspector.Inspect(bytes);var stream=AudioStreamWav.LoadFromBuffer(bytes,new Godot.Collections.Dictionary());
                if(facts.DurationMicroseconds<=0||stream is null||stream.GetLength()<=0)throw new InvalidDataException("导出包真实 WAV 无法解码："+path);
                stream.Dispose();audio++;
            }
        }
        string[] required=["lexicon/learner_lexicon.json","r1_life/learner_package.json","r2_life/learner_contract.json","first_approaches/learner_package.json","field_relay_contract.json","vessel_life_player.json","lina_personal_player.json","r3_shift_work/learner_package.json","optional_encounter_contracts/learner_package.json","settlement_support_contracts.json","old_city_life_contract.json","scoped_personal_work/learner_package.json","chapter_four/optional_credit.json","chapter_four/optional_inquiry.json","campaign/first_entry.json","chapter_one/chapter_one.json","chapter_two/learner_package.json","chapter_three/learner_package.json","chapter_four/learner_package.json","chapter_five/learner_package.json","chapter_six/learner_package.json","finale/learner_package.json","vessel/vessel_interior.json","voyage/r1_r2_voyage.json","voyage/r2_r3_voyage.json","voyage/r3_r4_voyage.json","voyage/r4_r5_voyage.json","voyage/r5_r6_voyage.json"];
        if(required.Any(p=>!paths.Contains(p))||audio==0)throw new InvalidDataException("导出包缺少完整章节、航路或实际音轨。");
        return new{manifest_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifestBytes)).ToLowerInvariant(),verified_assets=paths.Count,decoded_wave_tracks=audio,chapter_packages=8,regional_voyages=5,coverage="packaged_bytes_and_decode_only"};
    }
}
