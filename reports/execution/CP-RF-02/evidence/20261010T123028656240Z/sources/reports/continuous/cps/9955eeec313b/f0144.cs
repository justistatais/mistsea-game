using Mist.Core.Combat;

namespace Mist.Core.Finale;

public enum FinaleMechanismKind { RookCommand, CentralMachine }
public enum FinaleCorePhase { ForcedLink = 1, AutomaticRestart = 2, DampingStop = 3 }
public sealed record RookCombinationPart(string Unit, int RawDamage);
public sealed record RookCombination(int Round, string Source, string[] Targets, RookCombinationPart[] Parts, bool Cancelled = false, string[]? CancelledParts = null);

// The encounter owner persists this object IN BattleSnapshot. It is not a projection
// inferred from HP, a second battle clock, or an authority to change world entities.
public sealed class FinaleMechanismState
{
    public string Version { get; set; } = "rf-mechanism-1";
    public FinaleMechanismKind Kind { get; set; }
    public FinaleCorePhase Phase { get; set; } = FinaleCorePhase.ForcedLink;
    public string Subject { get; set; } = "";
    public long Configuration { get; set; }
    public long? VerifiedConfiguration { get; set; }
    public long? StopApproval { get; set; }
    public bool LeftIsolated { get; set; }
    public bool RightIsolated { get; set; }
    public bool BypassLatched { get; set; }
    public bool DampingSet { get; set; }
    public bool ControlledStop { get; set; }
    public bool CommandLeftClosed { get; set; }
    public bool CommandRightClosed { get; set; }
    public bool RelaysScheduled { get; set; }
    public string[] RelayUnits { get; set; } = ["rf-relay-left", "rf-relay-right"];
    public string[] RelayTemplates { get; set; } = ["rf-relay-left", "rf-relay-right"];
    public string[] GuardUnits { get; set; } = ["rf-rook-guard-left", "rf-rook-guard-right"];
    public RookCombination? Combination { get; set; }
    public int LastTransitionRound { get; set; }
    public string LastTransitionActor { get; set; } = "";
}

public sealed record FinaleTransition(FinaleCorePhase From, FinaleCorePhase To, string[] ScheduledRelays);
public sealed record FinaleControlResult(bool ControlledStop, bool ConfigurationChanged, bool VerificationRecorded);

public static class FinaleBattleRules
{
    public const string RookMechanism = "rf-rook-command";
    public const string CoreMechanism = "rf-central-machine";
    public static bool Owns(string? mechanism) => mechanism is RookMechanism or CoreMechanism;
    public static FinaleMechanismState CreateRook() => new() { Kind = FinaleMechanismKind.RookCommand, Subject = "rf-rook" };
    public static FinaleMechanismState CreateCore() => new() { Kind = FinaleMechanismKind.CentralMachine, Subject = "rf-central" };
    private static BattleUnit Subject(BattleSnapshot s, FinaleMechanismState f) => s.Units.Single(u => u.Id == f.Subject && u.Side == CombatSide.Enemy);
    private static bool Verified(FinaleMechanismState f) => f.VerifiedConfiguration == f.Configuration;
    public static bool RelaysStopped(BattleSnapshot s, FinaleMechanismState f) => f.RelaysScheduled && f.RelayUnits.All(id => s.Units.Any(u => u.Id == id && u.Side == CombatSide.Enemy && !u.Alive)) && !s.PendingReinforcements.Any(p => f.RelayTemplates.Contains(p.Template));

