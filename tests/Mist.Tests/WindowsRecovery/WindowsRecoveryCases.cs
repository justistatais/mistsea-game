using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.Learning;
using Mist.Infrastructure.Storage;
using Mist.Tests.Storage;

namespace Mist.Tests.WindowsRecovery;

/// <summary>Offline engineering fixtures on newly created paths. This is not installed-package,
/// physical power-loss, system-volume-full, or another-machine acceptance.</summary>
public static class WindowsRecoveryCases
{
    private const string Release = "mistsea-internal-0.1";
    private const string TaskId = "WINDOWS-RECOVERY-ENGINEERING-FIXTURE";
    private const string OriginalBody = "Offline engineering fixture draft: 中文 e\u0301 😀\nKeep the complete original version.";
    private static readonly string[] NotRun = ["physical_power_loss", "system_volume_physically_full", "another_machine", "installed_formal_distribution", "native_editor_failure_ux", "input_method_preedit_recovery"];
    private static T Done<T>(ValueTask<T> value) => StorageCases.Done(value);
    private static void Done(ValueTask value) => StorageCases.Done(value);
    private static string Root(string name)
    {
        string root = Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "MistSeaTests"),
            "windows-recovery-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    private static SaveDraft Draft(ProfileStore store, string body, DraftView? previous = null) =>
        new(store.Session.Profile, store.Session.Branch, store.Session.Load, TaskId, previous?.Id, previous?.Revision ?? 0, body, "recovery-fixture@1");
    private static LearningObservation Observation(string attempt) =>
        new(Guid.NewGuid().ToString(), "RECOVERY-ENGINEERING-FIXTURE", attempt, InputModality.StructuredChoice, true, true, false, false, [], "fixture@1", "engineering fixture; no language assessment");
    private sealed record SeedState(DraftView Draft, BranchSnapshot World, PrivacyControl Privacy, string Checkpoint);
    private static SeedState Seed(ProfileStore store)
    {
        Done(store.SaveAsync(Draft(store, OriginalBody)));
        var result = Done(store.ExecuteAsync(StorageCases.Plan(store, "baseline-fixture-work", 37, "baseline-fixture-entity")));
        TestRegistry.Require(result.Status == CommitStatus.Committed);
        var observation = Done(store.ObserveAsync(Observation("baseline-fixture-attempt")));
        TestRegistry.Require(!observation.IndependentListening && !observation.IndependentWriting && !observation.EligibleUnfamiliar);
        string checkpoint = Done(store.CreateCheckpointAsync("protected", protectedId: "windows-recovery-fixture"));
        return new(Done(store.GetDraftAsync(TaskId))!, Done(store.SnapshotAsync()), Done(store.PrivacyAsync()), checkpoint);
    }
    private static void RequirePreserved(ProfileStore store, SeedState seed)
    {
        TestRegistry.Require(Done(store.GetDraftAsync(TaskId)) == seed.Draft, "Original draft/head/hash changed.");
        TestRegistry.Require(StrictJson.Serialize(Done(store.SnapshotAsync())) == StrictJson.Serialize(seed.World), "World transaction partially changed.");
        TestRegistry.Require(Done(store.ObservationCountAsync()) == 1 && Done(store.PrivacyAsync()) == seed.Privacy);
        TestRegistry.Require(Done(store.SlotsAsync())["protected:windows-recovery-fixture"] == seed.Checkpoint);
    }
    private static void Evidence(string root, string name, object details, string result = "executed")
    {
        File.WriteAllText(Path.Combine(root, name + ".evidence.json"), StrictJson.Serialize(new
        {
            task = "CX-076", case_name = name, result,
            source_scope = "offline engineering fixture; actual local SQLite/files/processes when executed",
            operating_system = Environment.OSVersion.VersionString,
            runtime = Environment.Version.ToString(),
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            details, not_run = NotRun, human_signoff = (string?)null, release_gate_accepted = false
        }));
    }
    private static SqliteConnection Raw(string path, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }
    private static void Sql(SqliteConnection db, string sql, SqliteTransaction? transaction = null)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static long Number(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static string Text(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand(); command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? throw new InvalidDataException("Expected text value.");
    }
    private static Exception CaptureFailure(Action operation)
    {
        try { operation(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Fault did not reject the operation.");
    }
    private static bool WriteDenied(Exception error) => error is UnauthorizedAccessException || error is SqliteException { SqliteErrorCode: 8 or 14 };

    public static void Register()
    {
        TestRegistry.Add("CX-076", "Actual isolated Windows file DACL denies writes and is restored in finally", AclDenial);
        TestRegistry.Add("CX-076", "SQLite max_page_count quota rolls back draft and wallet mutations without a partial transaction", CapacityQuota);
        TestRegistry.Add("CX-076", "Actual after_commit lost response is resolved by receipt query and exact command retry without another reward", LostCommitResponse);
        TestRegistry.Add("CX-076", "Actual corrupted registered recovery source SHA refuses pointer replacement", BadRegisteredSource);
        TestRegistry.Add("CX-076", "Read-only upgrade source audit checks actual bytes and refuses an altered source", UpgradeSourceAudit);
        TestRegistry.Add("CX-076", "Failed migration transaction leaves a real migration copy and preserves live original bytes", FailedMigration);
        TestRegistry.Add("CX-076", "Committed invalid migration schema is rejected on a separate copy without promoting original", InvalidMigrationSchema);
        TestRegistry.Add("CX-076", "Actually missing control file stops writes and restoring that same control retains deletion generation", MissingControl);
        TestRegistry.Add("CX-076", "Old-generation backup re-applies learning and draft deletions and discards an actual registered late response", DeletedGenerationRestore);
        TestRegistry.Add("CX-076", "Recovery interruption before pointer promotion preserves live state and leaves candidate for inspection", InterruptedRecovery);
        TestRegistry.Add("CX-076", "Actual corrupt original recovery reports backup sequence and missing newer draft and observation", CorruptOriginalLossRange);
        TestRegistry.Add("CX-076", "Real child termination before commit preserves saved draft learning checkpoint and original reward", () => TerminateProcess("after_balances", false));
        TestRegistry.Add("CX-076", "Real child termination after commit preserves new transaction and retry does not duplicate its reward", () => TerminateProcess("after_commit", true));
        TestRegistry.Add("CX-076", "Mismatched content release and newer schema refuse writes without overwriting original database", ReleaseAndSchemaMismatch);
    }

    private static void AclDenial()
    {
        string root = Root("acl-denial");
        if (!OperatingSystem.IsWindows())
        {
            Evidence(root, "acl-denial", new { reason = "Windows file DACL API unavailable on this OS" }, "not_run");
            throw new PlatformNotSupportedException("Windows DACL matrix case not_run; see isolated evidence.");
        }
        using var store = new ProfileStore(root, Release);
        var seed = Seed(store);
        var file = new FileInfo(store.MainPath);
        FileSecurity original = file.GetAccessControl(AccessControlSections.Access);
        byte[] originalDescriptor = original.GetSecurityDescriptorBinaryForm();
        string originalDacl = original.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("Current Windows SID unavailable.");
        var deny = new FileSystemAccessRule(user, FileSystemRights.WriteData | FileSystemRights.AppendData, AccessControlType.Deny);
        Exception? draftError = null, worldError = null;
        bool aclApplied = false;
        try
        {
            FileSecurity blocked = file.GetAccessControl(AccessControlSections.Access);
            blocked.AddAccessRule(deny);
            file.SetAccessControl(blocked);
            aclApplied = true;
            var actual = file.GetAccessControl(AccessControlSections.Access).GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
            TestRegistry.Require(actual.Any(rule => OperatingSystem.IsWindows() && rule.IdentityReference.Equals(user) && rule.AccessControlType == AccessControlType.Deny &&
                (rule.FileSystemRights & deny.FileSystemRights) == deny.FileSystemRights), "Actual current-user deny rule absent.");
            TestRegistry.Require(CaptureFailure(() => { using var probe = new FileStream(file.FullName, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }) is UnauthorizedAccessException);
            draftError = CaptureFailure(() => Done(store.SaveAsync(Draft(store, "unsaved local fixture buffer", seed.Draft))));
            worldError = CaptureFailure(() => Done(store.ExecuteAsync(StorageCases.Plan(store, "denied-fixture-work", 9, "denied-fixture-entity"))));
            TestRegistry.Require(WriteDenied(draftError) && WriteDenied(worldError), "Expected actual file write-denial error, not another failure.");
            RequirePreserved(store, seed);
        }
        finally
        {
            // Only this fixture file's original access section is restored. No directory/user/global ACL is changed.
            var restored = new FileSecurity();
            restored.SetSecurityDescriptorBinaryForm(originalDescriptor, AccessControlSections.Access);
            file.SetAccessControl(restored);
            Evidence(root, "acl-denial-attempt", new
            {
                acl_applied = aclApplied, deny_rights = "WriteData|AppendData", fixture_file = Path.GetFileName(file.FullName),
                draft_error_type = draftError?.GetType().Name, world_error_type = worldError?.GetType().Name,
                draft_sqlite_error = (draftError as SqliteException)?.SqliteErrorCode,
                world_sqlite_error = (worldError as SqliteException)?.SqliteErrorCode,
                original_dacl_restored = file.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access) == originalDacl
            }, aclApplied ? "executed" : "not_run");
        }
        TestRegistry.Require(file.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access) == originalDacl);
        RequirePreserved(store, seed);
        var saved = Done(store.SaveAsync(Draft(store, "retry after ACL restoration", seed.Draft)));
        var retried = Done(store.ExecuteAsync(StorageCases.Plan(store, "denied-fixture-work", 9, "denied-fixture-entity")));
        TestRegistry.Require(saved.Revision == seed.Draft.Revision + 1 && retried.Status == CommitStatus.Committed);
        TestRegistry.Require(Done(store.SnapshotAsync()).Balances["marks"] == 46);
        Evidence(root, "acl-denial", new { original_draft_preserved_before_retry = true, retry_committed_after_dacl_restore = true, actual_draft_error = draftError?.GetType().Name });
    }

    private static void CapacityQuota()
    {
        string root = Root("capacity-quota");
        using var store = new ProfileStore(root, Release);
        var seed = Seed(store);
        int fullCode = 0;
        long pages, pageSize, appliedLimit;
        using (var db = Raw(store.MainPath))
        {
            Sql(db, "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; CREATE TABLE recovery_capacity_engineering_fixture(id INTEGER PRIMARY KEY, payload BLOB NOT NULL)");
            pages = Number(db, "PRAGMA page_count");
            pageSize = Number(db, "PRAGMA page_size");
            long originalLimit = Number(db, "PRAGMA max_page_count");
            appliedLimit = Number(db, "PRAGMA max_page_count=" + (pages + 1));
            TestRegistry.Require(appliedLimit == pages + 1, "Actual SQLite quota was not applied.");
            try
            {
                // Native SQL engineering fault fixture, not an authored world task or ProfileStore command.
                Sql(db, "BEGIN IMMEDIATE");
                Sql(db, "UPDATE balances SET quantity=quantity+9 WHERE account='marks'");
                Sql(db, "UPDATE draft_revisions SET body='incomplete quota fixture draft',body_hash='not-a-valid-draft-hash' WHERE deleted=0");
                Sql(db, "INSERT INTO recovery_capacity_engineering_fixture(payload) VALUES(zeroblob(8388608))");
                Sql(db, "COMMIT");
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 13)
            {
                fullCode = error.SqliteErrorCode;
                // SQLite may already have rolled back SQLITE_FULL. An explicit rollback covers the retained-transaction case.
                try { Sql(db, "ROLLBACK"); }
                catch (SqliteException rollback) when (rollback.SqliteErrorCode == 1 && rollback.Message.Contains("no transaction is active", StringComparison.OrdinalIgnoreCase)) { }
            }
            finally { Sql(db, "PRAGMA max_page_count=" + originalLimit); }
            TestRegistry.Require(fullCode == 13, "Expected actual SQLITE_FULL from a bounded database page quota.");
            TestRegistry.Require(Number(db, "SELECT count(*) FROM recovery_capacity_engineering_fixture") == 0);
            Sql(db, "DROP TABLE recovery_capacity_engineering_fixture");
        }
        RequirePreserved(store, seed);
        var saved = Done(store.SaveAsync(Draft(store, "complete retry following quota removal", seed.Draft)));
        TestRegistry.Require(saved.Revision == seed.Draft.Revision + 1);
        Evidence(root, "capacity-quota", new { sqlite_error = fullCode, original_pages = pages, page_size = pageSize, quota_pages = appliedLimit,
            attempted_blob_bytes = 8388608, fixture_sql_transaction_rolled_back = true, original_draft_hash = seed.Draft.Hash,
            fault_scope = "SQLite connection max_page_count; not actual system-volume full; fixture table removed", world_command_executed = false });
    }

    private static void LostCommitResponse()
    {
        string root = Root("lost-response");
        SeedState seed;
        using (var initial = new ProfileStore(root, Release)) seed = Seed(initial);
        bool responseLost = false;
        using (var store = new ProfileStore(root, Release, phase =>
        {
            if (phase == "after_commit" && !responseLost) { responseLost = true; throw new IOException("Offline fixture: response lost after durable commit."); }
        }))
        {
            var command = StorageCases.Plan(store, "response-lost-fixture-work", 11, "response-lost-fixture-entity");
            TestRegistry.Throws<IOException>(() => Done(store.ExecuteAsync(command)));
            var receipt = Done(store.FindReceiptAsync(command.Context.Branch, command.Context.Command));
            TestRegistry.Require(responseLost && receipt is not null);
            var retry = Done(store.ExecuteAsync(command));
            TestRegistry.Require(retry.Status == CommitStatus.AlreadyCommitted && retry.Receipt == receipt);
            var after = Done(store.SnapshotAsync());
            TestRegistry.Require(after.Sequence == seed.World.Sequence + 1 && after.Balances["marks"] == 48);
            TestRegistry.Require(after.Receipts.Length == 2 && after.Commands.Length == 2 && after.Outbox.Length == 2);
            TestRegistry.Require(Done(store.GetDraftAsync(TaskId)) == seed.Draft && Done(store.ObservationCountAsync()) == 1);
            Evidence(root, "lost-response", new { observed_after_commit_io_failure = true, command_id = command.Context.Command.Value,
                queried_receipt = receipt!.Id.Value, retry_status = retry.Status.ToString(), reward_delta_once = 11, committed_sequence = after.Sequence });
        }
        using var reopened = new ProfileStore(root, Release);
        TestRegistry.Require(Done(reopened.SnapshotAsync()).Balances["marks"] == 48 && Done(reopened.GetDraftAsync(TaskId)) == seed.Draft);
    }

    private static void BadRegisteredSource()
    {
        string root = Root("bad-registered-source");
        using var store = new ProfileStore(root, Release);
        var seed = Seed(store); var backup = Done(store.BackupAsync());
        string main = store.MainPath, mainHash = HashFile(main);
        File.AppendAllText(backup.Path, "\nOFFLINE-ENGINEERING-CHECKSUM-CORRUPTION");
        string corruptedHash = HashFile(backup.Path);
        TestRegistry.Require(corruptedHash != backup.Sha256);
        TestRegistry.Throws<InvalidDataException>(() => Done(store.RestoreBackupAsync(backup.Id)));
        TestRegistry.Require(store.MainPath == main && HashFile(main) == mainHash);
        TestRegistry.Require(!Directory.Exists(Path.Combine(root, "recovery")), "Checksum failure must precede candidate creation.");
        RequirePreserved(store, seed);
        Evidence(root, "bad-registered-source", new { registered_sha256 = backup.Sha256, actual_corrupted_sha256 = corruptedHash,
            active_original_unchanged = true, source_kind = "registered recovery backup; no upgrade was attempted" });
    }

    private static string ToolPath()
    {
        for (DirectoryInfo? cursor = new(AppContext.BaseDirectory); cursor is not null; cursor = cursor.Parent)
        {
            string path = Path.Combine(cursor.FullName, "tools", "recovery", "AuditRecoverySource.ps1");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Read-only recovery audit tool missing from source tree.");
    }
    private sealed record ToolResult(int ExitCode, JsonElement Json);
    private static ToolResult Audit(string source, string expectedHash)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ToolPath(), "-LiteralPath", source, "-ExpectedSha256", expectedHash }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell audit tool could not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(entireProcessTree: true); process.WaitForExit(10000); throw new TimeoutException("Read-only source audit timed out."); }
        Task.WaitAll(output, error);
        TestRegistry.Require(string.IsNullOrWhiteSpace(error.Result), "Recovery source audit error: " + error.Result);
        using var document = JsonDocument.Parse(output.Result);
        return new(process.ExitCode, document.RootElement.Clone());
    }
    private static void UpgradeSourceAudit()
    {
        string root = Root("upgrade-source-audit");
        if (!OperatingSystem.IsWindows())
        {
            Evidence(root, "upgrade-source-audit", new { reason = "This matrix invokes Windows PowerShell" }, "not_run");
            throw new PlatformNotSupportedException("Windows PowerShell source audit not_run.");
        }
        using var store = new ProfileStore(root, Release); var seed = Seed(store);
        var backup = Done(store.BackupAsync());
        string source = Path.Combine(root, "declared-upgrade-source.sqlite");
        File.Copy(backup.Path, source);
        string expected = HashFile(source), mainHash = HashFile(store.MainPath);
        var valid = Audit(source, expected);
        Evidence(root, "upgrade-source-audit-valid-attempt", new { source_audit_exit = valid.ExitCode, source_audit = valid.Json });
        TestRegistry.Require(valid.ExitCode == 0 && valid.Json.GetProperty("checksum_matches").GetBoolean(),
            "Canonical source audit failed: exit=" + valid.ExitCode + " JSON=" + valid.Json.GetRawText());
        TestRegistry.Require(!valid.Json.GetProperty("promotion_authorized").GetBoolean() && valid.Json.GetProperty("database_validation").GetString() == "not_run");
        File.AppendAllText(source, "\nBAD-UPGRADE-SOURCE-ENGINEERING-FIXTURE");
        var bad = Audit(source, expected);
        TestRegistry.Require(bad.ExitCode == 3 && !bad.Json.GetProperty("checksum_matches").GetBoolean());
        TestRegistry.Require(bad.Json.GetProperty("result").GetString() == "source_checksum_mismatch");
        TestRegistry.Require(HashFile(store.MainPath) == mainHash && !Directory.Exists(Path.Combine(root, "recovery")));
        RequirePreserved(store, seed);
        Evidence(root, "upgrade-source-audit", new { valid_exit = valid.ExitCode, altered_exit = bad.ExitCode, altered_audit = bad.Json,
            tool = "AuditRecoverySource.ps1", scope = "actual read-only declared upgrade input preflight; storage migration launcher not integrated" });
    }

