using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EncounterRetry;
using Mist.Application.Drafts;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.Learning;
using Mist.Infrastructure.Storage;

namespace Mist.Tests.Storage;

public static class StorageCases
{
    private const string Release = "mistsea-internal-0.1";
    public static T Done<T>(ValueTask<T> value) => value.GetAwaiter().GetResult();
    public static void Done(ValueTask value) => value.GetAwaiter().GetResult();
    private static string Root(string name)
    {
        string root = Path.Combine(System.Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "MistSeaTests"), name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    public static CommitPlanCommand Plan(ProfileStore store, string key, long coins = 10, string? entity = null, long revision = -1, CommandId? id = null)
    {
        var s = store.Session;
        return new(new(s.Profile, s.Branch, s.Load, id ?? new(Guid.NewGuid()), s.ReleaseId, entity is null ? [] : [new(entity, revision)]),
            new(key, "test_resolution", entity is null ? [] : [new(entity, "test", revision, "{\"on\":true}")], [], [new("marks", coins)], "{\"resolved\":true}"));
    }
    private static SaveDraft Draft(ProfileStore store, string text, DraftView? head = null) => new(store.Session.Profile, store.Session.Branch, store.Session.Load, "TASK-1", head?.Id, head?.Revision ?? 0, text, "MATERIAL-1@1");
    private static LearningObservation Observation(string attempt = "attempt-1", bool complete = true, bool submitted = true) => new(Guid.NewGuid().ToString(), "FAMILY-1", attempt, InputModality.Listening, submitted, complete, true, false, [], "1", "answer submitted");
    private static SqliteConnection Raw(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); c.Open(); return c;
    }
    private static void Sql(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    private static long Number(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar()); }
    public static void Register()
    {
        TestRegistry.Add("CX-008", "Integer rewards use exact floor basis points", () =>
        {
            TestRegistry.Require(ExactMath.BasisPoints(101, 12500) == 126);
            TestRegistry.Throws<InvalidDataException>(() => new RewardBundle(-1, 0, 0, "entitlement").Validate());
        });
        TestRegistry.Add("CX-011", "Relevant reads ignore dictionary but invalidate battery/load", () =>
        {
            var load = new LoadContext(Guid.NewGuid());
            var revisions = ImmutableDictionary<string, long>.Empty.Add("battery", 2).Add("dictionary", 5);
            var preview = PreviewFactory.Create("depart", load, revisions, ["battery"], "plan", false, []);
            TestRegistry.Require(preview.ReadSet.Matches(load, revisions.SetItem("dictionary", 6)));
            TestRegistry.Require(!preview.ReadSet.Matches(load, revisions.SetItem("battery", 3)) && !preview.ReadSet.Matches(new(Guid.NewGuid()), revisions));
        });
        TestRegistry.Add("CX-011", "Unknown system state and empty evidence never imply readiness", () =>
        {
            var empty = ImmutableDictionary<string, long>.Empty;
            TestRegistry.Require(PreviewFactory.Create("depart", new(Guid.NewGuid()), empty, ["unknown"], "plan", false, []).Status == PreviewStatus.StateUnavailable);
            TestRegistry.Require(PreviewFactory.Create("depart", new(Guid.NewGuid()), empty, [], "", false, []).Status == PreviewStatus.NeedsClarification);
            var choice = PreviewFactory.Create("depart", new(Guid.NewGuid()), empty, [], "", true, []);
            TestRegistry.Require(choice.Status == PreviewStatus.Ready && !choice.IndependentWritingEvidence);
        });
        TestRegistry.Add("CX-012", "Real profile schema enforces foreign keys and unique ownership slots", () =>
        {
            using var store = new ProfileStore(Root("schema"), Release);
            var diagnostics = Done(store.DiagnosticsAsync());
            TestRegistry.Require(diagnostics.JournalMode == "delete" && diagnostics.Synchronous == 2 && diagnostics.ForeignKeys == 1 && diagnostics.BusyTimeout == 5000 && diagnostics.WriterThread == "MistSea single writer");
            using var db = Raw(store.MainPath);
            Sql(db, "PRAGMA foreign_keys=ON");
            TestRegistry.Require(Number(db, "PRAGMA user_version") == 1 && Number(db, "SELECT count(*) FROM pragma_foreign_key_check") == 0);
            TestRegistry.Throws<SqliteException>(() => Sql(db, "INSERT INTO entities VALUES('missing','x','state',0,'{}')"));
            string b = store.Session.Branch.Value.ToString();
            Sql(db, $"INSERT INTO items VALUES('{b}','i1','I-1','installed','ship','battery')");
            TestRegistry.Throws<SqliteException>(() => Sql(db, $"INSERT INTO items VALUES('{b}','i2','I-1','installed','ship','battery')"));
        });
        TestRegistry.Add("CX-008", "Read-only snapshot query does not write database or advance clock", () =>
        {
            using var store = new ProfileStore(Root("query-purity"), Release);
            string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(store.MainPath)));
            long sequence = store.Session.Sequence; Done(store.SnapshotAsync()); Done(store.SnapshotAsync());
            TestRegistry.Require(store.Session.Sequence == sequence && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(store.MainPath))) == before);
        });
        TestRegistry.Add("CX-013", "Mid-write exception rolls back all world changes", () =>
        {
            using var store = new ProfileStore(Root("rollback"), Release, phase => { if (phase == "after_balances") throw new IOException("injected transaction interruption"); });
            TestRegistry.Throws<IOException>(() => Done(store.ExecuteAsync(Plan(store, "rollback", 10, "beacon"))));
            var snapshot = Done(store.SnapshotAsync());
            TestRegistry.Require(snapshot.Entities.Length == 0 && snapshot.Balances.Count == 0 && snapshot.Receipts.Length == 0 && snapshot.Commands.Length == 0 && snapshot.Outbox.Length == 0);
        });
        TestRegistry.Add("CX-013", "Insufficient balance rolls back earlier item transfer", () =>
        {
            using var store = new ProfileStore(Root("half-item"), Release);
            var seed = Plan(store, "seed", 0) with { Plan = new("seed", "grant", [], [new("battery", "I-1", "new", "", "vessel", "ship", null)], [], "{}") };
            Done(store.ExecuteAsync(seed));
            var bad = Plan(store, "install", -1) with { Plan = new("install", "install", [], [new("battery", "I-1", "vessel", "ship", "installed", "beacon", "power")], [new("marks", -1)], "{}") };
            TestRegistry.Require(Done(store.ExecuteAsync(bad)).Status == CommitStatus.Rejected);
            TestRegistry.Require(Done(store.SnapshotAsync()).Items.Single().OwnerKind == "vessel");
        });
        TestRegistry.Add("CX-013", "Busy competing connection does not block caller thread", () =>
        {
            using var store = new ProfileStore(Root("busy"), Release);
            using var other = Raw(store.MainPath); Sql(other, "BEGIN IMMEDIATE");
            var watch = Stopwatch.StartNew(); var pending = store.ExecuteAsync(Plan(store, "busy"));
            TestRegistry.Require(watch.ElapsedMilliseconds < 250, "enqueue blocked caller");
            TestRegistry.Require(Done(pending).Status == CommitStatus.Busy);
            Sql(other, "ROLLBACK");
            TestRegistry.Require(Done(store.ExecuteAsync(Plan(store, "busy"))).Status == CommitStatus.Committed);
        });
        TestRegistry.Add("CX-014", "Request and semantic work deduplicate; outbox display grants nothing", () =>
        {
            using var store = new ProfileStore(Root("dedup"), Release);
            var command = Plan(store, "Q-102:resolution");
            var first = Done(store.ExecuteAsync(command));
            TestRegistry.Require(first.Status == CommitStatus.Committed && Done(store.ExecuteAsync(command)).Status == CommitStatus.AlreadyCommitted);
            TestRegistry.Require(Done(store.ExecuteAsync(Plan(store, "Q-102:resolution"))).Receipt!.Id == first.Receipt!.Id);
            TestRegistry.Require(Done(store.FindReceiptAsync(command.Context.Branch, command.Context.Command))!.Id == first.Receipt.Id);
            var before = Done(store.SnapshotAsync()); Done(store.MarkOutboxDisplayedAsync(before.Outbox[0].EventId));
            var after = Done(store.SnapshotAsync());
            TestRegistry.Require(after.Balances["marks"] == 10 && after.Receipts.Length == 1 && after.Commands.Length == 2 && after.Outbox.Single().Displayed);
            TestRegistry.Require(Done(store.ExecuteAsync(command with { Plan = command.Plan with { ResultJson = "{\"changed\":true}" } })).Status == CommitStatus.Rejected);
        });
        TestRegistry.Add("CX-014", "Two actual stores serialize concurrent work without double reward", () =>
        {
            string root = Root("competing"); using var a = new ProfileStore(root, Release); using var b = new ProfileStore(root, Release);
            var t1 = a.ExecuteAsync(Plan(a, "one-work")).AsTask(); var t2 = b.ExecuteAsync(Plan(b, "one-work")).AsTask();
            Task.WaitAll(t1, t2);
            TestRegistry.Require(new[] { t1.Result.Status, t2.Result.Status }.Count(x => x == CommitStatus.Committed) == 1);
            TestRegistry.Require(Done(a.SnapshotAsync()).Balances["marks"] == 10);
        });
        TestRegistry.Add("CX-015", "Before/after reward checkpoints branch correctly and learning persists", () =>
        {
            using var store = new ProfileStore(Root("branches"), Release);
            Done(store.ObserveAsync(Observation()));
            string before = Done(store.CreateCheckpointAsync("manual", 0));
            var command = Plan(store, "departure"); Done(store.ExecuteAsync(command));
            string after = Done(store.CreateCheckpointAsync("manual", 1));
            Done(store.LoadCheckpointAsync(before));
            TestRegistry.Require(Done(store.ExecuteAsync(command)).Status == CommitStatus.Rejected);
            TestRegistry.Require(Done(store.ExecuteAsync(Plan(store, "departure"))).Status == CommitStatus.Committed);
            TestRegistry.Require(Done(store.SnapshotAsync()).Balances["marks"] == 10 && Done(store.ObservationCountAsync()) == 1);
            Done(store.LoadCheckpointAsync(after));
            TestRegistry.Require(Done(store.ExecuteAsync(Plan(store, "departure"))).Status == CommitStatus.AlreadyCommitted && Done(store.SnapshotAsync()).Balances["marks"] == 10);
        });
        TestRegistry.Add("CX-015", "Continue preserves encounter instance; automatic slots preserve protected point", () =>
        {
            using var store = new ProfileStore(Root("slots"), Release);
            Done(store.ExecuteAsync(Plan(store, "encounter-seed", 0, "encounter:original")));
            string protect = Done(store.CreateCheckpointAsync("protected", protectedId: "before-battle"));
            for (int i = 0; i < 7; i++) Done(store.CreateCheckpointAsync("automatic"));
            for (int i = 0; i < 6; i++) Done(store.CreateCheckpointAsync("manual", i));
            var old = store.Session; Done(store.ContinueAsync());
            var slots = Done(store.SlotsAsync());
            TestRegistry.Require(slots.Count == 12 && slots["protected:before-battle"] == protect && store.Session.Branch == old.Branch && store.Session.Load != old.Load);
            TestRegistry.Require(Done(store.SnapshotAsync()).Entities.Single().Id == "encounter:original");
        });
        TestRegistry.Add("CX-016", "Local retry preserves external state and refuses successful reward rewind", () =>
        {
            var entry = new EncounterEntry("instance", 1, ImmutableDictionary<string, int>.Empty.Add("player", 100), ImmutableDictionary<string, int>.Empty.Add("med", 2), ImmutableDictionary<string, string>.Empty.Add("phase", "start"), ImmutableHashSet<string>.Empty);
            var failed = entry with { PartyHp = entry.PartyHp.SetItem("player", 0), Consumables = entry.Consumables.SetItem("med", 0) };
            var result = RetryBoundary.Restore(entry, failed);
            TestRegistry.Require(result.Restored.Attempt == 2 && result.Restored.PartyHp["player"] == 100 && result.Restored.Consumables["med"] == 2);
            TestRegistry.Require(!result.Changes.Any(c => c.Contains("draft") || c.Contains("world") || c.Contains("transport")));
            TestRegistry.Throws<InvalidOperationException>(() => RetryBoundary.Restore(entry, failed with { SuccessfulRewards = ImmutableHashSet<string>.Empty.Add("reward") }));
        });
        TestRegistry.Add("CX-017", "Unicode drafts CAS globally and old branch heads keep their version", () =>
        {
            using var store = new ProfileStore(Root("draft"), Release);
            var ack = Done(store.SaveAsync(Draft(store, "中文😀e\u0301\r\ntext")));
            var first = Done(store.GetDraftAsync("TASK-1"))!;
            TestRegistry.Require(first.Body == "中文😀e\u0301\ntext" && first.Hash == ack.Utf8Sha256);
            string checkpoint = Done(store.CreateCheckpointAsync("manual", 0));
            Done(store.SaveAsync(Draft(store, "future text", first)));
            TestRegistry.Throws<StoreConflictException>(() => Done(store.SaveAsync(Draft(store, "stale", first))));
            Done(store.LoadCheckpointAsync(checkpoint));
            var restored = Done(store.GetDraftAsync("TASK-1"))!;
            TestRegistry.Require(restored.Body == first.Body && restored.Revision == 1);
            var third = Done(store.SaveAsync(Draft(store, "new branch text", restored)));
            TestRegistry.Require(third.Draft == ack.Draft && third.Revision == 3);
        });
        TestRegistry.Add("CX-016", "Actual retry transaction preserves latest draft, collected material and delivered transport", () =>
        {
            using var store = new ProfileStore(Root("retry-persistence"), Release);
            var entry = new EncounterEntry("battle-1", 1, ImmutableDictionary<string, int>.Empty.Add("hero", 100), ImmutableDictionary<string, int>.Empty.Add("CON-01", 2), ImmutableDictionary<string, string>.Empty, ImmutableHashSet<string>.Empty);
            var failed = entry with { PartyHp = entry.PartyHp.SetItem("hero", 0), Consumables = entry.Consumables.SetItem("CON-01", 1) };
            var seed = Plan(store, "seed-retry") with { Plan = new("seed-retry", "setup", [new("encounter:battle-1", "encounter", -1, StrictJson.Serialize(failed)), new("transport:delivered", "transport", -1, "{\"delivered\":true}")], [new("new-material", "RE-01", "new", "", "player", "hero", null)], [new("inventory:CON-01", 1)], "{}") };
            Done(store.ExecuteAsync(seed)); Done(store.SaveAsync(Draft(store, "latest draft after loss")));
            var world = Done(store.SnapshotAsync()); var plan = RetryPlanFactory.Create(world, entry, failed);
            var command = Plan(store, plan.SemanticKey) with { Plan = plan };
            TestRegistry.Require(Done(store.ExecuteAsync(command)).Status == CommitStatus.Committed);
            var after = Done(store.SnapshotAsync());
            TestRegistry.Require(after.Items.Single().Id == "new-material" && after.Entities.Single(e => e.Id == "transport:delivered").Json.Contains("true"));
            TestRegistry.Require(after.Balances["inventory:CON-01"] == 2 && Done(store.GetDraftAsync("TASK-1"))!.Body == "latest draft after loss");
            Done(store.ContinueAsync()); TestRegistry.Require(Done(store.SnapshotAsync()).Balances["inventory:CON-01"] == 2);
        });
        TestRegistry.Add("CX-017", "Failed draft write never acknowledges saved text", () =>
        {
            using var store = new ProfileStore(Root("draft-fault"), Release, p => { if (p == "draft_before_commit") throw new IOException("disk failure fixture"); });
            TestRegistry.Throws<IOException>(() => Done(store.SaveAsync(Draft(store, "unsaved"))));
            TestRegistry.Require(Done(store.GetDraftAsync("TASK-1")) is null);
        });
        TestRegistry.Add("CX-017", "Three shared editor entrances reject old ACK and device action leaves text", () =>
        {
            using var store = new ProfileStore(Root("draft-entrances"), Release);
            var editor = new DraftEditor(store, Draft(store, "initial")); var panel = editor; var material = editor; var log = editor;
            material.Edit("first");
            var entered = new ManualResetEventSlim(); var release = new ManualResetEventSlim();
            using var blocked = new ProfileStore(Root("ack-order"), Release, phase => { if (phase == "draft_before_commit") { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); } });
            var delayed = new DraftEditor(blocked, Draft(blocked, "old")); var pending = delayed.SaveAsync().AsTask();
            TestRegistry.Require(entered.Wait(TimeSpan.FromSeconds(5))); delayed.Edit("new text"); release.Set(); pending.GetAwaiter().GetResult();
            TestRegistry.Require(!delayed.IsSaved);
            Done(panel.SaveAsync()); TestRegistry.Require(log.IsSaved && Done(store.GetDraftAsync("TASK-1"))!.Id == editor.LastAck!.Draft);
            Done(store.ExecuteAsync(Plan(store, "repair-device", 0, "beacon"))); TestRegistry.Require(Done(store.GetDraftAsync("TASK-1"))!.Body == "first");
            var stale = Draft(store, "archive text", Done(store.GetDraftAsync("TASK-1"))); string checkpoint = Done(store.CreateCheckpointAsync("manual")); Done(store.LoadCheckpointAsync(checkpoint));
            TestRegistry.Throws<StoreConflictException>(() => Done(store.SaveAsync(stale))); TestRegistry.Require(Done(store.GetDraftAsync("TASK-1"))!.Body == "first");
        });
        TestRegistry.Add("CX-018", "Playback/subtitles/repeat/support are distinct from independent evidence", () =>
        {
            TestRegistry.Require(ObservationRules.Project(Observation(submitted: false), false).State == "not_evidence");
            TestRegistry.Require(!ObservationRules.Project(Observation() with { SubtitlesViewed = true }, false).IndependentListening);
            TestRegistry.Require(!ObservationRules.Project(Observation(), true).EligibleUnfamiliar);
            TestRegistry.Require(!ObservationRules.Project(Observation() with { Modality = InputModality.StructuredChoice }, false).IndependentWriting);
            using var store = new ProfileStore(Root("learning"), Release);
            TestRegistry.Require(Done(store.ObserveAsync(Observation(complete: false))).State == "conditions_incomplete" && Done(store.ObservationCountAsync()) == 0);
        });
        TestRegistry.Add("CX-019", "Draft erasure preserves world receipts and prevents old backup resurrection", () =>
        {
            using var store = new ProfileStore(Root("delete-restore"), Release);
            var draft = Done(store.SaveAsync(Draft(store, "private body"))); Done(store.ExecuteAsync(Plan(store, "world-resolution")));
            var backup = Done(store.BackupAsync()); Done(store.EraseAsync("draft", draft.Draft)); Done(store.RestoreBackupAsync(backup.Id));
            var head = Done(store.GetDraftAsync("TASK-1"))!;
            TestRegistry.Require(head.Deleted && head.Body is null && Done(store.SnapshotAsync()).Receipts.Length == 1);
            TestRegistry.Require(Done(store.SaveAsync(Draft(store, "new body", head))).Draft != draft.Draft);
        });
        TestRegistry.Add("CX-019", "Learning tombstone survives cleanup interruption and old restore", () =>
        {
            string root = Root("delete-crash"); BackupInfo backup;
            using (var store = new ProfileStore(root, Release, phase => { if (phase == "erasure_after_control") throw new IOException("interrupted after tombstone"); }))
            {
                Done(store.ObserveAsync(Observation())); backup = Done(store.BackupAsync());
                TestRegistry.Throws<IOException>(() => Done(store.EraseAsync("learning")));
            }
            using var reopen = new ProfileStore(root, Release);
            TestRegistry.Require(Done(reopen.ObservationCountAsync()) == 0);
            Done(reopen.RestoreBackupAsync(backup.Id)); TestRegistry.Require(Done(reopen.ObservationCountAsync()) == 0);
        });
        TestRegistry.Add("CX-019", "Assistance routes current/old draft/archived branch/discarded and requires consent", () =>
        {
            using var store = new ProfileStore(Root("late-feedback"), Release);
            var saved = Done(store.SaveAsync(Draft(store, "plan")));
            TestRegistry.Throws<InvalidOperationException>(() => Done(store.SetExternalConsentAsync(true, false)));
            var privacy = Done(store.SetExternalConsentAsync(true, true));
            AssistanceRequest Request(string id) => new(id, store.Session.Load, saved.Draft, saved.Revision, privacy.Epoch, privacy.ConsentVersion, "writing", [], "plan");
            Done(store.RegisterAssistanceAsync(Request("current"))); TestRegistry.Require(Done(store.RouteAssistanceAsync("current", "{}")) == "current");
            Done(store.RegisterAssistanceAsync(Request("old"))); string checkpoint = Done(store.CreateCheckpointAsync("manual", 0));
            Done(store.SaveAsync(Draft(store, "edited", Done(store.GetDraftAsync("TASK-1"))))); TestRegistry.Require(Done(store.RouteAssistanceAsync("old", "{}")) == "old_draft");
            Done(store.RegisterAssistanceAsync(Request("archive"))); Done(store.LoadCheckpointAsync(checkpoint)); TestRegistry.Require(Done(store.RouteAssistanceAsync("archive", "{}")) == "archived_branch");
            Done(store.SetExternalConsentAsync(false, true)); TestRegistry.Require(Done(store.RouteAssistanceAsync("current", "{}")) == "discarded");
        });
        TestRegistry.Add("CX-020", "Failed migration preserves live database; high schema refuses all writes", () =>
        {
            string root = Root("migration"); string path;
            using (var store = new ProfileStore(root, Release))
            {
                path = store.MainPath; string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
                TestRegistry.Throws<IOException>(() => Done(store.CreateMigrationCandidateAsync((db, tx) => { using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "DELETE FROM entities"; cmd.ExecuteNonQuery(); throw new IOException("migration failed"); })));
                TestRegistry.Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) == before);
            }
            using (var db = Raw(path)) Sql(db, "PRAGMA user_version=999");
            string high = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            TestRegistry.Throws<StoreVersionException>(() => new ProfileStore(root, Release));
            TestRegistry.Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) == high);
        });
        TestRegistry.Add("CX-020", "Missing deletion control halts without touching main file", () =>
        {
            string root = Root("missing-control"), main, control;
            using (var store = new ProfileStore(root, Release)) { main = store.MainPath; control = store.ControlPath; }
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(main)));
            File.Move(control, control + ".preserved");
            TestRegistry.Throws<MissingControlException>(() => new ProfileStore(root, Release));
            TestRegistry.Require(!File.Exists(control) && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(main))) == hash);
        });
        TestRegistry.Add("CX-020", "Corrupt backups are rejected and managed cleanup preserves outside file", () =>
        {
            using var store = new ProfileStore(Root("backup-safety"), Release);
            var backup = Done(store.BackupAsync()); File.AppendAllText(backup.Path, "corruption");
            TestRegistry.Throws<InvalidDataException>(() => Done(store.RestoreBackupAsync(backup.Id)));
            string outside = Path.Combine(Root("outside"), "user.sqlite"); File.WriteAllText(outside, "keep");
            using (var control = Raw(store.ControlPath)) Sql(control, $"UPDATE backup_registry SET relative_path='{outside.Replace("'", "''")}' WHERE backup_id='{backup.Id}'");
            TestRegistry.Throws<InvalidDataException>(() => Done(store.RemoveManagedBackupAsync(backup.Id))); TestRegistry.Require(File.ReadAllText(outside) == "keep");
        });
        TestRegistry.Add("CX-022", "Real child process kill before commit recovers original complete state", () => KillCase("after_balances", false));
        TestRegistry.Add("CX-020", "Actual corrupt main is preserved while validated recovery switches candidate", () =>
        {
            string root = Root("corrupt-main"); BackupInfo backup; string original;
            using (var store = new ProfileStore(root, Release)) { Done(store.ExecuteAsync(Plan(store, "saved-work"))); backup = Done(store.BackupAsync()); original = store.MainPath; }
            File.WriteAllText(original, "deliberately corrupt database fixture");
            TestRegistry.Throws<SqliteException>(() => new ProfileStore(root, Release));
            using var recovered = new ProfileStore(root, Release, recoveryBackupId: backup.Id);
            TestRegistry.Require(recovered.MainPath != original && File.ReadAllText(original) == "deliberately corrupt database fixture" && Done(recovered.SnapshotAsync()).Balances["marks"] == 10);
        });
        TestRegistry.Add("CX-022", "Real child process kill after commit recovers new complete state", () => KillCase("after_commit", true));
    }
    public static int Child(string[] args)
    {
        if (args.Length != 3) return 2;
        using var store = new ProfileStore(args[1], Release, phase =>
        {
            if (phase != args[2]) return;
            Console.WriteLine("FAULT_READY:" + phase); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite);
        });
        Done(store.ExecuteAsync(Plan(store, "killed-work", 10, "beacon")));
        return 0;
    }
    private static void KillCase(string phase, bool committed)
    {
        string root = Root("process-kill-" + phase); using (var initial = new ProfileStore(root, Release)) { }
        var start = new ProcessStartInfo(System.Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("--storage-child"); start.ArgumentList.Add(root); start.ArgumentList.Add(phase);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Child failed to start.");
        var line = child.StandardOutput.ReadLineAsync();
        if (!line.Wait(TimeSpan.FromSeconds(30)) || line.Result != "FAULT_READY:" + phase)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Fault marker absent: " + child.StandardError.ReadToEnd());
        }
        child.Kill(entireProcessTree: true); child.WaitForExit(10000);
        File.WriteAllText(Path.Combine(root, "process_evidence.json"), StrictJson.Serialize(new { phase, child_pid = child.Id, observed_marker = line.Result, child.ExitCode, experiment = "process termination, not power loss" }));
        using var reopened = new ProfileStore(root, Release); var snapshot = Done(reopened.SnapshotAsync());
        TestRegistry.Require(snapshot.Entities.Length == (committed ? 1 : 0) && snapshot.Receipts.Length == (committed ? 1 : 0) && snapshot.Commands.Length == (committed ? 1 : 0) && snapshot.Outbox.Length == (committed ? 1 : 0));
        TestRegistry.Require(committed ? snapshot.Balances["marks"] == 10 : snapshot.Balances.Count == 0);
    }
}
