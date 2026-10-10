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
    public Func<Spot,string>? SpotLabel {get;set;}
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
        var (floor,accent)=Geometry.Id.Split('-')[0] switch {
            "R2"=>(new Color("203c36"),new Color("a8c4a0")),
            "R3"=>(new Color("35363b"),new Color("d7a77c")),
            "R4"=>(new Color("233c4b"),new Color("acd6e0")),
            "R5"=>(new Color("323747"),new Color("b8b5ca")),
            "R6"=>(new Color("27364b"),new Color("a8b9df")),
            "RF"=>(new Color("373b41"),new Color("ddc48e")),
            "HUB"=>(new Color("3d3835"),new Color("c6b39c")),
            _=>(new Color("193b43"),new Color("8bbdc5")) };
        Rect2 Rect(Bounds b) { var a=Screen(new(b.Left,b.Top)); var z=Screen(new(b.Right,b.Bottom)); return new(a,z-a); }
        for(float y=58;y<Size.Y;y+=28)DrawLine(new(12,y),new(Math.Max(12,Size.X-12),y),new Color(.22f,.37f,.43f,.12f),1);
        var walk=Rect(Geometry.Walk);DrawRect(walk,floor);DrawRect(walk,accent with { A=.42f },false,2);
        for(int i=10;i<100;i+=10) {
            if(i>Geometry.Walk.Left&&i<Geometry.Walk.Right)DrawLine(Screen(new(i,Geometry.Walk.Top)),Screen(new(i,Geometry.Walk.Bottom)),accent with { A=.10f });
            if(i>Geometry.Walk.Top&&i<Geometry.Walk.Bottom)DrawLine(Screen(new(Geometry.Walk.Left,i)),Screen(new(Geometry.Walk.Right,i)),accent with { A=.07f });
        }
        foreach(var b in Geometry.Blocked) {
            var block=Rect(b);DrawRect(block,new Color("40515a"));DrawRect(block,accent with { A=.65f },false,2);
            DrawLine(block.Position+new Vector2(3,4),new(block.End.X-3,block.Position.Y+4),accent with { A=.32f },2);
            if(block.Size.X>32&&block.Size.Y>25)DrawRect(new Rect2(block.Position+new Vector2(6,9),new(Math.Max(1,block.Size.X-12),5)),new Color("24343c"));
        }
        foreach(var d in Geometry.Danger) if(!FrontSafe) { DrawRect(Rect(d),new Color(0.7f,0.3f,0.15f,0.35f)); DrawRect(Rect(d),new Color("d99a62"),false,2); }
        var font=GetThemeDefaultFont();
        DrawString(font,new(24,30),SceneTitle,HorizontalAlignment.Left,-1,22,new Color("e4ecec"));
        DrawLine(new(24,39),new(Math.Max(24,Size.X-24),39),accent with { A=.45f },1);
        foreach(var port in Geometry.Ports)
        {
            var p=Screen(port.Stand); bool selected=Selected==port.Id;
            if(selected)DrawCircle(p,18,new Color(1f,.84f,.50f,.18f));
            DrawCircle(p,selected?11:8,selected?new Color("f0d896"):accent);
            DrawArc(p,14,Mathf.Pi,Mathf.Tau,12,accent with { A=.6f },2,true);
            DrawString(font,p+new Vector2(10,-7),"门",HorizontalAlignment.Left,-1,16,new Color("c4dbe0"));
        }
        foreach(var spot in Geometry.Spots)
        {
            var p=Screen(spot.Stand); bool selected=Selected==spot.Id;
            if(selected)DrawCircle(p,17,new Color(1f,.84f,.50f,.18f));
            DrawRect(new Rect2(p-new Vector2(7,7),new(14,14)),new Color("13252e"));
            DrawRect(new Rect2(p-new Vector2(7,7),new(14,14)),selected?new Color("f0d896"):accent,false,2);
            DrawLine(p+new Vector2(-3,-2),p+new Vector2(3,-2),accent,1);DrawLine(p+new Vector2(-3,2),p+new Vector2(2,2),accent,1);
            var label=string.Concat((SpotLabel?.Invoke(spot)??DisplayName(spot)).EnumerateRunes().Take(6));
            DrawString(font,p+new Vector2(9,5),label,HorizontalAlignment.Left,-1,15,new Color("d4e0e2"));
        }
        var player=Screen(Player); var coat=new Color("ead5a0");
        DrawCircle(player+new Vector2(0,13),10,new Color(0,0,0,.28f));
        DrawCircle(player-new Vector2(0,7),5,new Color("dcc8b1"));DrawLine(player-new Vector2(6,10),player+new Vector2(6,-10),new Color("67969f"),3);
        DrawLine(player-new Vector2(0,2),player+new Vector2(0,9),coat,7);DrawLine(player+new Vector2(-3,4),player+new Vector2(3,4),new Color("a08157"),2);
        DrawLine(player+new Vector2(0,9),player+new Vector2(-5,15),coat,3); DrawLine(player+new Vector2(0,9),player+new Vector2(5,15),coat,3);
        DrawCircle(player+new Vector2(0,15),13,new Color(0.9f,0.8f,0.5f,0.4f),false,1);
    }
    public static string DisplayName(Spot spot)
    {
        string known=ShortName(spot.Id);if(known!="物件")return known;
        return spot.Purpose.EnumerateRunes().Any(r=>r.Value is >=0x3400 and <=0x9fff)?spot.Purpose:"现场工位";
    }
    public static string ShortName(string id) => id.Split('.').Last() switch {
        "bulletin"=>"公告", "district_map"=>"地图", "quay_contact"=>"哈文", "dispatch_audio"=>"录音", "route_b_rule"=>"B公告", "havel"=>"哈文", "vessel_spec"=>"资料", "beacon"=>"灯", "reserve_battery"=>"电池", "writing_desk"=>"写作台", "service_map"=>"检修图", "observe_unit"=>"机械", "spare_bracket"=>"支架", "shortcut_latch"=>"内闩", "work_order"=>"工单", "main_generator_tag"=>"禁启牌", "local_controller"=>"控制器", "maintenance_note"=>"说明",
        "glimmer_handover"=>"船务", "harbour_registration"=>"登记", "temporary_commission"=>"任职", "arrival_notice"=>"入港单", "warden"=>"塔站值守", "supply_indicator"=>"供给检查", "encounter_entry"=>"现场入口", "safety_console"=>"制止复核",
        "gangway"=>"登离船口", "deckwatch"=>"甲板值守", "harbor_view"=>"当前泊位", "archive"=>"档案", "correspondence"=>"来信", "draft_desk"=>"草稿桌", "chart"=>"海图", "collection"=>"收藏", "workbench"=>"工作台", "roster"=>"名册", "work_orders"=>"工程单",
        "berth"=>"接驳泊位", "arrival"=>"登陆登记", "june"=>"琼", "living_a"=>"A住居", "living_b"=>"B住居", "living_c"=>"C住居", "scope"=>"访问范围", "mira"=>"米拉", "m12_order"=>"M12指令", "m12_arrival"=>"M12接收", "d1_handover"=>"D−1记录", "signature"=>"历史签名", "batch"=>"矿区批次", "delivery"=>"送达回执", "public_board"=>"公示台", "root_route"=>"根系入口", "conveyor"=>"输送机", "core_entry"=>"核心入口", "branch_a"=>"A支路", "branch_b"=>"B支路", "selector"=>"负荷选择", "drain"=>"手动排水", "test"=>"配置测试", "inner_latch"=>"回程内闩", "duty"=>"塔站值守", _=>"物件" };
}