    private static void FailedMigration()
    {
        string root = Root("failed-migration");
        using var store = new ProfileStore(root, Release); var seed = Seed(store);
        string original = store.MainPath, originalHash = HashFile(original);
        TestRegistry.Throws<IOException>(() => Done(store.CreateMigrationCandidateAsync((db, tx) =>
        {
            Sql(db, "UPDATE balances SET quantity=999; UPDATE draft_revisions SET body='bad migration fixture',body_hash='invalid'", tx);
            throw new IOException("Offline migration transaction failure fixture.");
        })));
        string candidate = Directory.GetFiles(Path.Combine(root, "recovery"), "migration-*.sqlite").Single();
        TestRegistry.Require(store.MainPath == original && HashFile(original) == originalHash && Path.GetFullPath(candidate) != original);
        using (var copied = Raw(candidate, readOnly: true))
        {
            TestRegistry.Require(Text(copied, "PRAGMA integrity_check") == "ok");
            TestRegistry.Require(Number(copied, "SELECT quantity FROM balances WHERE account='marks'") == 37);
            TestRegistry.Require(Text(copied, "SELECT body_hash FROM draft_revisions WHERE deleted=0") == seed.Draft.Hash);
        }
        RequirePreserved(store, seed);
        Evidence(root, "failed-migration", new { original_sha256 = originalHash, retained_candidate = Path.GetFileName(candidate), candidate_sha256 = HashFile(candidate), candidate_transaction_rolled_back = true });
    }

