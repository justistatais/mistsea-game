using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore
{
    public ValueTask<BranchSnapshot> SnapshotAsync(CancellationToken cancellationToken = default) => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath, readOnly: true);
        using var tx = db.BeginTransaction(deferred: true);
        return Snapshot(db, tx, Session.Branch.Value.ToString());
    }, cancellationToken);
    private static ImmutableArray<T> Rows<T>(SqliteConnection db, SqliteTransaction? tx, string sql, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] parameters)
    {
        var rows = ImmutableArray.CreateBuilder<T>();
        using var c = SqliteSupport.Command(db, tx, sql, parameters);
        using var r = c.ExecuteReader();
        while (r.Read()) rows.Add(read(r));
        return rows.ToImmutable();
    }
    private static BranchSnapshot Snapshot(SqliteConnection db, SqliteTransaction tx, string b) => new((int)SqliteSupport.Number(db,tx,"PRAGMA user_version"),
        SqliteSupport.Text(db, tx, "SELECT release_id FROM branches WHERE branch_id=$b", ("$b", b))!,
        SqliteSupport.Number(db, tx, "SELECT sequence FROM branches WHERE branch_id=$b", ("$b", b)),
        Rows(db, tx, "SELECT entity_id,kind,revision,payload_json FROM entities WHERE branch_id=$b ORDER BY entity_id", r => new StoredEntity(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3)), ("$b", b)),
        Rows(db, tx, "SELECT item_id,definition_id,owner_kind,owner_id,slot FROM items WHERE branch_id=$b ORDER BY item_id", r => new StoredItem(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)), ("$b", b)),
        Rows(db, tx, "SELECT account,quantity FROM balances WHERE branch_id=$b ORDER BY account", r => new KeyValuePair<string, long>(r.GetString(0), r.GetInt64(1)), ("$b", b)).ToImmutableDictionary(),
        Rows(db, tx, "SELECT semantic_key,receipt_id,action_type,effect_hash,result_json,commit_sequence FROM receipts WHERE branch_id=$b ORDER BY semantic_key", r => new StoredReceipt(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt64(5)), ("$b", b)),
        Rows(db, tx, "SELECT command_id,request_hash,receipt_id FROM command_log WHERE branch_id=$b ORDER BY command_id", r => new StoredCommand(r.GetString(0), r.GetString(1), r.GetString(2)), ("$b", b)),
        Rows(db, tx, "SELECT event_id,receipt_id,event_type,payload_json,displayed FROM outbox WHERE branch_id=$b ORDER BY event_id", r => new StoredOutbox(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) == 1), ("$b", b)),
        Rows(db, tx, "SELECT task_id,draft_id,revision FROM draft_heads WHERE branch_id=$b ORDER BY task_id", r => new StoredHead(r.GetString(0), r.GetString(1), r.GetInt64(2)), ("$b", b)),
        Rows(db, tx, "SELECT subject_id,revision,payload_json FROM permissions WHERE branch_id=$b ORDER BY subject_id", r => new StoredPermission(r.GetString(0), r.GetInt64(1), r.GetString(2)), ("$b", b)));
    public ValueTask<string> CreateCheckpointAsync(string category, int manualIndex = 0, string? protectedId = null) => _writer.Enqueue(() =>
    {
        if (category != "manual" && category != "automatic" && category != "protected") throw new ArgumentException("Unknown checkpoint category.");
        if (category == "manual" && manualIndex is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(manualIndex));
        if (category == "protected" && string.IsNullOrWhiteSpace(protectedId)) throw new ArgumentException("Protected checkpoint needs its own identity.");
        using var db = SqliteSupport.Open(_mainPath);
        using var tx = db.BeginTransaction(deferred: false);
        var snapshot = Snapshot(db, tx, Session.Branch.Value.ToString());
        string json = StrictJson.Serialize(snapshot), id = Guid.NewGuid().ToString();
        string slot = category == "manual" ? "manual:" + manualIndex : "protected:" + protectedId;
        if (category == "automatic")
        {
            long next = SqliteSupport.Number(db, tx, "SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='automatic_next'),0)");
            slot = "automatic:" + next % 5;
            SetMeta(db, tx, "automatic_next", (next + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        SqliteSupport.Exec(db, tx, "INSERT INTO checkpoints VALUES($i,$b,$r,$v,$q,$j,$h); INSERT INTO save_slots VALUES($s,$c,$i) ON CONFLICT(slot_id) DO UPDATE SET checkpoint_id=$i",
            ("$i", id), ("$b", Session.Branch.Value.ToString()), ("$r", snapshot.ReleaseId), ("$v",snapshot.SchemaVersion), ("$q", snapshot.Sequence), ("$j", json), ("$h", Hash(json)), ("$s", slot), ("$c", category));
        tx.Commit();
        return id;
    });
    public ValueTask<ImmutableDictionary<string, string>> SlotsAsync() => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath, readOnly: true);
        return Rows(db, null, "SELECT slot_id,checkpoint_id FROM save_slots ORDER BY slot_id", r => new KeyValuePair<string, string>(r.GetString(0), r.GetString(1))).ToImmutableDictionary();
    });
    public ValueTask<SessionView> ContinueAsync() => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath, readOnly: true);
        _session = ReadSession(db, Session.Profile, new(Guid.Parse(Meta(db, "active_branch"))), new(Guid.NewGuid()));
        return Session;
    });
    public ValueTask<SessionView> LoadCheckpointAsync(string checkpoint) => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath);
        using var tx = db.BeginTransaction(deferred: false);
        string json, source;
        using (var c = SqliteSupport.Command(db, tx, "SELECT snapshot_json,checksum,source_branch_id,snapshot_schema,release_id FROM checkpoints WHERE checkpoint_id=$i", ("$i", checkpoint)))
        using (var r = c.ExecuteReader())
        {
            if (!r.Read()) throw new InvalidDataException("Checkpoint missing.");
            json = r.GetString(0); source = r.GetString(2);
            if (Hash(json) != r.GetString(1) || r.GetInt64(3) is <1 or >2 || r.GetString(4) != Session.ReleaseId) throw new InvalidDataException("Checkpoint checksum/schema/release mismatch.");
        }
        var snapshot = StrictJson.Deserialize<BranchSnapshot>(json);
        if (snapshot.SchemaVersion is <1 or >2 || snapshot.ReleaseId != Session.ReleaseId) throw new StoreVersionException("Snapshot incompatible.");
        string b = Guid.NewGuid().ToString();
        SqliteSupport.Exec(db, tx, "INSERT INTO branches VALUES($b,$p,$c,$r,$q)", ("$b", b), ("$p", source), ("$c", checkpoint), ("$r", snapshot.ReleaseId), ("$q", snapshot.Sequence));
        foreach (var e in snapshot.Entities) SqliteSupport.Exec(db, tx, "INSERT INTO entities VALUES($b,$i,$k,$r,$j)", ("$b", b), ("$i", e.Id), ("$k", e.Kind), ("$r", e.Revision), ("$j", e.Json));
        foreach (var i in snapshot.Items) SqliteSupport.Exec(db, tx, "INSERT INTO items VALUES($b,$i,$d,$k,$o,$s)", ("$b", b), ("$i", i.Id), ("$d", i.DefinitionId), ("$k", i.OwnerKind), ("$o", i.OwnerId), ("$s", i.Slot));
        foreach (var q in snapshot.Balances) SqliteSupport.Exec(db, tx, "INSERT INTO balances VALUES($b,$a,$q)", ("$b", b), ("$a", q.Key), ("$q", q.Value));
        foreach (var r in snapshot.Receipts) SqliteSupport.Exec(db, tx, "INSERT INTO receipts VALUES($b,$s,$i,$a,$h,$j,$q)", ("$b", b), ("$s", r.SemanticKey), ("$i", r.ReceiptId), ("$a", r.ActionType), ("$h", r.EffectHash), ("$j", r.ResultJson), ("$q", r.Sequence));
        foreach (var c in snapshot.Commands) SqliteSupport.Exec(db, tx, "INSERT INTO command_log VALUES($b,$c,$h,$r)", ("$b", b), ("$c", c.CommandId), ("$h", c.RequestHash), ("$r", c.ReceiptId));
        foreach (var e in snapshot.Outbox) SqliteSupport.Exec(db, tx, "INSERT INTO outbox VALUES($b,$e,$r,$t,$j,$d)", ("$b", b), ("$e", e.EventId), ("$r", e.ReceiptId), ("$t", e.EventType), ("$j", e.Json), ("$d", e.Displayed ? 1 : 0));
        foreach (var h in snapshot.DraftHeads) SqliteSupport.Exec(db, tx, "INSERT INTO draft_heads VALUES($b,$t,$d,$r)", ("$b", b), ("$t", h.TaskId), ("$d", h.DraftId), ("$r", h.Revision));
        foreach (var p in snapshot.Permissions) SqliteSupport.Exec(db, tx, "INSERT INTO permissions VALUES($b,$s,$r,$j)", ("$b", b), ("$s", p.SubjectId), ("$r", p.Revision), ("$j", p.Json));
        SetMeta(db, tx, "active_branch", b);
        tx.Commit();
        _session = ReadSession(db, Session.Profile, new(Guid.Parse(b)), new(Guid.NewGuid()));
        return Session;
    });
    public ValueTask MarkOutboxDisplayedAsync(string eventId) => new(_writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath);
        SqliteSupport.Exec(db, null, "UPDATE outbox SET displayed=1 WHERE branch_id=$b AND event_id=$e", ("$b", Session.Branch.Value.ToString()), ("$e", eventId));
        return true;
    }).AsTask());
}
