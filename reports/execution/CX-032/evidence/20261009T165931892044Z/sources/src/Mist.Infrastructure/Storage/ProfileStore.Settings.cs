using Microsoft.Data.Sqlite;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore
{
    public ValueTask<string?> GetSettingAsync(string key) => _writer.Enqueue(() => { using var db=SqliteSupport.Open(_mainPath,readOnly:true); return SqliteSupport.Text(db,null,"SELECT value_json FROM settings WHERE key=$k",("$k",key)); });
    public ValueTask SetSettingAsync(string key,string json) => new(_writer.Enqueue(() =>
    {
        ValidateJson(json); using var db=SqliteSupport.Open(_mainPath);
        SqliteSupport.Exec(db,null,"INSERT INTO settings VALUES($k,$j) ON CONFLICT(key) DO UPDATE SET value_json=$j",("$k",key),("$j",json)); return true;
    }).AsTask());
    public ValueTask<bool> TryLeaseAsync(string resource,string eventId) => TryLeasesAsync(Session.Load,eventId,[resource]);
    public ValueTask<bool> TryLeasesAsync(Mist.Core.Contracts.LoadContext load,string eventId,System.Collections.Immutable.ImmutableArray<string> resources) => _writer.Enqueue(() =>
    {
        if(load!=Session.Load||resources.Length==0||resources.Distinct().Count()!=resources.Length||resources.Any(string.IsNullOrWhiteSpace))return false;
        using var db=SqliteSupport.Open(_mainPath); using var tx=db.BeginTransaction(deferred:false);
        SqliteSupport.Exec(db,tx,"DELETE FROM leases WHERE branch_id=$b AND load_context<>$l",("$b",Session.Branch.Value.ToString()),("$l",Session.Load.Value.ToString()));
        foreach(string resource in resources)if(SqliteSupport.Number(db,tx,"SELECT count(*) FROM leases WHERE branch_id=$b AND resource_id=$r AND event_id<>$e",("$b",Session.Branch.Value.ToString()),("$r",resource),("$e",eventId))>0)return false;
        foreach(string resource in resources)SqliteSupport.Exec(db,tx,"INSERT OR IGNORE INTO leases VALUES($b,$r,$e,$l)",("$b",Session.Branch.Value.ToString()),("$r",resource),("$e",eventId),("$l",Session.Load.Value.ToString())); tx.Commit(); return true;
    });
    public ValueTask ReleaseLeaseAsync(string resource,string eventId) => new(_writer.Enqueue(() =>
    {
        using var db=SqliteSupport.Open(_mainPath); SqliteSupport.Exec(db,null,"DELETE FROM leases WHERE branch_id=$b AND resource_id=$r AND event_id=$e AND load_context=$l",("$b",Session.Branch.Value.ToString()),("$r",resource),("$e",eventId),("$l",Session.Load.Value.ToString())); return true;
    }).AsTask());
    public ValueTask ReleaseLeasesAsync(Mist.Core.Contracts.LoadContext load,string eventId) => new(_writer.Enqueue(() =>
    {
        if(load!=Session.Load)return false;using var db=SqliteSupport.Open(_mainPath);SqliteSupport.Exec(db,null,"DELETE FROM leases WHERE branch_id=$b AND event_id=$e AND load_context=$l",("$b",Session.Branch.Value.ToString()),("$e",eventId),("$l",load.Value.ToString()));return true;
    }).AsTask());
}