    private static void InvalidMigrationSchema()
    {
        string root = Root("invalid-migration-schema");
        using var store = new ProfileStore(root, Release); var seed = Seed(store);
        string original = store.MainPath, originalHash = HashFile(original);
        TestRegistry.Throws<StoreVersionException>(() => Done(store.CreateMigrationCandidateAsync((db, tx) => Sql(db, "PRAGMA user_version=999", tx))));
        string candidate = Directory.GetFiles(Path.Combine(root, "recovery"), "migration-*.sqlite").Single();
        using (var copied = Raw(candidate, readOnly: true)) TestRegistry.Require(Number(copied, "PRAGMA user_version") == 999);
        TestRegistry.Require(store.MainPath == original && HashFile(original) == originalHash);
        RequirePreserved(store, seed);
        Evidence(root, "invalid-migration-schema", new { retained_candidate = Path.GetFileName(candidate), actual_candidate_schema = 999, original_not_promoted_or_overwritten = true });
    }

    private static void MissingControl()
    {
        string root = Root("missing-control"), main, control;
        SeedState seed; PrivacyControl afterDelete;
        using (var initial = new ProfileStore(root, Release))
        {
            seed = Seed(initial); afterDelete = Done(initial.EraseAsync("learning"));
            main = initial.MainPath; control = initial.ControlPath;
        }
        string mainHash = HashFile(main), controlHash = HashFile(control), preserved = control + ".isolated-preserved";
        File.Move(control, preserved);
        try
        {
            TestRegistry.Throws<MissingControlException>(() => { using var refused = new ProfileStore(root, Release); });
            TestRegistry.Require(!File.Exists(control) && HashFile(main) == mainHash && HashFile(preserved) == controlHash);
        }
        finally { File.Move(preserved, control); }
        using var reopened = new ProfileStore(root, Release);
        TestRegistry.Require(Done(reopened.PrivacyAsync()) == afterDelete && afterDelete.Epoch > seed.Privacy.Epoch);
        TestRegistry.Require(Done(reopened.ObservationCountAsync()) == 0 && Done(reopened.GetDraftAsync(TaskId)) == seed.Draft);
        TestRegistry.Require(Done(reopened.SnapshotAsync()).Balances["marks"] == 37);
        Evidence(root, "missing-control", new { missing_control_rejected = true, same_original_control_restored = true,
            restored_learning_epoch = afterDelete.Epoch, restored_consent_version = afterDelete.ConsentVersion, reset_to_epoch_zero = false });
    }