    public static void Validate(BattleSnapshot s, FinaleMechanismState? f)
    {
        if (!Owns(s.Mechanism)) { if (f is not null) throw new InvalidDataException("A finale mechanism cannot attach to another encounter."); return; }
        if (f is null || f.Version != "rf-mechanism-1" || f.Configuration < 0 || !Enum.IsDefined(f.Phase) || f.VerifiedConfiguration > f.Configuration || f.StopApproval > f.Configuration || f.RelayUnits.Length != 2 || f.RelayUnits.Distinct().Count() != 2 || f.RelayTemplates.Length != 2 || f.RelayTemplates.Distinct().Count() != 2 || f.GuardUnits.Length != 2 || f.GuardUnits.Distinct().Count() != 2) throw new InvalidDataException("Finale mechanism identity or revision is invalid.");
        var subject = Subject(s, f);
        // The factory owns standard 1200/220 and 1900/240 capacities and applies the
        // catalog's selected HP multiplier once. Phase floors use those actual capacities.
        if (s.Mechanism == RookMechanism && (f.Kind != FinaleMechanismKind.RookCommand || f.Subject != "rf-rook" || !f.GuardUnits.All(id => s.Units.Any(u => u.Id == id && u.Side == CombatSide.Enemy))) || s.Mechanism == CoreMechanism && (f.Kind != FinaleMechanismKind.CentralMachine || f.Subject != "rf-central" || !f.RelayTemplates.All(t => s.ReinforcementTemplates.ContainsKey(t)))) throw new InvalidDataException("Finale fixed encounter subjects or finite component identities changed.");
        if (f.Kind == FinaleMechanismKind.CentralMachine && (f.Phase == FinaleCorePhase.ForcedLink && f.RelaysScheduled || f.Phase != FinaleCorePhase.ForcedLink && !f.RelaysScheduled || f.ControlledStop && (f.Phase != FinaleCorePhase.DampingStop || !StopReady(f) || subject.Hp != 0))) throw new InvalidDataException("Finale phase proof is inconsistent.");
        if (f.Combination is { } c && (f.Kind != FinaleMechanismKind.RookCommand || c.Source != f.Subject || c.Round < 1 || c.Parts.Length != 3 || c.Parts.Select(p => p.Unit).Distinct().Count() != 3 || c.Parts.Any(p => p.RawDamage < 0 || !s.Units.Any(u => u.Id == p.Unit && u.Side == CombatSide.Enemy)) || c.Targets.Any(id => !s.Units.Any(u => u.Id == id && u.Side == CombatSide.Ally)))) throw new InvalidDataException("Rook combined intention needs actual, named participants and targets.");
    }

    public static bool DamageLocked(BattleSnapshot s, FinaleMechanismState f, BattleUnit target) => target.Id == f.Subject && f.Kind == FinaleMechanismKind.CentralMachine && f.Phase == FinaleCorePhase.ForcedLink && (!f.LeftIsolated || !f.RightIsolated || !Verified(f));
    public static int Floor(BattleSnapshot s, FinaleMechanismState f)
    {
        if (f.Kind == FinaleMechanismKind.RookCommand) return f.CommandLeftClosed && f.CommandRightClosed ? 0 : 1;
        int maximum = Subject(s, f).MaxHp;
        return f.Phase switch { FinaleCorePhase.ForcedLink => (int)Math.Ceiling(maximum * .70), FinaleCorePhase.AutomaticRestart => (int)Math.Ceiling(maximum * .35), FinaleCorePhase.DampingStop => StopReady(f) ? 0 : 1, _ => throw new InvalidDataException() };
    }
    // Call before subtracting HP, after shield absorption. Preview uses this SAME function.
    public static int ClampHpLoss(BattleSnapshot s, FinaleMechanismState f, BattleUnit target, int proposedHpLoss)
    {
        if (proposedHpLoss < 0) throw new ArgumentOutOfRangeException(nameof(proposedHpLoss));
        if (target.Id != f.Subject) return Math.Min(target.Hp, proposedHpLoss);
        if (DamageLocked(s, f, target)) return 0;
        return Math.Min(proposedHpLoss, Math.Max(0, target.Hp - Floor(s, f)));
    }
    private static bool StopReady(FinaleMechanismState f) => f.Phase == FinaleCorePhase.DampingStop && f.DampingSet && Verified(f) && f.StopApproval == f.Configuration;
    public static bool CanEndControlled(BattleSnapshot s, FinaleMechanismState f) => f.Kind == FinaleMechanismKind.RookCommand
        ? f.CommandLeftClosed && f.CommandRightClosed && !Subject(s, f).Alive && f.GuardUnits.All(id => s.Units.Any(u => u.Id == id && !u.Alive))
        : f.ControlledStop && StopReady(f) && !Subject(s, f).Alive && RelaysStopped(s, f);

