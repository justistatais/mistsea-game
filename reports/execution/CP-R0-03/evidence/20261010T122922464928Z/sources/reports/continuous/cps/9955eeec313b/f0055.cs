using Godot;
using Mist.Application.Contracts;
using Mist.Application.Learning;
using Mist.Core.Campaign;
using Mist.Core.Learning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public partial class CampaignClient
{
    private sealed class CampaignLearningClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private RegionalLexicon? _campaignLexicon;
    private RegionalLearningService? _campaignLearning;

    private RegionalLearningService CampaignLearning()
    {
        if (_campaignLearning is not null) return _campaignLearning;
        using var json = Godot.FileAccess.Open("res://content/lexicon/learner_lexicon.json", Godot.FileAccess.ModeFlags.Read);
        if (json is null) throw new InvalidDataException("离线词典尚未安装，当前存档和正文保留。");
        _campaignLexicon = RegionalLexicon.Parse(json.GetAsText());
        return _campaignLearning = new(_campaignLexicon, _store, new CampaignLearningClock());
    }

    private async ValueTask<IReadOnlyCollection<CampaignMaterial>> CurrentCampaignAcquiredMaterials()
    {
        var view = await _service.ViewAsync();
        var granted = new List<CampaignMaterial>();
        foreach (var material in view.Materials)
        {
            var entity = view.Snapshot.Entities.SingleOrDefault(e => e.Id == "material:" + material.Id);
            if (entity is null || entity.Kind != "material_access") continue;
            using var json = JsonDocument.Parse(entity.Json);
            var access = json.RootElement;
            if (!access.TryGetProperty("acquired", out var acquired) || acquired.ValueKind != JsonValueKind.True ||
                !access.TryGetProperty("version", out var version) || version.GetString() != material.Version ||
                !access.TryGetProperty("scope", out var scope) || scope.GetString() != CampaignRules.Scope) continue;

            granted.Add(material);
        }
        return granted;
    }

    private async ValueTask<IReadOnlyCollection<LearningSourceAccess>> CurrentCampaignLearningSources()
    {
        _ = CampaignLearning();
        var granted = new List<LearningSourceAccess>();
        foreach (var material in await CurrentCampaignAcquiredMaterials())
        {
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.English))).ToLowerInvariant();
            var contexts = _campaignLexicon!.Entries.SelectMany(e => e.Contexts)
                .Where(c => c.MaterialId == material.Id && c.Version == material.Version && c.Fingerprint == fingerprint).ToArray();
            // A generic dictionary cannot extend the permission of the acquired campaign copy.
            // Unknown or individual/controlled access classes fail closed even if an ID is reused.
            if (contexts.Length == 0 || contexts.Any(c => c.SourceFile != "content/source/campaign/first_entry.json" ||
                c.Region != "R0_R1" || c.AccessClass is not ("public" or "acquired_material"))) continue;
            bool audioAvailable = false;
            if (material.Modality == "listening_script")
            {
                try { audioAvailable = TryCampaignAudio(material, out _, out _); }
                catch (InvalidDataException) { /* Invalid audio cannot remove permission to read the script. */ }
                catch (IOException) { /* Written practice remains available when a local asset fails. */ }
            }
            var session = _store.Session;
            bool played = audioAvailable && _campaignAudioPlaybackStarted && _campaignAudioId == material.Id &&
                _campaignAudioSource == CampaignAudioSource(material) && _campaignAudioContext is { } context &&
                context.Profile == session.Profile && context.Branch == session.Branch && context.Load == session.Load;
            granted.Add(new(material.Id, material.Version, fingerprint, audioAvailable, played,
                _campaignAudioId == material.Id && _campaignAudioSubtitlesViewed));
        }
        return granted;
    }

    private void AddCampaignLearningEntries(VBoxContainer flow)
    {
        Action(flow, "离线查词", () => Run(OpenCampaignDictionarySearch));
        Action(flow, "主动查看到期复习", () => Run(OpenCampaignReview));
    }

    private void AddCampaignDictionary(VBoxContainer flow, TextEdit reader)
    {
        var query = new LineEdit
        {
            PlaceholderText = "选中原文中的词，或输入词 / 短语",
            AccessibilityName = "序章离线查词输入"
        };
        query.AddThemeFontSizeOverride("font_size", 20 * _scale / 100);
        flow.AddChild(query);
        Action(flow, "查选区或输入的词义", () => Run(async () =>
        {
            if (Composing()) throw new InvalidOperationException("请先完成输入法组合，再查询词义。");
            string selected = reader.GetSelectedText();
            await OpenCampaignDictionary(string.IsNullOrWhiteSpace(selected) ? query.Text : selected);
        }));
        Action(flow, "主动查看到期复习", () => Run(OpenCampaignReview));
    }

    private Task OpenCampaignDictionarySearch()
    {
        PauseCampaignContentAudio();
        var flow = Panel("离线查词");
        var input = new LineEdit { PlaceholderText = "English word or phrase", AccessibilityName = "序章离线词典查询" };
        input.AddThemeFontSizeOverride("font_size", 20 * _scale / 100);
        flow.AddChild(input);
        Action(flow, "查词", () => Run(async () =>
        {
            if (Composing()) throw new InvalidOperationException("请先完成输入法组合，再查询词义。");
            await OpenCampaignDictionary(input.Text);
        }));
        Action(flow, "主动查看到期复习", () => Run(OpenCampaignReview));
        input.GrabFocus();
        return Task.CompletedTask;
    }

    private async Task OpenCampaignDictionary(string query)
    {
        PauseCampaignContentAudio();
        var service = CampaignLearning();
        var views = service.Lookup(query, await CurrentCampaignLearningSources());
        var flow = Panel("离线词典");
        Text(flow, "360 CORE + 360 EXTENDED 词义候选 · 读、听、写目标各6.5。词义、语境和词音尚待人工复核。", 18);
        if (views.IsEmpty)
        {
            Text(flow, "未收录这个词或选区。可缩小选区后再查；原文和草稿保留。", 22);
        }
        foreach (var view in views)
        {
            Text(flow, view.Term + " · " + view.Tier + " · " + view.SenseKey, 22);
            Text(flow, view.EnglishDefinition, 20);
            Text(flow, view.ChineseAid, 20);
            foreach (var context in view.PermittedContexts)
            {
                Text(flow, context.ParagraphEnglish, 20);
                Text(flow, "当前已得原文：" + context.MaterialId + " / " + context.Version + " / " + context.ParagraphId, 16);
            }
            if (view.PermittedContexts.IsEmpty)
                Text(flow, "当前未取得匹配的原文版本；这里只显示词义。其他地区和受限材料不会显示。", 18);
            var snapshot = await service.SnapshotAsync();
            await service.ObserveLookupAsync(snapshot.Identity, view.Id, view.PermittedContexts.FirstOrDefault(),
                await CurrentCampaignLearningSources(), Guid.NewGuid().ToString());
        }
        Action(flow, "主动查看到期复习", () => Run(OpenCampaignReview));
    }

    private async Task OpenCampaignReview()
    {
        PauseCampaignContentAudio();
        var cards = await CampaignLearning().DueAsync(await CurrentCampaignLearningSources(), limit: 3);
        var flow = Panel("离线到期复习");
        Text(flow, "每次最多3项，可关闭或主动延后。复习使用真实日期，不改变剧情时间、经验、物品或结局。", 18);
        Text(flow, "候选间隔为1 / 3 / 7 / 14 / 30天。当前回答与自评尚未评测，不会据此自动提高掌握阶段。", 18);
        if (cards.IsEmpty) Text(flow, "当前没有到期项。查词后会安排后续复习，不会自动弹出练习。", 22);
        foreach (var card in cards)
        {
            Action(flow, card.Term + " · " + (card.UnfamiliarFamily ? "当前已得的新材料族" : "熟悉语境或独立词义练习"),
                () => Run(() => OpenCampaignReviewCard(card.SenseId)));
        }
    }

    private async Task OpenCampaignReviewCard(string senseId)
    {
        var service = CampaignLearning();
        var card = (await service.DueAsync(await CurrentCampaignLearningSources(), limit: 3))
            .SingleOrDefault(c => c.SenseId == senseId);
        if (card is null) throw new InvalidOperationException("该项已复习、延后或不在当前三项中，请重新打开到期列表。");
        var snapshot = await service.SnapshotAsync();
        var flow = Panel("复习：" + card.Term);
        Text(flow, "用英文解释这个词义，或写一个符合语境的句子。回答与自评仅保存在本机学习记录中。", 20);
        if (card.Context is { } context)
        {
            Text(flow, context.ParagraphEnglish, 20);
            Text(flow, context.MaterialId + " / " + context.Version + " / " + context.ParagraphId, 16);
        }
        if (card.Context?.Modality == "listening") Text(flow, "此页显示书面脚本，当前复习按阅读记录；打开脚本或加载音轨不计为已听，也不记录独立听力证据。", 18);
        bool support = false;
        var answer = new TextEdit
        {
            CustomMinimumSize = new(0, 160), WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "Your explanation or contextual sentence", AccessibilityName = "序章本机复习英文回答"
        };
        answer.AddThemeFontSizeOverride("font_size", 20 * _scale / 100);
        flow.AddChild(answer);
        Action(flow, "查看词义辅助", () => { support = true; Text(flow, card.EnglishDefinition + "\n" + card.ChineseAid, 20); });
        var production = new CheckBox { Text = "这次写了语境句子（产出练习）", AccessibilityName = "序章产出练习" };
        production.AddThemeFontSizeOverride("font_size", 20 * _scale / 100);
        flow.AddChild(production);
        foreach (var rating in new[] { ("again", "再练一次"), ("hard", "仍觉得困难"), ("good", "我觉得理解了") })
        {
            string attempt = Guid.NewGuid().ToString();
            Action(flow, rating.Item2, () => Run(async () =>
            {
                if (Composing()) throw new InvalidOperationException("请先完成输入法组合，再保存复习回答。");
                await service.ReviewAsync(snapshot.Identity, senseId, rating.Item1, card.Context,
                    await CurrentCampaignLearningSources(), attempt, answer.Text, support, production.ButtonPressed);
                Close();
                _status.Text = "本机复习回答与自评已保存；尚未评测，不生成能力分数或掌握认证。";
            }));
        }
        foreach (int days in new[] { 1, 3, 7 })
        {
            string attempt = Guid.NewGuid().ToString();
            Action(flow, "延后" + days + "天", () => Run(async () =>
            {
                if (Composing()) throw new InvalidOperationException("请先完成输入法组合，再延后这项复习。");
                await service.DeferAsync(snapshot.Identity, senseId, days, attempt);
                Close();
                _status.Text = "复习已按你的选择延后；这次未提交回答。";
            }));
        }
        answer.GrabFocus();
    }
}