    private static void DeletedGenerationRestore()
    {
        string root = Root("deleted-generation");
        BackupInfo backup; SeedState seed; PrivacyControl firstDeletion;
        using (var initial = new ProfileStore(root, Release))
        {
            seed = Seed(initial);
            var consent = Done(initial.SetExternalConsentAsync(true, true));
            // Registers metadata in local SQLite. No provider, body upload or online request occurs.
            Done(initial.RegisterAssistanceAsync(new("recovery-old-generation-fixture", initial.Session.Load, seed.Draft.Id, seed.Draft.Revision,
                consent.Epoch, consent.ConsentVersion, "writing", [], OriginalBody)));
            backup = Done(initial.BackupAsync());
            firstDeletion = Done(initial.EraseAsync("learning"));
        }
        using (var interrupted = new ProfileStore(root, Release, phase => { if (phase == "erasure_after_control") throw new IOException("Offline interruption after durable draft deletion control."); }))
            TestRegistry.Throws<IOException>(() => Done(interrupted.EraseAsync("draft", seed.Draft.Id)));
        using var store = new ProfileStore(root, Release);
        var deletion = Done(store.PrivacyAsync());
        TestRegistry.Require(deletion.Epoch > firstDeletion.Epoch && Done(store.GetDraftAsync(TaskId)) is { Deleted: true, Body: null });
        string oldPath = store.MainPath;
        Done(store.RestoreBackupAsync(backup.Id));
        TestRegistry.Require(store.MainPath != oldPath && Done(store.PrivacyAsync()) == deletion);
        var erased = Done(store.GetDraftAsync(TaskId));
        TestRegistry.Require(erased is { Deleted: true, Body: null } && erased.Id == seed.Draft.Id);
        TestRegistry.Require(Done(store.ObservationCountAsync()) == 0 && Done(store.RouteAssistanceAsync("recovery-old-generation-fixture", "{\"fixture\":true}")) == "discarded");
        using (var db = Raw(store.MainPath, readOnly: true))
        {
            TestRegistry.Require(Number(db, "SELECT count(*) FROM draft_revisions WHERE body IS NOT NULL AND deleted=0") == 0);
            TestRegistry.Require(Number(db, "SELECT count(*) FROM ai_requests WHERE response_json IS NOT NULL OR status<>'discarded'") == 0);
        }
        var after = Done(store.SnapshotAsync());
        TestRegistry.Require(after.Receipts.Length == 1 && after.Balances["marks"] == 37);
        var rewritten = Done(store.SaveAsync(Draft(store, "new compatible fixture body after deletion", erased)));
        TestRegistry.Require(rewritten.Draft != seed.Draft.Id);
        Evidence(root, "deleted-generation", new { actual_old_backup_sequence = backup.Sequence, current_epoch = deletion.Epoch,
            interrupted_deletion_replayed = true, deleted_body_or_learning_resurrected = false, late_response_route = "discarded",
            new_draft_identity_created = true, world_receipt_preserved = true, backup_physical_secure_erasure = "not_run" });
    }

