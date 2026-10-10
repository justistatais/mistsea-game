namespace Mist.Application.UI;

public enum InputOwner { Exploration, Workspace, Editable, Dialogue, Puzzle, CombatDecision, CombatResolving, Confirmation, Transition }
public sealed class InputRouting
{
    private readonly Stack<(string View, InputOwner Owner)> _views = new();
    public bool WorldPaused => _views.Count > 0;
    public InputOwner Owner => _views.Count == 0 ? InputOwner.Exploration : _views.Peek().Owner;
    public string? Top => _views.Count == 0 ? null : _views.Peek().View;
    public bool Contains(string view) => _views.Any(v=>v.View==view);
    public void Push(string view, InputOwner owner) { if (owner == InputOwner.Exploration) throw new ArgumentException("Modal cannot own exploration."); _views.Push((view, owner)); }
    public string? Pop() => _views.TryPop(out var view) ? view.View : null;
    public bool MayExecuteWorld(bool editing, bool imeComposing, bool focusedUi, bool backgroundClick) => !WorldPaused && !editing && !imeComposing && !focusedUi && !backgroundClick;
    public static bool MayMutateCombatFromView(string action) => action is "confirm_planned_action" or "cancel_plan";
}
