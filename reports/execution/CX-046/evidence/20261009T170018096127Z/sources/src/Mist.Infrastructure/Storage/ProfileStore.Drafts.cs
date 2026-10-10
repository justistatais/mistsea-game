using Microsoft.Data.Sqlite;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore
{
    public ValueTask<DraftSaved> SaveAsync(SaveDraft request, CancellationToken cancellationToken = default) => _writer.Enqueue(() =>
    {
        Guard(request.Profile, request.Branch, request.Load);
        if (string.IsNullOrWhiteSpace(request.TaskInstanceId) || string.IsNullOrWhiteSpace(request.MaterialVersion)) throw new ArgumentException("Draft requires task and material identities.");
        using var db = SqliteSupport.Open(_mainPath);
        using var tx = db.BeginTransaction(deferred: false);
        var head = ReadDraft(db, tx, request.Branch.Value.ToString(), request.TaskInstanceId);
        if ((head?.Revision ?? 0) != request.ExpectedHeadRevision || request.Draft is not null && head?.Id != request.Draft) throw new StoreConflictException("draft_compare_and_swap_failed");
        var id = head is null || head.Deleted ? new DraftId(Guid.NewGuid()) : head.Id;
        string d = id.Value.ToString();
        long revision = SqliteSupport.Number(db, tx, "SELECT COALESCE(MAX(revision),0)+1 FROM draft_revisions WHERE draft_id=$d", ("$d", d));
        string body = UnicodeOffsets.NormalizeNewlines(request.Text), hash = Hash(body);
        SqliteSupport.Exec(db, tx, "INSERT INTO draft_revisions VALUES($d,$r,$b,$m,$t,$h,0); INSERT INTO draft_heads VALUES($b,$k,$d,$r) ON CONFLICT(branch_id,task_id) DO UPDATE SET draft_id=$d,revision=$r",
            ("$d", d), ("$r", revision), ("$b", request.Branch.Value.ToString()), ("$m", request.MaterialVersion), ("$t", body), ("$h", hash), ("$k", request.TaskInstanceId));
        _fault?.Invoke("draft_before_commit");
        tx.Commit();
        return new DraftSaved(id, revision, hash);
    }, cancellationToken);
    public ValueTask<DraftView?> GetDraftAsync(string taskId) => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath, readOnly: true);
        return ReadDraft(db, null, Session.Branch.Value.ToString(), taskId);
    });
    private static DraftView? ReadDraft(SqliteConnection db, SqliteTransaction? tx, string branch, string task)
    {
        using var command = SqliteSupport.Command(db, tx, "SELECT r.draft_id,r.revision,r.body,r.body_hash,r.deleted FROM draft_heads h JOIN draft_revisions r ON r.draft_id=h.draft_id AND r.revision=h.revision WHERE h.branch_id=$b AND h.task_id=$t", ("$b", branch), ("$t", task));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        bool deleted = reader.GetInt64(4) == 1;
        string? body = deleted || reader.IsDBNull(2) ? null : reader.GetString(2);
        string hash = reader.GetString(3);
        if (body is not null && Hash(body) != hash) throw new InvalidDataException("Draft text checksum mismatch.");
        return new(new(Guid.Parse(reader.GetString(0))), reader.GetInt64(1), body, hash, deleted);
    }
}
