using Godot;
using Mist.Core.Assets;
using Mist.Core.Campaign;
using Mist.Core.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public partial class CampaignClient
{
    private IReadOnlyDictionary<string, RegionalAudioBinding>? _campaignAudioBindings;
    private readonly Dictionary<string, double> _campaignAudioPositions = new(StringComparer.Ordinal);
    private AudioStreamPlayer? _campaignContentAudio;
    private Label? _campaignAudioState;
    private string? _campaignAudioId;
    private string? _campaignAudioSource;
    private bool _campaignAudioPlaybackStarted;
    private bool _campaignAudioSubtitlesViewed;
    private (ProfileId Profile, BranchId Branch, LoadContext Load)? _campaignAudioContext;

    private IReadOnlyDictionary<string, RegionalAudioBinding> CampaignAudioBindings()
    {
        if (_campaignAudioBindings is not null) return _campaignAudioBindings;
        using var json = Godot.FileAccess.Open("res://content/audio/regional_runtime_bindings.json", Godot.FileAccess.ModeFlags.Read);
        return _campaignAudioBindings = json is null ? new Dictionary<string, RegionalAudioBinding>() :
            StrictJson.Deserialize<RegionalAudioBinding[]>(json.GetAsText()).ToDictionary(b => b.MaterialId, StringComparer.Ordinal);
    }

    private static string CampaignAudioSource(CampaignMaterial material) => material.Id + ":" + material.Version + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.English))).ToLowerInvariant();

    private bool TryCampaignAudio(CampaignMaterial material, out byte[] bytes, out WaveFacts? facts)
    {
        bytes = []; facts = null;
        if (material.Id != "QP02-L-CORRECTION" || material.Modality != "listening_script" ||
            !CampaignAudioBindings().TryGetValue(material.Id, out var binding)) return false;
        if (!Regex.IsMatch(binding.AudioPath, "^audio/regional/[A-Za-z0-9_.-]+\\.wav$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("序章候选音轨路径未登记。");
        string path = "res://content/" + binding.AudioPath;
        if (!Godot.FileAccess.FileExists(path)) return false;
        bytes = Godot.FileAccess.GetFileAsBytes(path);
        facts = CampaignSpeechProjection.Validate(material, binding, bytes);
        return true;
    }

    // Root calls this when the app goes into the background or its output device changes.
    // Closing a dictionary never resumes this player; only a visible playback command does.
    public void PauseCampaignContentAudio()
    {
        if (_campaignContentAudio is not { } player || !GodotObject.IsInstanceValid(player) ||
            (!player.Playing && !player.StreamPaused)) return;
        if (_campaignAudioSource is { } source) _campaignAudioPositions[source] = player.GetPlaybackPosition();
        player.StreamPaused = true;
        if (_campaignAudioState is { } label && GodotObject.IsInstanceValid(label))
            label.Text = "候选音轨已暂停，实际位置保留；请明确继续播放。";
    }

    private void CloseCampaignContentAudio()
    {
        PauseCampaignContentAudio();
        if (_campaignContentAudio is { } player && GodotObject.IsInstanceValid(player))
        {
            player.Stop();
            player.QueueFree();
        }
        _campaignContentAudio = null;
        _campaignAudioState = null;
        _campaignAudioId = null;
        _campaignAudioSource = null;
        _campaignAudioPlaybackStarted = false;
        _campaignAudioSubtitlesViewed = false;
        _campaignAudioContext = null;
    }

    public override void _ExitTree() => CloseCampaignContentAudio();

    private async Task OpenCampaignAudio(string materialId)
    {
        var material = await _service.OpenMaterialAsync(materialId);
        var session = _store.Session;
        string source = CampaignAudioSource(material);
        async Task CheckPermission()
        {
            var now = _store.Session;
            if (now.Profile != session.Profile || now.Branch != session.Branch || now.Load != session.Load)
            {
                PauseCampaignContentAudio();
                throw new InvalidOperationException("存档加载上下文已改变，请重新打开已得音轨。");
            }
            var permitted = await _service.OpenMaterialAsync(materialId);
            if (CampaignAudioSource(permitted) != source)
            {
                PauseCampaignContentAudio();
                throw new InvalidOperationException("当前原文版本或取得许可已改变，请重新打开音轨。");
            }
            // View exposes candidate objects; their actual acquired/version/scope records remain authoritative.
            if (!(await CurrentCampaignAcquiredMaterials()).Any(a => a.Id == materialId && CampaignAudioSource(a) == source))
            {
                PauseCampaignContentAudio();
                throw new InvalidOperationException("这份原文缺少当前有效的取得记录。");
            }
        }

        await CheckPermission();
        CloseCampaignContentAudio();
        AudioStreamPlayer? openedPlayer = null;
        var flow = Panel("听力：" + material.Title, () =>
        {
            if (ReferenceEquals(_campaignContentAudio, openedPlayer)) PauseCampaignContentAudio();
        });
        Text(flow, "来源 " + material.Id + " · 版本 " + material.Version, 16);
        Text(flow, "本地合成候选音轨，尚待真实审听、声音区分、字幕对齐、语言校准与权利复核。播放与阅读不会生成能力分数或世界奖励。", 18);
        var state = Text(flow, "音轨尚不可用，可主动查看完整脚本继续任务。", 20);
        _campaignAudioState = state;
        byte[] bytes = [];
        WaveFacts? facts = null;
        bool available = false;
        try { available = TryCampaignAudio(material, out bytes, out facts); }
        catch (InvalidDataException error) { state.Text = "实际音轨校验失败，完整脚本仍可查看：" + error.Message; }
        catch (IOException error) { state.Text = "实际音轨读取失败，完整脚本仍可查看：" + error.Message; }
        if (available)
        {
            var stream = AudioStreamWav.LoadFromBuffer(bytes, new Godot.Collections.Dictionary());
            if (stream is null)
                state.Text = "实际 WAV 无法解码，完整原文和中文辅助仍可查看。";
            else
            {
                if (AudioServer.GetBusIndex(MistAudioChannels.Content) < 0) await MistAudioChannels.LoadAsync(_store);
                var player = new AudioStreamPlayer { Stream = stream, Bus = MistAudioChannels.Content };
                AddChild(player);
                openedPlayer = player;
                _campaignContentAudio = player;
                _campaignAudioId = material.Id;
                _campaignAudioSource = source;
                _campaignAudioContext = (session.Profile, session.Branch, session.Load);
                state.Text = "实际候选音轨已就绪；尚未播放。";
                double duration = facts!.DurationMicroseconds / 1_000_000d;
                var progress = Text(flow, $"实际 WAV 时长 {duration:F1} 秒 · 保留位置 {_campaignAudioPositions.GetValueOrDefault(source):F1} 秒", 18);
                Action(flow, "播放 / 暂停候选音轨", () => Run(async () =>
                {
                    await CheckPermission();
                    if (!ReferenceEquals(_campaignContentAudio, player) || !GodotObject.IsInstanceValid(player))
                        throw new InvalidOperationException("音轨页面已过期，请重新打开。");
                    if (player.Playing && !player.StreamPaused) PauseCampaignContentAudio();
                    else
                    {
                        if (player.StreamPaused) player.StreamPaused = false;
                        else player.Play((float)Math.Clamp(_campaignAudioPositions.GetValueOrDefault(source), 0, Math.Max(0, duration - .01)));
                        _campaignAudioPlaybackStarted |= player.Playing;
                        state.Text = player.Playing ? "正在播放实际本地候选音轨；理解尚未评估。" : "音频输出未开始，尚未记录播放。";
                    }
                }));
                Action(flow, "暂停并保留当前播放位置", PauseCampaignContentAudio);
                decimal[] values = [.75m, .9m, 1m, 1.1m, 1.25m];
                var speed = new OptionButton { AccessibilityName = "序章候选音轨播放速度", FitToLongestItem = false };
                foreach (var value in values) speed.AddItem(value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×");
                speed.Select(2);
                speed.AddThemeFontSizeOverride("font_size", 20 * _scale / 100);
                speed.ItemSelected += index => Run(async () =>
                {
                    await CheckPermission();
                    if (!ReferenceEquals(_campaignContentAudio, player)) throw new InvalidOperationException("音轨页面已过期。");
                    player.PitchScale = (float)values[checked((int)index)];
                });
                flow.AddChild(speed);
                player.Finished += () =>
                {
                    _campaignAudioPositions[source] = 0;
                    if (GodotObject.IsInstanceValid(state)) state.Text = "实际音轨播放结束；播放完成不证明理解。";
                };
                var timer = new Godot.Timer { WaitTime = .25, Autostart = true };
                flow.AddChild(timer);
                timer.Timeout += () =>
                {
                    if (!GodotObject.IsInstanceValid(player) || !GodotObject.IsInstanceValid(progress)) return;
                    if (player.Playing || player.StreamPaused)
                    {
                        _campaignAudioPositions[source] = player.GetPlaybackPosition();
                        progress.Text = $"实际位置 {_campaignAudioPositions[source]:F1} / {duration:F1} 秒 · " + (player.StreamPaused ? "已暂停" : "播放中");
                    }
                };
            }
        }
        Action(flow, "查看完整原文脚本 / 字幕", () => Run(async () =>
        {
            await CheckPermission();
            PauseCampaignContentAudio();
            var current = await _service.OpenMaterialAsync(materialId);
            _campaignAudioSubtitlesViewed = true;
            var script = Panel("脚本：" + current.Title);
            Text(script, "这是完整书面原文；查看脚本不等于已播放音轨，没有实际对齐的逐句时间码。", 18);
            var reader = new TextEdit
            {
                Text = current.English, Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary,
                CustomMinimumSize = new(0, 350), AccessibilityName = current.Title + "完整原文脚本"
            };
            reader.AddThemeFontSizeOverride("font_size", 22 * _scale / 100);
            script.AddChild(reader);
            AddCampaignDictionary(script, reader);
            Action(script, "显示这份已得材料的中文释义", () => Text(script, current.Chinese, 20));
            reader.GrabFocus();
        }));
        AddCampaignLearningEntries(flow);
    }
}