    // Each command costs 1 AP in the existing engine, and one control per personal turn.
    // The application encounter owner additionally checks actual acquired device sources.
    public static bool CanOperate(BattleSnapshot s, FinaleMechanismState f, BattleUnit actor, string? device)
    {
        if (!actor.Alive || actor.Side != CombatSide.Ally || actor.Ap < 1 || actor.TurnUsed.Contains("control") || s.Outcome != CombatOutcome.Active) return false;
        if (f.Kind == FinaleMechanismKind.RookCommand) return device switch { "command_left" => actor.Slot == 0 && !f.CommandLeftClosed, "command_right" => actor.Slot == 1 && !f.CommandRightClosed, _ => false };
        return device switch
        {
            "isolate_left" => f.Phase == FinaleCorePhase.ForcedLink && actor.Slot == 0 && !f.LeftIsolated,
            "isolate_right" => f.Phase == FinaleCorePhase.ForcedLink && actor.Slot == 1 && !f.RightIsolated,
            "verify_link" => f.Phase == FinaleCorePhase.ForcedLink && actor.Slot == 2 && f.LeftIsolated && f.RightIsolated && !Verified(f),
            "latch_bypass" => f.Phase == FinaleCorePhase.AutomaticRestart && actor.Slot == 0 && RelaysStopped(s, f) && !f.BypassLatched,
            "verify_bypass" => f.Phase == FinaleCorePhase.AutomaticRestart && actor.Slot == 2 && f.BypassLatched && RelaysStopped(s, f) && !Verified(f),
            "set_damping" => f.Phase == FinaleCorePhase.DampingStop && actor.Slot == 1 && !f.DampingSet,
            "verify_damping" => f.Phase == FinaleCorePhase.DampingStop && actor.Slot == 2 && f.DampingSet && !Verified(f),
            "arm_controlled_stop" => f.Phase == FinaleCorePhase.DampingStop && actor.Slot == 0 && f.DampingSet && Verified(f) && f.StopApproval != f.Configuration,
            "reconnect_left" => f.Phase == FinaleCorePhase.ForcedLink && actor.Slot == 0 && f.LeftIsolated,
            "release_bypass" => f.Phase == FinaleCorePhase.AutomaticRestart && actor.Slot == 0 && f.BypassLatched,
            "unset_damping" => f.Phase == FinaleCorePhase.DampingStop && actor.Slot == 1 && f.DampingSet,
            _ => false
        };
    }
    public static FinaleControlResult ApplyControl(BattleSnapshot s, FinaleMechanismState f, BattleUnit actor, string device)
    {
        if (!CanOperate(s, f, actor, device)) throw new InvalidOperationException("当前站位、组件、核验或阶段不允许该控制动作。");
        bool changed = false, verified = false;
        switch (device)
        {
            case "command_left": f.CommandLeftClosed = true; break;
            case "command_right": f.CommandRightClosed = true; break;
            case "isolate_left": f.LeftIsolated = true; changed = true; break;
            case "isolate_right": f.RightIsolated = true; changed = true; break;
            case "reconnect_left": f.LeftIsolated = false; changed = true; break;
            case "latch_bypass": f.BypassLatched = true; changed = true; break;
            case "release_bypass": f.BypassLatched = false; changed = true; break;
            case "set_damping": f.DampingSet = true; changed = true; break;
            case "unset_damping": f.DampingSet = false; changed = true; break;
            case "verify_link": case "verify_bypass": case "verify_damping": f.VerifiedConfiguration = f.Configuration; verified = true; break;
            // Approval is not a configuration change; it preserves the just-completed check.
            case "arm_controlled_stop": f.StopApproval = f.Configuration; break;
            default: throw new InvalidOperationException("Unknown finale device.");
        }
        if (changed) { f.Configuration = checked(f.Configuration + 1); f.VerifiedConfiguration = null; f.StopApproval = null; }
        bool stop = f.Kind == FinaleMechanismKind.CentralMachine && StopReady(f) && Subject(s, f).Hp <= 1;
        if (stop) { Subject(s, f).Hp = 0; f.ControlledStop = true; }
        return new(stop, changed, verified);
    }

