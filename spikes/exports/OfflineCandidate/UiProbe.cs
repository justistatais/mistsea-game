using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public partial class UiProbe : Control
{
    public TextEdit Editor { get; private set; } = null!;
    public Button Finish { get; private set; } = null!;
    public ScrollContainer Scroll { get; private set; } = null!;
    private VBoxContainer _flow = null!;
    private Label _status = null!;
    private Button _load = null!;
    public void AddFooter(Control control) => _flow.AddChild(control);
    private readonly HashSet<Key> _releaseBarrier = new();
    private bool _syntheticComposition;
    private int _worldActions;
    private int _scale = 100;
    public const string UnicodeSample = "港口 harbour — café e\u0301 — 船🚢 — 👩‍🔧 — don't — 引用\n第二行 / second line";

    public override void _Ready()
    {
        Theme = new Theme { DefaultFont = new SystemFont { FontNames = new[] { "Microsoft YaHei", "Segoe UI" } }, DefaultFontSize = 20 };
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        AddChild(margin);
        Scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddChild(Scroll);
        _flow = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _flow.AddThemeConstantOverride("separation", 14);
        Scroll.AddChild(_flow);
        AddLabel("雾海档案 · 原生输入试件", 30);
        AddLabel("在正文中输入中英文、WASD 和回车。下方计数只记录正文外的场景按键。此页面用于技术验证。", 20);
        var scales = new HBoxContainer();
        _flow.AddChild(scales);
        foreach (int scale in new[] { 100, 125, 150, 175, 200 })
        {
            var button = new Button { Text = scale + "%", CustomMinimumSize = new Vector2(110, 48), AccessibilityName = "字号 " + scale + "%" };
            button.Pressed += () => ApplyScale(scale);
            scales.AddChild(button);
        }
        var load = new Button { Text = "载入 3000 词压力样本", AccessibilityName = "载入三千词压力样本", CustomMinimumSize = new Vector2(0, 48) };
        _load = load;
        load.Pressed += () => { Editor.Text = LongSample(); UpdateStatus(); };
        _flow.AddChild(load);
        Editor = new TextEdit
        {
            Text = UnicodeSample, WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, 360), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AccessibilityName = "草稿正文", AccessibilityDescription = "多行编辑。回车换行；Tab 到下一个按钮。",
        };
        Editor.AddThemeFontSizeOverride("font_size", 20);
        Editor.TextChanged += UpdateStatus;
        Editor.FocusExited += () =>
        {
            foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D, Key.Enter })
                if (Input.IsPhysicalKeyPressed(key)) _releaseBarrier.Add(key);
        };
        _flow.AddChild(Editor);
        _status = AddLabel("", 18);
        Finish = new Button { Text = "末行按钮 · 确认本次检查", CustomMinimumSize = new Vector2(0, 52), AccessibilityName = "确认本次检查" };
        Finish.Pressed += () => { _status.Text = "按钮可达。场景触发次数：" + _worldActions; };
        _flow.AddChild(Finish);
        AddLabel("真实输入法、撤销和读屏的逐项人工检查另行记录；合成输入只用于回归。", 18);
        Finish.FocusEntered += () => Scroll.EnsureControlVisible(Finish);
        UpdateStatus();
        if (OS.GetCmdlineUserArgs().Contains("--ui-test")) CallDeferred(nameof(RunAutomatedChecks));
    }

    private Label AddLabel(string text, int fontSize)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _flow.AddChild(label);
        return label;
    }

    public static string LongSample() => UnicodeSample + "\n\n" + string.Join("\n", Enumerable.Range(0, 150)
        .Select(row => string.Join(" ", Enumerable.Range(0, 20).Select(i => "word" + (row * 20 + i).ToString("D4"))))) + "\nEND / 文末";

    private bool Composing() => _syntheticComposition || (DisplayServer.HasFeature(DisplayServer.Feature.Ime) && DisplayServer.ImeGetText().Length > 0);
    private bool Editing() => GetViewport().GuiGetFocusOwner() is TextEdit or LineEdit;
    private void UpdateStatus() => _status.Text = $"字号 {_scale}% · 正文 {Editor.Text.Length} 个 UTF-16 单元 · 场景触发次数：{_worldActions}";

    public void ApplyScale(int scale)
    {
        _scale = scale;
        Theme.DefaultFontSize = 20 * scale / 100;
        Editor.AddThemeFontSizeOverride("font_size", 20 * scale / 100);
        Editor.CustomMinimumSize = new Vector2(0, 360 * scale / 100);
        foreach (var label in _flow.GetChildren().OfType<Label>()) label.AddThemeFontSizeOverride("font_size", 20 * scale / 100);
        UpdateStatus();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: false } key) _releaseBarrier.Remove(key.Keycode);
        if (input is InputEventKey { Pressed: true, Keycode: Key.Tab } tab && Editor.HasFocus() && !Composing())
        {
            if (tab.ShiftPressed) _load.GrabFocus(); else Finish.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key || GetViewport().GuiGetFocusOwner() is not null || Composing() || _releaseBarrier.Contains(key.Keycode)) return;
        if (key.Keycode is Key.W or Key.A or Key.S or Key.D or Key.Enter)
        {
            _worldActions++;
            UpdateStatus();
            GetViewport().SetInputAsHandled();
        }
    }

    private static void Send(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private async void RunAutomatedChecks()
    {
        var results = new Dictionary<string, bool>();
        try
        {
            Editor.Text = LongSample();
            results["3000_word_fixture_loaded"] = Editor.Text.Contains("word2999") && Enumerable.Range(0, 3000).All(i => Editor.Text.Contains("word" + i.ToString("D4")));
            results["unicode_roundtrip"] = Editor.Text.StartsWith(UnicodeSample, StringComparison.Ordinal);
            Editor.GrabFocus();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D, Key.Enter }) { Send(key, true); Send(key, false); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            results["editing_does_not_trigger_world"] = _worldActions == 0;
            _syntheticComposition = true;
            Editor.ReleaseFocus();
            Send(Key.Enter, true); Send(Key.Enter, false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            results["synthetic_composition_blocks_scene"] = _worldActions == 0;
            _syntheticComposition = false;
            foreach (int scale in new[] { 100, 200 })
            {
                ApplyScale(scale);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Editor.GrabFocus();
                Send(Key.Tab, true); Send(Key.Tab, false);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                results["last_button_reachable_" + scale] = Scroll.GetGlobalRect().Intersects(Finish.GetGlobalRect()) && Finish.HasFocus();
            }
            results["accessible_names_present"] = Editor.AccessibilityName == "草稿正文" && Finish.AccessibilityName.Length > 0;
            GD.Print("MISTSEA_UI_RESULTS " + JsonSerializer.Serialize(new { scope = "Godot native controls with synthetic event checks", checks = results, real_ime = "not_run", real_screen_reader = "not_run" }));
            GetTree().Quit(results.Values.All(v => v) ? 0 : 1);
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }
}
