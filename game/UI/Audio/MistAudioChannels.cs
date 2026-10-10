using Godot;
using Mist.Core.Contracts;
using Mist.Infrastructure.Storage;

public static class MistAudioChannels
{
    public const string Content="ContentVoice";
    public static readonly string[] Names=["Master",Content,"Ambience","Music","UI"];
    private static readonly string[] Labels=["主音量","内容语音","环境声","音乐","界面音"];
    public static async ValueTask LoadAsync(ProfileStore store)
    {
        string? saved=await store.GetSettingAsync("audio_preferences");
        var values=saved is null?new Dictionary<string,int>():StrictJson.Deserialize<Dictionary<string,int>>(saved);
        if(values.Any(p=>!Names.Contains(p.Key)||p.Value is <0 or >100))throw new InvalidDataException("声音偏好超出已登记声道或音量范围。原偏好保留。");
        foreach(string name in Names)
        {
            int index=AudioServer.GetBusIndex(name);
            if(index<0){AudioServer.AddBus(-1);index=AudioServer.BusCount-1;AudioServer.SetBusName(index,name);AudioServer.SetBusSend(index,"Master");}
            Set(name,values.GetValueOrDefault(name,100));
        }
    }
    public static int Volume(string name)=>AudioServer.IsBusMute(AudioServer.GetBusIndex(name))?0:(int)Math.Round(AudioServer.GetBusVolumeLinear(AudioServer.GetBusIndex(name))*100);
    public static void Set(string name,int value)
    {
        if(!Names.Contains(name)||value is <0 or >100)throw new ArgumentOutOfRangeException(nameof(value));
        int bus=AudioServer.GetBusIndex(name);AudioServer.SetBusMute(bus,value==0);AudioServer.SetBusVolumeLinear(bus,value/100f);
    }
    public static ValueTask SaveAsync(ProfileStore store)=>store.SetSettingAsync("audio_preferences",StrictJson.Serialize(Names.ToDictionary(n=>n,Volume)));
    public static void Controls(VBoxContainer flow,ProfileStore store,int scale,Action<string> status)
    {
        var title=new Label{Text="声音",AutowrapMode=TextServer.AutowrapMode.WordSmart};title.SetMeta("base_font_size",22);title.AddThemeFontSizeOverride("font_size",22*scale/100);flow.AddChild(title);
        for(int i=0;i<Names.Length;i++)
        {
            string name=Names[i],label=Labels[i];var row=new HBoxContainer();flow.AddChild(row);
            var caption=new Label{Text=label+" · "+Volume(name)+"%",CustomMinimumSize=new(160,0)};caption.SetMeta("base_font_size",20);caption.AddThemeFontSizeOverride("font_size",20*scale/100);row.AddChild(caption);
            var slider=new HSlider{MinValue=0,MaxValue=100,Step=1,Value=Volume(name),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(160,36),AccessibilityName=label+"音量"};row.AddChild(slider);
            slider.ValueChanged+=value=>{Set(name,(int)value);caption.Text=label+" · "+(int)value+"%";};
        }
        var save=new Button{Text="保存声音偏好"};save.AddThemeFontSizeOverride("font_size",20*scale/100);flow.AddChild(save);
        save.Pressed+=async()=>{save.Disabled=true;try{await SaveAsync(store);status("声音偏好已保存。");}catch(Exception e){status("声音偏好保存失败："+e.Message);}finally{if(GodotObject.IsInstanceValid(save))save.Disabled=false;}};
    }
}