    // Call ONCE after the complete ally/enemy action (including all trap/multi-target hits),
    // before Terminal. Never call in Hurt/DamageAction, never carry overflow into a phase.
    public static FinaleTransition? AfterAtomicAction(BattleSnapshot s, FinaleMechanismState f, string actor)
    {
        if (f.Kind != FinaleMechanismKind.CentralMachine || s.Outcome != CombatOutcome.Active) return null;
        var subject = Subject(s, f);var from = f.Phase;bool ready = from switch
        {
            FinaleCorePhase.ForcedLink => subject.Hp == Floor(s, f) && f.LeftIsolated && f.RightIsolated && Verified(f),
            FinaleCorePhase.AutomaticRestart => subject.Hp == Floor(s, f) && RelaysStopped(s, f) && f.BypassLatched && Verified(f),
            FinaleCorePhase.DampingStop => subject.Hp == 0 && StopReady(f), _ => false
        };
        if (!ready) return null;
        if (from == FinaleCorePhase.DampingStop) { f.ControlledStop = true; return null; }
        f.Phase = from == FinaleCorePhase.ForcedLink ? FinaleCorePhase.AutomaticRestart : FinaleCorePhase.DampingStop;
        f.Configuration = checked(f.Configuration + 1);f.VerifiedConfiguration = null;f.StopApproval = null;
        f.LastTransitionRound = s.Round;f.LastTransitionActor = actor;
        string[] scheduled = [];
        if (from == FinaleCorePhase.ForcedLink)
        {
            if (f.RelaysScheduled) throw new InvalidDataException("Relay components may be scheduled only once per actual attempt.");
            f.RelaysScheduled = true;scheduled = f.RelayTemplates.ToArray();
            foreach (string template in scheduled) s.PendingReinforcements.Add(new(f.Subject, template, checked(s.Round + 1)));
        }
        // Old intentions are replaced, not secretly executed after changing the phase.
        s.Intents = s.Intents.Select(i => new EnemyIntent(i.Source, "阶段切换：无伤害重校准", 0, i.Lock, [], [], false, false)).ToList();
        s.Events.Add(new(s.Round, "finale_phase_transition", actor, f.Subject, (int)f.Phase, "同一尝试；溢出舍弃；资源不回满；组件最早下一轮。"));
        return new(from, f.Phase, scheduled);
    }

    // BeginRound freezes the named three participants and existing targets before allies act.
    // The numeric combined contributions are original unreviewed local encounter candidates.
    public static void DeclareRookCombination(BattleSnapshot s, FinaleMechanismState f)
    {
        if (f.Kind != FinaleMechanismKind.RookCommand) return;
        var intent = s.Intents.SingleOrDefault(i => i.Source == f.Subject);
        if (intent is null || intent.Name != "鲁克：公开参与者合击") { f.Combination = null; return; }
        var ids = s.Units.Where(u => u.Side == CombatSide.Ally && u.Alive && (intent.Lock == TargetLock.Unit ? intent.Units.Contains(u.Id) : intent.Slots.Contains(u.Slot))).Select(u => u.Id).ToArray();
        f.Combination = new(s.Round, f.Subject, ids, [new(f.Subject, 120), new(f.GuardUnits[0], 40), new(f.GuardUnits[1], 40)]);
    }
    public static int RookCombinedRaw(BattleSnapshot s, FinaleMechanismState f, EnemyIntent intent)
    {
        if (f.Kind != FinaleMechanismKind.RookCommand || intent.Name != "鲁克：公开参与者合击") return intent.RawDamage;
        var c = f.Combination ?? throw new InvalidDataException("Combined intention was not declared at round start.");
        if (c.Round != s.Round || c.Source != intent.Source) throw new InvalidDataException("Combined intention cannot use a later declaration.");
        if (c.Cancelled || Subject(s, f).PendingBreak) return 0;
        return c.Parts.Where(p => !(c.CancelledParts ?? []).Contains(p.Unit) && s.Units.Any(u => u.Id == p.Unit && u.Alive && !u.PendingBreak)).Sum(p => p.RawDamage);
    }
    // Latch cancellation when Pressure breaks Rook, even if END later clears PendingBreak.
    public static void CancelDeclaredRookCombination(FinaleMechanismState f, string brokenUnit)
    {
        if (f.Kind != FinaleMechanismKind.RookCommand || f.Combination is not { } c) return;
        if (brokenUnit == f.Subject) f.Combination = c with { Cancelled = true };
        else if (f.GuardUnits.Contains(brokenUnit)) f.Combination = c with { CancelledParts = (c.CancelledParts ?? []).Append(brokenUnit).Distinct().ToArray() };
    }
}
