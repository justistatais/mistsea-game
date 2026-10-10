using Godot;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class CampaignClient
{
    private async Task ReadUiPreferences()
    {
        string? json=await _store.GetSettingAsync("ui_preferences");
        if(json is null)return;
        using var saved=JsonDocument.Parse(json);
        if(saved.RootElement.TryGetProperty("font_scale",out var value)&&value.TryGetInt32(out int scale)&&scale is 100 or 125 or 150 or 175 or 200)ApplyFontScale(scale);
    }
    private async Task SetUiScale(int scale)
    {
        string? saved=await _store.GetSettingAsync("ui_preferences");
        var preferences=saved is null?new JsonObject():JsonNode.Parse(saved)?.AsObject()??throw new InvalidDataException("界面偏好不是有效对象。");
        preferences["font_scale"]=scale;
        await _store.SetSettingAsync("ui_preferences",preferences.ToJsonString());
        ApplyFontScale(scale);
    }
    private void ScaleInheritedTheme(int scale)
    {
        if(GetParent() is Control parent&&parent.Theme is{} theme)theme.DefaultFontSize=20*scale/100;
    }
}