    private static void InterruptedRecovery()
    {
        string root = Root("interrupted-recovery");
        SeedState seed; BackupInfo backup;
        using (var initial = new ProfileStore(root, Release)) { seed = Seed(initial); backup = Done(initial.BackupAsync()); }
        string active, activeHash, candidate;
        using (var interrupted = new ProfileStore(root, Release, phase => { if (phase == "recovery_before_pointer") throw new IOException("Offline fixture interruption before candidate pointer promotion."); }))
        {
            active = interrupted.MainPath; activeHash = HashFile(active);
            TestRegistry.Throws<IOException>(() => Done(interrupted.RestoreBackupAsync(backup.Id)));
            candidate = Directory.GetFiles(Path.Combine(root, "recovery"), "candidate-*.sqlite").Single();
            TestRegistry.Require(interrupted.MainPath == active && HashFile(active) == activeHash);
            using var control = Raw(interrupted.ControlPath, readOnly: true);
            TestRegistry.Require(Text(control, "SELECT current_path FROM recovery_pointers") == "profile.sqlite");
            TestRegistry.Require(Text(control, "SELECT pending_candidate FROM recovery_pointers").Replace('\\', '/') == "recovery/" + Path.GetFileName(candidate));
        }
        using var reopened = new ProfileStore(root, Release);
        TestRegistry.Require(reopened.MainPath == active); RequirePreserved(reopened, seed);
        Done(reopened.RestoreBackupAsync(backup.Id));
        TestRegistry.Require(reopened.MainPath != active && File.Exists(candidate) && HashFile(active) == activeHash);
        RequirePreserved(reopened, seed);
        Evidence(root, "interrupted-recovery", new { interrupted_candidate_retained = Path.GetFileName(candidate), original_sha256 = activeHash,
            later_explicit_restore_promoted_a_different_candidate = true, experiment = "in-process injected exception, not process kill or power loss" });
    }

