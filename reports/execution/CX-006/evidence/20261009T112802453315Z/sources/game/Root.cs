using Godot;
using Mist.Application;

public partial class Root : Control
{
    public override void _Ready()
    {
        Theme = new Theme { DefaultFont = new SystemFont { FontNames = new[] { "Microsoft YaHei", "Segoe UI" } }, DefaultFontSize = 20 };
        var label = new Label { Text = "雾海档案：第七座灯塔\n内部开发候选 · 后续章节正在接入", Position = new Vector2(48, 48) };
        AddChild(label);
        GD.Print("MISTSEA_CLIENT_BOOT " + BuildIdentity.Version);
        if (OS.GetCmdlineUserArgs().Contains("--smoke")) GetTree().Quit();
    }
}
