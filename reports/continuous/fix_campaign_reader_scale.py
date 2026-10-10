from pathlib import Path
for name in ['game/UI/Campaign/CampaignClient.cs','game/UI/Campaign/CampaignClient.FirstApproaches.cs']:
 p=Path(name);s=p.read_text(encoding='utf8');s=s.replace('reader.AddThemeFontSizeOverride("font_size",22*_scale/100);','reader.SetMeta("base_font_size",22);reader.AddThemeFontSizeOverride("font_size",22*_scale/100);')
 if name.endswith('CampaignClient.cs'):s=s.replace('if(n is TextEdit e)e.AddThemeFontSizeOverride("font_size",20*size/100);','if(n is TextEdit e)e.AddThemeFontSizeOverride("font_size",(e.HasMeta("base_font_size")?e.GetMeta("base_font_size").AsInt32():20)*size/100);')
 p.write_text(s,encoding='utf8')