    private static void CorruptOriginalLossRange()
    {
        string root = Root("corrupt-original");
        BackupInfo backup; SeedState seed; long newerSequence; string original, newerHash;
        using (var initial = new ProfileStore(root, Release))
        {
            seed = Seed(initial); backup = Done(initial.BackupAsync());
            Done(initial.SaveAsync(Draft(initial, "newer fixture draft committed after the selected backup", seed.Draft)));
            newerHash = Done(initial.GetDraftAsync(TaskId))!.Hash;
            Done(initial.ObserveAsync(Observation("newer-fixture-attempt")));
            Done(initial.ExecuteAsync(StorageCases.Plan(initial, "newer-fixture-work", 5, "newer-fixture-entity")));
            newerSequence = Done(initial.SnapshotAsync()).Sequence; original = initial.MainPath;
        }
        File.WriteAllBytes(original, "CORRUPT-ORIGINAL-ENGINEERING-FIXTURE"u8.ToArray());
        string corruptHash = HashFile(original);
        TestRegistry.Throws<SqliteException>(() => { using var refused = new ProfileStore(root, Release); });
        using var recovered = new ProfileStore(root, Release, recoveryBackupId: backup.Id);
        var after = Done(recovered.SnapshotAsync());
        TestRegistry.Require(recovered.MainPath != original && HashFile(original) == corruptHash);
        TestRegistry.Require(after.Sequence == backup.Sequence && after.Sequence < newerSequence && after.Balances["marks"] == 37);
        TestRegistry.Require(Done(recovered.GetDraftAsync(TaskId)) == seed.Draft && seed.Draft.Hash != newerHash);
        TestRegistry.Require(Done(recovered.ObservationCountAsync()) == 1);
        Evidence(root, "corrupt-original-loss-range", new { preserved_corrupt_original_sha256 = corruptHash,
            selected_backup_sequence = backup.Sequence, last_known_live_sequence = newerSequence, world_commits_missing = newerSequence - backup.Sequence,
            newer_draft_hash_not_in_selected_backup = newerHash, restored_draft_hash = seed.Draft.Hash,
            observations_before_corruption = 2, observations_in_selected_backup = 1,
            loss_explanation = "Full database backup restoration lacks newer world commit, newer draft and newer profile observation; not world-checkpoint rollback and not zero-loss recovery.",
            native_recovery_ui_explanation = "not_run" });
    }

