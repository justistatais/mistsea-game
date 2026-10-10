using Microsoft.Data.Sqlite;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.Learning;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore
{
    private PrivacyControl Control(SqliteConnection control) => new(
        SqliteSupport.Number(control, null, "SELECT learning_epoch FROM profile_controls WHERE profile_id=$p", ("$p", Session.Profile.Value.ToString())),
        SqliteSupport.Number(control, null, "SELECT consent_version FROM profile_controls WHERE profile_id=$p", ("$p", Session.Profile.Value.ToString())));
    public ValueTask<PrivacyControl> PrivacyAsync() => _writer.Enqueue(() => { using var c = SqliteSupport.Open(ControlPath, readOnly: true); return Control(c); });
    public ValueTask<LearningView> ObserveAsync(LearningObservation observation) => _writer.Enqueue(() =>
    {
        using var db = SqliteSupport.Open(_mainPath);
        using var control = SqliteSupport.Open(ControlPath, readOnly: true);
        var privacy = Control(control);
        bool seen = SqliteSupport.Number(db, null, "SELECT count(*) FROM observations WHERE family_id=$f AND attempt_id<>$a", ("$f", observation.FamilyId), ("$a", observation.AttemptId)) > 0;
        var view = ObservationRules.Project(observation, seen);
        if (!observation.PersistenceComplete) return view;
        SqliteSupport.Exec(db, null, "INSERT INTO observations VALUES($i,$e,$b,$f,$a,$s,$j) ON CONFLICT(profile_epoch,attempt_id) DO NOTHING",
            ("$i", observation.Id), ("$e", privacy.Epoch), ("$b", Session.Branch.Value.ToString()), ("$f", observation.FamilyId), ("$a", observation.AttemptId), ("$s", view.State), ("$j", StrictJson.Serialize(observation)));
        return view;
    });
    public ValueTask<long> ObservationCountAsync() => _writer.Enqueue(() => { using var db = SqliteSupport.Open(_mainPath, readOnly: true); return SqliteSupport.Number(db, null, "SELECT count(*) FROM observations"); });
    public ValueTask<PrivacyControl> SetExternalConsentAsync(bool allow, bool explicitConfirmation) => _writer.Enqueue(() =>
    {
        if (allow && !explicitConfirmation) throw new InvalidOperationException("External consent requires an explicit confirmation, not page navigation.");
        using var control = SqliteSupport.Open(ControlPath);
        SqliteSupport.Exec(control, null, "UPDATE profile_controls SET consent_version=consent_version+1,external_allowed=$a WHERE profile_id=$p", ("$a", allow ? 1 : 0), ("$p", Session.Profile.Value.ToString()));
        return Control(control);
    });
    public ValueTask<PrivacyControl> EraseAsync(string scope, DraftId? draft = null) => _writer.Enqueue(() =>
    {
        if (scope != "learning" && scope != "draft") throw new ArgumentException("Unknown erasure scope.");
        if (scope == "draft" && draft is null) throw new ArgumentException("Draft erasure requires a draft identity.");
        using var control = SqliteSupport.Open(ControlPath);
        using (var tx = control.BeginTransaction(deferred: false))
        {
            SqliteSupport.Exec(control, tx, "UPDATE profile_controls SET learning_epoch=learning_epoch+1,consent_version=consent_version+1,external_allowed=0 WHERE profile_id=$p", ("$p", Session.Profile.Value.ToString()));
            long epoch = SqliteSupport.Number(control, tx, "SELECT learning_epoch FROM profile_controls WHERE profile_id=$p", ("$p", Session.Profile.Value.ToString()));
            SqliteSupport.Exec(control, tx, "INSERT INTO erasure_jobs VALUES($i,$p,$s,$o,$e,'pending')", ("$i", Guid.NewGuid().ToString()), ("$p", Session.Profile.Value.ToString()), ("$s", scope), ("$o", draft?.Value.ToString()), ("$e", epoch));
            tx.Commit();
        }
        _fault?.Invoke("erasure_after_control");
        using var db = SqliteSupport.Open(_mainPath);
        ApplyErasureJobs(control, db);
        return Control(control);
    });
    private void ApplyErasureJobs(SqliteConnection control, SqliteConnection db)
    {
        // Replay every retained tombstone, including applied jobs, when an old backup is restored.
        var jobs = Rows(control, null, "SELECT job_id,scope,object_id,epoch FROM erasure_jobs WHERE profile_id=$p ORDER BY epoch", r => (Id: r.GetString(0), Scope: r.GetString(1), Object: r.IsDBNull(2) ? null : r.GetString(2), Epoch: r.GetInt64(3)), ("$p", Session.Profile.Value.ToString()));
        using (var tx = db.BeginTransaction(deferred: false))
        {
            foreach (var job in jobs)
            {
                if (job.Scope == "learning") SqliteSupport.Exec(db, tx, "DELETE FROM observations WHERE profile_epoch<$e; DELETE FROM assessments WHERE profile_epoch<$e", ("$e", job.Epoch));
                if (job.Scope == "draft") SqliteSupport.Exec(db, tx, "UPDATE draft_revisions SET body=NULL,body_hash='',deleted=1 WHERE draft_id=$d; DELETE FROM assessments WHERE draft_id=$d; UPDATE ai_requests SET status='discarded',response_json=NULL WHERE draft_id=$d", ("$d", job.Object));
            }
            var current = Control(control);
            SqliteSupport.Exec(db, tx, "UPDATE ai_requests SET status='discarded',response_json=NULL WHERE profile_epoch<>$e OR consent_version<>$v", ("$e", current.Epoch), ("$v", current.ConsentVersion));
            tx.Commit();
        }
        SqliteSupport.Exec(control, null, "UPDATE erasure_jobs SET status='applied' WHERE profile_id=$p", ("$p", Session.Profile.Value.ToString()));
    }
    public ValueTask RegisterAssistanceAsync(AssistanceRequest request) => new(_writer.Enqueue(() =>
    {
        using var control = SqliteSupport.Open(ControlPath, readOnly: true);
        var privacy = Control(control);
        if (request.PrivacyEpoch != privacy.Epoch || request.ConsentVersion != privacy.ConsentVersion || request.Load != Session.Load || request.Draft is null || request.Revision is null || SqliteSupport.Number(control, null, "SELECT external_allowed FROM profile_controls WHERE profile_id=$p", ("$p", Session.Profile.Value.ToString())) != 1) throw new StoreConflictException("assistance_not_authorized");
        using var db = SqliteSupport.Open(_mainPath);
        string? body = SqliteSupport.Text(db, null, "SELECT body FROM draft_revisions WHERE draft_id=$d AND revision=$r AND deleted=0", ("$d", request.Draft.Value.Value.ToString()), ("$r", request.Revision));
        if (body is null) throw new StoreConflictException("draft_unavailable");
        SqliteSupport.Exec(db, null, "INSERT INTO ai_requests VALUES($i,$e,$v,$b,$l,$d,$r,'pending',NULL)", ("$i", request.RequestId), ("$e", privacy.Epoch), ("$v", privacy.ConsentVersion), ("$b", Session.Branch.Value.ToString()), ("$l", request.Load.Value.ToString()), ("$d", request.Draft.Value.Value.ToString()), ("$r", request.Revision));
        return true;
    }).AsTask());
    public ValueTask<string> RouteAssistanceAsync(string requestId, string responseJson) => _writer.Enqueue(() =>
    {
        ValidateJson(responseJson);
        using var control = SqliteSupport.Open(ControlPath, readOnly: true);
        var privacy = Control(control);
        using var db = SqliteSupport.Open(_mainPath);
        var rows = Rows(db, null, "SELECT profile_epoch,consent_version,branch_id,load_context,draft_id,draft_revision,status FROM ai_requests WHERE request_id=$i", r => (Epoch: r.GetInt64(0), Consent: r.GetInt64(1), Branch: r.GetString(2), Load: r.GetString(3), Draft: r.GetString(4), Revision: r.GetInt64(5), Status: r.GetString(6)), ("$i", requestId));
        if (rows.Length == 0) return "discarded";
        var request = rows[0];
        bool exists = SqliteSupport.Number(db, null, "SELECT count(*) FROM draft_revisions WHERE draft_id=$d AND revision=$r AND deleted=0", ("$d", request.Draft), ("$r", request.Revision)) == 1;
        string status = request.Status == "discarded" || !exists || request.Epoch != privacy.Epoch || request.Consent != privacy.ConsentVersion ? "discarded" :
            request.Branch != Session.Branch.Value.ToString() ? "archived_branch" :
            request.Load != Session.Load.Value.ToString() || SqliteSupport.Number(db, null, "SELECT count(*) FROM draft_heads WHERE branch_id=$b AND draft_id=$d AND revision=$r", ("$b", request.Branch), ("$d", request.Draft), ("$r", request.Revision)) == 0 ? "old_draft" : "current";
        SqliteSupport.Exec(db, null, "UPDATE ai_requests SET status=$s,response_json=$j WHERE request_id=$i", ("$s", status), ("$j", status == "discarded" ? null : responseJson), ("$i", requestId));
        return status;
    });
}
