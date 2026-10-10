using Godot;
using Mist.Core.Campaign;
using Mist.Core.Contracts;

public partial class CampaignClient
{
    private async Task ReleaseProbe()
    {
        var view=await _service.ViewAsync();
        bool debugDriverAbsent=typeof(Root).Assembly.GetType("Mist.DebugCandidates.Q102RouteCandidate") is null;
        if(!debugDriverAbsent||view.Campaign.Scope!=CampaignRules.Scope||view.Actor.Scene!="R0-S01"||view.Snapshot.Balances.GetValueOrDefault("experience")!=0||view.Materials.Length!=0||view.Snapshot.Receipts.Any(r=>r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)))throw new InvalidOperationException("候选包的新档或开发驱动隔离检查失败。");
        GD.Print("MISTSEA_RELEASE_PROBE "+StrictJson.Serialize(new{release=_catalog.ReleaseId,scope=view.Campaign.Scope,scene=view.Actor.Scene,experience=0,marks=view.Snapshot.Balances.GetValueOrDefault("marks"),receipt_count=view.Snapshot.Receipts.Length,debug_driver_absent=debugDriverAbsent,online_provider="disabled",human_release_approval="pending"}));
        GetTree().Quit();
    }
}
