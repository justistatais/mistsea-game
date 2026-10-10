using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore
{
    private static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public ValueTask<BackupInfo> BackupAsync() => _writer.Enqueue(() =>
    {
        Directory.CreateDirectory(ManagedPath("backups"));
        string id = Guid.NewGuid().ToString(), relative = "backups/" + id + ".sqlite", path = ManagedPath(relative);
        using (var source = SqliteSupport.Open(_mainPath, readOnly: true))
        using (var target = SqliteSupport.Open(path)) source.BackupDatabase(target);
        long sequence;
        using (var verify = SqliteSupport.Open(path, readOnly: true))
        {
            ValidateDatabase(verify, Session.ReleaseId, Session.Profile.Value.ToString());
            sequence = ReadSession(verify, Session.Profile, Session.Branch, Session.Load).Sequence;
        }
        string hash = FileHash(path);
        using var control = SqliteSupport.Open(ControlPath);
        SqliteSupport.Exec(control, null, "INSERT INTO backup_registry VALUES($i,$p,$h,$q,1,1)", ("$i", id), ("$p", relative), ("$h", hash), ("$q", sequence));
        return new BackupInfo(id, path, hash, sequence);
    });
    public ValueTask<SessionView> RestoreBackupAsync(string id) => _writer.Enqueue(() =>
    {
        using var control = SqliteSupport.Open(ControlPath);
        var registered = Rows(control, null, "SELECT relative_path,sha256,sequence,schema_version,validated FROM backup_registry WHERE backup_id=$i", r => (Path: r.GetString(0), Hash: r.GetString(1), Sequence: r.GetInt64(2), Schema: r.GetInt64(3), Validated: r.GetInt64(4)), ("$i", id));
        if (registered.Length != 1 || registered[0].Validated != 1 || registered[0].Schema != 1) throw new InvalidDataException("Backup is not validated.");
        var backup = registered[0];
        string path = ManagedPath(backup.Path);
        if (!File.Exists(path) || FileHash(path) != backup.Hash) throw new InvalidDataException("Backup missing or checksum mismatch.");
        Directory.CreateDirectory(ManagedPath("recovery"));
        string relative = "recovery/candidate-" + Guid.NewGuid() + ".sqlite", candidate = ManagedPath(relative);
        SqliteSupport.Exec(control, null, "UPDATE recovery_pointers SET pending_candidate=$p WHERE profile_id=$i", ("$p", relative), ("$i", Session.Profile.Value.ToString()));
        using (var source = SqliteSupport.Open(path, readOnly: true))
        using (var target = SqliteSupport.Open(candidate)) source.BackupDatabase(target);
        using (var verify = SqliteSupport.Open(candidate))
        {
            ValidateDatabase(verify, Session.ReleaseId, Session.Profile.Value.ToString());
            string branch = Meta(verify, "active_branch");
            if (ReadSession(verify, Session.Profile, new(Guid.Parse(branch)), Session.Load).Sequence != backup.Sequence) throw new InvalidDataException("Backup sequence mismatch.");
            ApplyErasureJobs(control, verify);
            _fault?.Invoke("recovery_before_pointer");
            SqliteSupport.Exec(control, null, "UPDATE recovery_pointers SET current_path=$p,pending_candidate=NULL WHERE profile_id=$i", ("$p", relative), ("$i", Session.Profile.Value.ToString()));
            _mainPath = candidate;
            _session = ReadSession(verify, Session.Profile, new(Guid.Parse(branch)), new(Guid.NewGuid()));
        }
        return Session;
    });
    public ValueTask<string> CreateMigrationCandidateAsync(Action<SqliteConnection, SqliteTransaction> migration) => _writer.Enqueue(() =>
    {
        Directory.CreateDirectory(ManagedPath("recovery"));
        string candidate = ManagedPath("recovery/migration-" + Guid.NewGuid() + ".sqlite");
        using (var source = SqliteSupport.Open(_mainPath, readOnly: true))
        using (var target = SqliteSupport.Open(candidate)) source.BackupDatabase(target);
        using (var db = SqliteSupport.Open(candidate))
        using (var tx = db.BeginTransaction(deferred: false))
        {
            migration(db, tx);
            tx.Commit();
        }
        using (var verify = SqliteSupport.Open(candidate, readOnly: true)) ValidateDatabase(verify, Session.ReleaseId, Session.Profile.Value.ToString());
        return candidate; // Explicit promotion is separate; a failed candidate never replaces the original.
    });
    public ValueTask RemoveManagedBackupAsync(string id) => new(_writer.Enqueue(() =>
    {
        using var control = SqliteSupport.Open(ControlPath);
        string? relative = SqliteSupport.Text(control, null, "SELECT relative_path FROM backup_registry WHERE backup_id=$i", ("$i", id));
        if (relative is null) throw new InvalidDataException("Backup identity unknown.");
        string path = ManagedPath(relative);
        if (!relative.Replace('\\', '/').StartsWith("backups/", StringComparison.Ordinal) || path.Equals(_mainPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Cannot remove this path.");
        File.Delete(path);
        SqliteSupport.Exec(control, null, "DELETE FROM backup_registry WHERE backup_id=$i", ("$i", id));
        return true;
    }).AsTask());
}
