#if DEBUG
using Godot;
using Mist.Core.Contracts;
using Mist.Core.Learning;

public partial class Root
{
    private async Task VerifyRegionalLearningClientCandidate(Action<string,bool> check)
    {
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        IEnumerable<Node> Nodes(Node node){foreach(var child in node.GetChildren()){yield return child;foreach(var descendant in Nodes(child))yield return descendant;}}
        var before=await _store.SnapshotAsync();string world=StrictJson.Serialize(before);
        var scope=await ReadGrantedLearningSources();var learning=RegionalLearning();
        using var installed=Godot.FileAccess.Open("res://content/lexicon/learner_lexicon.json",Godot.FileAccess.ModeFlags.Read);
        if(installed is null)throw new InvalidDataException("Compiled learner lexicon is missing.");
        var lexicon=RegionalLexicon.Parse(installed.GetAsText());
        var sense=lexicon.Entries.First(e=>e.Contexts.Any(c=>RegionalReviewRules.Permitted(c,scope)));
        await OpenChapterFiveWriter("Q-501");await Frame();var editor=_chapterFiveWriter!;string body=editor.Text;
        await OpenRegionalDictionary(sense.Term,ReadGrantedLearningSources);await Frame();
        check("lexicon_native_720_entries_and_distinct_senses",lexicon.Entries.Length==720&&lexicon.Entries.Select(e=>e.Id).Distinct().Count()==720);
        check("lexicon_native_panel_blocks_world_without_mutating_draft",_routing.Top=="离线词典"&&editor.Text==body);
        var shown=Nodes(_panels.Peek()).OfType<Label>().Select(l=>l.Text).ToArray();
        check("lexicon_native_actual_permitted_context_is_shown",sense.Contexts.Where(c=>RegionalReviewRules.Permitted(c,scope)).Any(c=>shown.Contains(c.ParagraphEnglish)));
        var forbidden=lexicon.Entries.Where(e=>string.Equals(e.Term,sense.Term,StringComparison.OrdinalIgnoreCase)||e.Aliases.Any(a=>string.Equals(a,sense.Term,StringComparison.OrdinalIgnoreCase))).SelectMany(e=>e.Contexts).Where(c=>!RegionalReviewRules.Permitted(c,scope)).Select(c=>c.ParagraphEnglish).Distinct().Except(sense.Contexts.Where(c=>RegionalReviewRules.Permitted(c,scope)).Select(c=>c.ParagraphEnglish));
        check("lexicon_native_future_private_contexts_remain_absent",forbidden.All(text=>!shown.Contains(text)));
        check("lexicon_native_lookup_does_not_change_world_or_rewards",StrictJson.Serialize(await _store.SnapshotAsync())==world);
        ClosePanel();await Frame();check("lexicon_native_reader_close_restores_same_editor_focus",GetViewport().GuiGetFocusOwner()==editor&&editor.Text==body);
        ApplyScale(200);await OpenRegionalDictionary(sense.Term,ReadGrantedLearningSources);await Frame();
        check("lexicon_native_dictionary_200_percent_font",Nodes(_panels.Peek()).OfType<Label>().Any(l=>l.GetThemeFontSize("font_size")>=40));
        ClosePanel();ApplyScale(100);await OpenRegionalReview();await Frame();
        check("lexicon_native_due_review_is_explicit_no_first_day_mastery",_routing.Top=="离线到期复习"&&(await learning.DueAsync(scope)).IsEmpty);
        check("lexicon_native_review_menu_is_world_read_only",StrictJson.Serialize(await _store.SnapshotAsync())==world);
        CloseAllPanels();
    }
}
#endif
