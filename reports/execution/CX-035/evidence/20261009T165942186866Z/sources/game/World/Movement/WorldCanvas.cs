using Godot;
using Mist.Core.Exploration;

public partial class WorldCanvas : Control
{
    public SceneGeometry? Geometry { get; set; }
    public Point2 Player { get; set; } = new(48,90);
    public string? Selected { get; set; }
    public bool FrontSafe { get; set; }
    public Action<Point2>? Clicked { get; set; }
    public string SceneTitle { get; set; } = "";
    public int Facing { get; set; }
    private Bounds ViewportBounds => new(24,50,Math.Max(25,Size.X-24),Math.Max(51,Size.Y-24));
    public Vector2 Screen(Point2 p) { var s=NavigationGrid.WorldToScreen(p,ViewportBounds); return new((float)s.X,(float)s.Y); }
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Stop; FocusMode=FocusModeEnum.All;
        AccessibilityName="探索场景"; AccessibilityDescription="方向键或WASD移动；点击出口或物件会先走近，按交互按钮才执行。";
        GuiInput+=input=>
        {
            if(input is InputEventMouseButton { Pressed:true,ButtonIndex:MouseButton.Left } mouse)
            {
                GrabFocus(); Clicked?.Invoke(NavigationGrid.ScreenToWorld(new(mouse.Position.X,mouse.Position.Y),ViewportBounds)); AcceptEvent();
            }
        };
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("101e29"));
        if(Geometry is null) return;
        Rect2 Rect(Bounds b) { var a=Screen(new(b.Left,b.Top)); var z=Screen(new(b.Right,b.Bottom)); return new(a,z-a); }
        DrawRect(Rect(Geometry.Walk),new Color("193340"));
        for(int i=10;i<100;i+=10) { DrawLine(Screen(new(i,Geometry.Walk.Top)),Screen(new(i,Geometry.Walk.Bottom)),new Color(0.2f,0.36f,0.42f,0.22f)); }
        foreach(var b in Geometry.Blocked) { DrawRect(Rect(b),new Color("405564")); DrawRect(Rect(b),new Color("81949e"),false,2); }
        foreach(var d in Geometry.Danger) if(!FrontSafe) { DrawRect(Rect(d),new Color(0.7f,0.3f,0.15f,0.35f)); DrawRect(Rect(d),new Color("d99a62"),false,2); }
        var font=GetThemeDefaultFont();
        DrawString(font,new(24,30),SceneTitle,HorizontalAlignment.Left,-1,22,new Color("e4ecec"));
        foreach(var port in Geometry.Ports)
        {
            var p=Screen(port.Stand); bool selected=Selected==port.Id;
            DrawCircle(p,selected?11:8,selected?new Color("f0d896"):new Color("6aa6b0"));
            DrawString(font,p+new Vector2(10,-7),"门",HorizontalAlignment.Left,-1,16,new Color("c4dbe0"));
        }
        foreach(var spot in Geometry.Spots)
        {
            var p=Screen(spot.Stand); bool selected=Selected==spot.Id;
            DrawRect(new Rect2(p-new Vector2(6,6),new(12,12)),selected?new Color("f0d896"):new Color("91babe"));
            DrawString(font,p+new Vector2(9,5),ShortName(spot.Id),HorizontalAlignment.Left,-1,15,new Color("d4e0e2"));
        }
        var player=Screen(Player); var coat=new Color("ead5a0");
        DrawCircle(player-new Vector2(0,7),5,coat); DrawLine(player-new Vector2(0,2),player+new Vector2(0,9),coat,5);
        DrawLine(player+new Vector2(0,9),player+new Vector2(-5,15),coat,3); DrawLine(player+new Vector2(0,9),player+new Vector2(5,15),coat,3);
        DrawCircle(player+new Vector2(0,15),13,new Color(0.9f,0.8f,0.5f,0.4f),false,1);
    }
    public static string ShortName(string id) => id.Split('.').Last() switch {
        "bulletin"=>"公告", "district_map"=>"地图", "quay_contact"=>"哈文", "dispatch_audio"=>"录音", "route_b_rule"=>"B公告", "havel"=>"哈文", "vessel_spec"=>"资料", "beacon"=>"灯", "reserve_battery"=>"电池", "writing_desk"=>"写作台", "service_map"=>"检修图", "observe_unit"=>"机械", "spare_bracket"=>"支架", "shortcut_latch"=>"内闩", "work_order"=>"工单", "main_generator_tag"=>"禁启牌", "local_controller"=>"控制器", "maintenance_note"=>"说明", _=>"物件" };
}
