using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Infrastructure.Storage;

/// <summary>One profile, one serialized writer. Views are immutable and never contain Godot objects.</summary>
public sealed partial class ProfileStore : IAdventureRepository, Mist.Application.EventScheduler.IEventLeaseStore, IDisposable
{
    private readonly SingleWriter _writer = new();
    private readonly Mutex _profileMutex;
    private readonly Action<string>? _fault;
    private SessionView _session = null!;
    private string _mainPath;
    public string RootPath { get; }
    public string MainPath => _mainPath;
    public string ControlPath => Path.Combine(RootPath, "control.sqlite");
    public SessionView Session => Volatile.Read(ref _session);
    public ProfileStore(string root, string releaseId, Action<string>? fault = null, string? recoveryBackupId = null)
    {
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Directory.CreateDirectory(RootPath);
        _profileMutex=new Mutex(false,"MistSea-profile-"+Hash(OperatingSystem.IsWindows()?RootPath.ToUpperInvariant():RootPath));
        _writer.Synchronize=LockProfile;
        _fault = fault;
        _mainPath = Path.Combine(RootPath, "profile.sqlite");
        _writer.BeforeEach=GuardActiveDatabase;
        try
        {
            bool anyProfile = File.Exists(_mainPath) || Directory.Exists(Path.Combine(RootPath, "backups")) && Directory.EnumerateFiles(Path.Combine(RootPath, "backups")).Any();
            if (anyProfile && !File.Exists(ControlPath)) throw new MissingControlException("删除控制库缺失。停止写入；请恢复原 control.sqlite，不能自动重建为世代零。");
            using var control = SqliteSupport.Open(ControlPath);
            if (SqliteSupport.Number(control, null, "PRAGMA user_version") == 0) SqliteSupport.Exec(control, null, Schema("001_control.sql"));
            string? pointed = SqliteSupport.Text(control, null, "SELECT current_path FROM recovery_pointers LIMIT 1");
            if (pointed is not null) _mainPath = ManagedPath(pointed);
            if (recoveryBackupId is not null)
            {
                string profile = SqliteSupport.Text(control, null, "SELECT profile_id FROM profile_controls LIMIT 1") ?? throw new MissingControlException("No recovery profile identity.");
                string relative = SqliteSupport.Text(control, null, "SELECT relative_path FROM backup_registry WHERE backup_id=$i AND validated=1", ("$i", recoveryBackupId)) ?? throw new InvalidDataException("Backup not registered.");
                using var backup = SqliteSupport.Open(ManagedPath(relative), readOnly: true);
                ValidateDatabase(backup, releaseId, profile);
                _session = ReadSession(backup, new(Guid.Parse(profile)), new(Guid.Parse(Meta(backup, "active_branch"))), new(Guid.NewGuid()));
                RestoreBackupAsync(recoveryBackupId).GetAwaiter().GetResult();
                return;
            }
            if (pointed is not null && !File.Exists(_mainPath)) throw new InvalidDataException("Recovery pointer target is missing; original is preserved.");
            using var db = SqliteSupport.Open(_mainPath);
            if (SqliteSupport.Number(db, null, "PRAGMA user_version") == 0)
            {
                if (anyProfile) throw new InvalidDataException("Existing database has no recognized schema; refusing to initialize it.");
                var profile = new ProfileId(Guid.NewGuid());
                var branch = new BranchId(Guid.NewGuid());
                using var tx = db.BeginTransaction();
                SqliteSupport.Exec(db, tx, Schema("001_profile.sql"));
                SetMeta(db, tx, "profile_id", profile.Value.ToString());
                SetMeta(db, tx, "active_branch", branch.Value.ToString());
                SqliteSupport.Exec(db, tx, "INSERT INTO branches(branch_id,release_id) VALUES($b,$r)", ("$b", branch.Value.ToString()), ("$r", releaseId));
                tx.Commit();
                SqliteSupport.Exec(control, null, "INSERT INTO profile_controls VALUES($p,0,0,0); INSERT INTO recovery_pointers VALUES($p,'profile.sqlite',NULL)", ("$p", profile.Value.ToString()));
            }
            string profileText = Meta(db, "profile_id"), branchText = Meta(db, "active_branch");
            if (SqliteSupport.Number(control, null, "SELECT count(*) FROM profile_controls WHERE profile_id=$p", ("$p", profileText)) != 1)
                throw new MissingControlException("Profile identity has no matching deletion-control record.");
            ValidateDatabase(db, releaseId, profileText);
            _session = ReadSession(db, new ProfileId(Guid.Parse(profileText)), new BranchId(Guid.Parse(branchText)), new LoadContext(Guid.NewGuid()));
            ApplyErasureJobs(control, db);
        }
        catch { _writer.Dispose();_profileMutex.Dispose(); throw; }
    }
    private static string Schema(string suffix)
    {
        var assembly = typeof(ProfileStore).Assembly;
        string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string Meta(SqliteConnection db, string key) => SqliteSupport.Text(db, null, "SELECT value FROM metadata WHERE key=$k", ("$k", key)) ?? throw new InvalidDataException("Missing metadata: " + key);
    private static void SetMeta(SqliteConnection db, SqliteTransaction? tx, string key, string value) => SqliteSupport.Exec(db, tx, "INSERT INTO metadata VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
    private static SessionView ReadSession(SqliteConnection db, ProfileId profile, BranchId branch, LoadContext load)
    {
        string release = SqliteSupport.Text(db, null, "SELECT release_id FROM branches WHERE branch_id=$b", ("$b", branch.Value.ToString())) ?? throw new InvalidDataException("Missing branch.");
        long sequence = SqliteSupport.Number(db, null, "SELECT sequence FROM branches WHERE branch_id=$b", ("$b", branch.Value.ToString()));
        return new(profile, branch, load, release, sequence);
    }
    private void Guard(ProfileId profile, BranchId branch, LoadContext load)
    {
        if (profile != Session.Profile || branch != Session.Branch || load != Session.Load) throw new StoreConflictException("stale_load_context");
    }
    private void GuardActiveDatabase()
    {
        if(_session is null)return;
        using var control=SqliteSupport.Open(ControlPath,readOnly:true);string? active=SqliteSupport.Text(control,null,"SELECT current_path FROM recovery_pointers WHERE profile_id=$i",("$i",Session.Profile.Value.ToString()));
        if(active is null||!Path.GetFullPath(ManagedPath(active)).Equals(_mainPath,StringComparison.OrdinalIgnoreCase))throw new StoreConflictException("active_database_pointer_changed: reopen profile before any further operation");
    }
    private IDisposable LockProfile()
    {
        bool acquired;try{acquired=_profileMutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){acquired=true;}
        if(!acquired)throw new StoreConflictException("profile_writer_busy: request was not committed");return new MutexLease(_profileMutex);
    }
    private sealed class MutexLease(Mutex mutex):IDisposable{public void Dispose()=>mutex.ReleaseMutex();}
    public ValueTask<CommandResult> ExecuteAsync(IWorldCommand command, CancellationToken cancellationToken = default) => _writer.Enqueue(() =>
    {
        if (command is not CommitPlanCommand planned) return Reject("unsupported_command");
        var context = planned.Context;
        try
        {
            Guard(context.Profile, context.Branch, context.Load);
            if (context.ReleaseId != Session.ReleaseId) return Reject("release_mismatch");
            using var db = SqliteSupport.Open(_mainPath);
            using var tx = db.BeginTransaction(deferred: false);
            var plan = planned.Plan;
            _ = new SemanticSlot(plan.SemanticKey);
            ValidateJson(plan.ResultJson);
            string requestHash = Hash(StrictJson.Serialize(planned));
            string b = context.Branch.Value.ToString(), c = context.Command.Value.ToString();
            using (var known = SqliteSupport.Command(db, tx, "SELECT request_hash,receipt_id FROM command_log WHERE branch_id=$b AND command_id=$c", ("$b", b), ("$c", c)))
            using (var reader = known.ExecuteReader())
            {
                if (reader.Read()) return reader.GetString(0) == requestHash ? ExistingReceipt(db, tx, b, reader.GetString(1)) : Reject("command_id_reused");
            }
            string? semanticReceipt = SqliteSupport.Text(db, tx, "SELECT receipt_id FROM receipts WHERE branch_id=$b AND semantic_key=$s", ("$b", b), ("$s", plan.SemanticKey));
            if (semanticReceipt is not null)
            {
                SqliteSupport.Exec(db, tx, "INSERT INTO command_log VALUES($b,$c,$h,$r)", ("$b", b), ("$c", c), ("$h", requestHash), ("$r", semanticReceipt));
                var existing = ExistingReceipt(db, tx, b, semanticReceipt);
                tx.Commit();
                return existing;
            }
            if (context.Expected.Select(e => e.EntityId).Distinct().Count() != context.Expected.Length) return Reject("duplicate_read_stamp");
            foreach (var stamp in context.Expected)
            {
                long actual = Revision(db, tx, b, stamp.EntityId);
                if (actual != stamp.Revision) return Reject("stale_relevant_read", stamp.EntityId);
            }
            if (context.ExpectedDraft is { } draftStamp)
            {
                var head = ReadDraft(db, tx, b, draftStamp.TaskId);
                if (head?.Id != draftStamp.Draft || (head?.Revision ?? 0) != draftStamp.Revision || head?.Hash != draftStamp.Hash) return Reject("stale_draft_read");
            }
            foreach (var e in plan.Entities)
            {
                ValidateJson(e.Json);
                if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Kind) || Revision(db, tx, b, e.Id) != e.ExpectedRevision) return Reject("entity_conflict", e.Id);
                SqliteSupport.Exec(db, tx, "INSERT INTO entities VALUES($b,$i,$k,$r,$j) ON CONFLICT(branch_id,entity_id) DO UPDATE SET kind=$k,revision=$r,payload_json=$j", ("$b", b), ("$i", e.Id), ("$k", e.Kind), ("$r", e.ExpectedRevision + 1), ("$j", e.Json));
            }
            _fault?.Invoke("after_entities");
            foreach (var item in plan.Items)
            {
                string? owner = SqliteSupport.Text(db, tx, "SELECT owner_kind || ':' || owner_id FROM items WHERE branch_id=$b AND item_id=$i", ("$b", b), ("$i", item.Id));
                if (owner is null && item.ExpectedOwnerKind != "new" || owner is not null && owner != item.ExpectedOwnerKind + ":" + item.ExpectedOwnerId) throw new StoreConflictException("item_owner_conflict");
                SqliteSupport.Exec(db, tx, "INSERT INTO items VALUES($b,$i,$d,$k,$o,$s) ON CONFLICT(branch_id,item_id) DO UPDATE SET owner_kind=$k,owner_id=$o,slot=$s", ("$b", b), ("$i", item.Id), ("$d", item.DefinitionId), ("$k", item.OwnerKind), ("$o", item.OwnerId), ("$s", item.Slot));
            }
            foreach (var balance in plan.Balances)
            {
                long old = SqliteSupport.Number(db, tx, "SELECT COALESCE((SELECT quantity FROM balances WHERE branch_id=$b AND account=$a),0)", ("$b", b), ("$a", balance.Account));
                long updated = checked(old + balance.Delta);
                if (updated < 0) throw new StoreConflictException("insufficient_balance");
                SqliteSupport.Exec(db, tx, "INSERT INTO balances VALUES($b,$a,$q) ON CONFLICT(branch_id,account) DO UPDATE SET quantity=$q", ("$b", b), ("$a", balance.Account), ("$q", updated));
            }
            _fault?.Invoke("after_balances");
            var receipt = new ReceiptId(Guid.NewGuid());
            long sequence = checked(SqliteSupport.Number(db, tx, "SELECT sequence FROM branches WHERE branch_id=$b", ("$b", b)) + 1);
            SqliteSupport.Exec(db, tx, "INSERT INTO receipts VALUES($b,$s,$r,$a,$h,$j,$q); INSERT INTO command_log VALUES($b,$c,$h,$r); INSERT INTO outbox VALUES($b,$e,$r,'world_receipt',$j,0); UPDATE branches SET sequence=$q WHERE branch_id=$b",
                ("$b", b), ("$s", plan.SemanticKey), ("$r", receipt.Value.ToString()), ("$a", plan.ActionType), ("$h", requestHash), ("$j", plan.ResultJson), ("$q", sequence), ("$c", c), ("$e", Guid.NewGuid().ToString()));
            _fault?.Invoke("before_commit");
            tx.Commit();
            _session = Session with { Sequence = sequence };
            _fault?.Invoke("after_commit");
            return new CommandResult(CommitStatus.Committed, new(receipt, context.Branch, new(plan.SemanticKey), plan.ActionType, sequence), []);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { return new(CommitStatus.Busy, null, [new("database_busy", "", "数据库忙，可按原请求重试。")]); }
        catch (StoreConflictException ex) { return Reject(ex.Message); }
    }, cancellationToken);
    private static CommandResult Reject(string code, string entity = "") => new(CommitStatus.Rejected, null, [new(code, entity, "状态已经变化，请重新预览。")]);
    private static void ValidateJson(string json) { using var doc = JsonDocument.Parse(json); StrictJson.CheckDuplicates(doc.RootElement); }
    private static long Revision(SqliteConnection db, SqliteTransaction? tx, string branch, string id) => SqliteSupport.Number(db, tx, "SELECT COALESCE((SELECT revision FROM entities WHERE branch_id=$b AND entity_id=$i),-1)", ("$b", branch), ("$i", id));
    private static CommandResult ExistingReceipt(SqliteConnection db, SqliteTransaction? tx, string branch, string receipt)
    {
        using var command = SqliteSupport.Command(db, tx, "SELECT semantic_key,action_type,commit_sequence FROM receipts WHERE branch_id=$b AND receipt_id=$r", ("$b", branch), ("$r", receipt));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("Command references a missing receipt.");
        return new(CommitStatus.AlreadyCommitted, new(new(Guid.Parse(receipt)), new(Guid.Parse(branch)), new(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2)), []);
    }
    public ValueTask<WorldReceipt?> FindReceiptAsync(BranchId branch, CommandId command, CancellationToken cancellationToken = default) => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath, readOnly: true);
        string b = branch.Value.ToString();
        string? id = SqliteSupport.Text(db, null, "SELECT receipt_id FROM command_log WHERE branch_id=$b AND command_id=$c", ("$b", b), ("$c", command.Value.ToString()));
        return id is null ? null : ExistingReceipt(db, null, b, id).Receipt;
    }, cancellationToken);
    private string ManagedPath(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(RootPath, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path is outside managed profile storage.");
        for (string? current = path; current is not null && current.Length > RootPath.Length; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Managed storage cannot traverse a symbolic link or junction.");
        return path;
    }
    private static void ValidateDatabase(SqliteConnection db, string release, string profile)
    {
        if (SqliteSupport.Number(db, null, "PRAGMA user_version") is <1 or >2 || Meta(db, "profile_id") != profile) throw new StoreVersionException("Unsupported schema or profile identity.");
        if (SqliteSupport.Text(db, null, "PRAGMA integrity_check") != "ok" || SqliteSupport.Number(db, null, "SELECT count(*) FROM pragma_foreign_key_check") != 0) throw new InvalidDataException("Database integrity check failed; original preserved.");
        if (SqliteSupport.Number(db, null, "SELECT count(*) FROM branches WHERE release_id<>$r", ("$r", release)) != 0) throw new StoreVersionException("Content release mismatch; migration requires an explicit candidate.");
    }
    public void Dispose(){_writer.Dispose();_profileMutex.Dispose();}
    public ValueTask<StorageDiagnostics> DiagnosticsAsync() => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath);
        return new StorageDiagnostics(db.ServerVersion, SqliteSupport.Text(db, null, "PRAGMA journal_mode")!, SqliteSupport.Number(db, null, "PRAGMA synchronous"), SqliteSupport.Number(db, null, "PRAGMA foreign_keys"), SqliteSupport.Number(db, null, "PRAGMA busy_timeout"), SqliteSupport.Number(db, null, "PRAGMA user_version"), Thread.CurrentThread.Name!);
    });
}