    private static ProcessStartInfo StorageChildStart(string root, string phase)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--storage-child"); start.ArgumentList.Add(root); start.ArgumentList.Add(phase);
        return start;
    }
    private static void TerminateProcess(string phase, bool committed)
    {
        string root = Root("process-" + phase); SeedState seed;
        using (var initial = new ProfileStore(root, Release)) seed = Seed(initial);
        using var child = Process.Start(StorageChildStart(root, phase)) ?? throw new InvalidOperationException("Actual child process failed to start.");
        var marker = child.StandardOutput.ReadLineAsync();
        try
        {
            if (!marker.Wait(TimeSpan.FromSeconds(30)) || marker.Result != "FAULT_READY:" + phase)
                throw new InvalidOperationException("Actual child did not reach the requested storage fault phase.");
            child.Kill(entireProcessTree: true);
            TestRegistry.Require(child.WaitForExit(10000), "Killed child failed to exit.");
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10000); } }
        using var reopened = new ProfileStore(root, Release);
        var after = Done(reopened.SnapshotAsync());
        TestRegistry.Require(after.Sequence == seed.World.Sequence + (committed ? 1 : 0));
        TestRegistry.Require(after.Receipts.Length == (committed ? 2 : 1) && after.Commands.Length == (committed ? 2 : 1) && after.Outbox.Length == (committed ? 2 : 1));
        TestRegistry.Require(after.Balances["marks"] == (committed ? 47 : 37));
        TestRegistry.Require(after.Entities.Any(entity => entity.Id == "beacon") == committed);
        TestRegistry.Require(Done(reopened.GetDraftAsync(TaskId)) == seed.Draft && Done(reopened.ObservationCountAsync()) == 1);
        TestRegistry.Require(Done(reopened.SlotsAsync())["protected:windows-recovery-fixture"] == seed.Checkpoint);
        // The original request ID is not retained by the sleeping child. Retry the same semantic work with a new transport ID.
        var retry = Done(reopened.ExecuteAsync(StorageCases.Plan(reopened, "killed-work", 10, "beacon")));
        TestRegistry.Require(retry.Status == (committed ? CommitStatus.AlreadyCommitted : CommitStatus.Committed));
        var retried = Done(reopened.SnapshotAsync());
        TestRegistry.Require(retried.Balances["marks"] == 47 && retried.Receipts.Length == 2 && retried.Outbox.Length == 2 && retried.Sequence == seed.World.Sequence + 1);
        Evidence(root, "process-" + phase, new { observed_marker = marker.Result, child_pid = child.Id, child_exit_code = child.ExitCode,
            phase, committed_before_termination = committed, semantic_retry_status = retry.Status.ToString(),
            original_saved_draft_learning_and_checkpoint_preserved = true, reward_once = 10, experiment = "actual operating-system process termination; not physical power loss" });
    }

    private static void ReleaseAndSchemaMismatch()
    {
        string root = Root("release-schema-mismatch"); SeedState seed; string main;
        using (var initial = new ProfileStore(root, Release)) { seed = Seed(initial); main = initial.MainPath; }
        string originalHash = HashFile(main);
        TestRegistry.Throws<StoreVersionException>(() => { using var refused = new ProfileStore(root, "missing-old-content-release-fixture"); });
        TestRegistry.Require(HashFile(main) == originalHash);
        using (var reopened = new ProfileStore(root, Release)) RequirePreserved(reopened, seed);
        using (var db = Raw(main)) Sql(db, "PRAGMA user_version=999");
        string highSchemaHash = HashFile(main);
        TestRegistry.Throws<StoreVersionException>(() => { using var refused = new ProfileStore(root, Release); });
        TestRegistry.Require(HashFile(main) == highSchemaHash);
        Evidence(root, "release-schema-mismatch", new { mismatched_release_rejected = true, actual_newer_fixture_schema = 999,
            high_schema_original_sha256 = highSchemaHash, high_schema_original_unchanged = true,
            actual_older_distributed_game_executable = "not_run", scope = "current executable release binding/schema guard; no content substitution" });
    }
}
