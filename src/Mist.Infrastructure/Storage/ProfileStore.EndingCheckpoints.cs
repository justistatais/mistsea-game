using Mist.Application.Contracts;
using Mist.Application.ReleaseReadiness;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.ReleaseReadiness;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore : IEndingCheckpointArchive
{
    public ValueTask<ProtectedEndingCheckpoint> SavePreFinalAsync(SessionView expected,long worldSequence,Guid preparationId,CancellationToken cancellationToken=default)=>_writer.Enqueue(()=>
    {
        var session=Session;
        if(preparationId==Guid.Empty||expected.Profile!=session.Profile||expected.Branch!=session.Branch||expected.Load!=session.Load||expected.ReleaseId!=session.ReleaseId)throw new StoreConflictException("终章保护档的档案世代已改变。");
        using var db=SqliteSupport.Open(_mainPath);using var tx=db.BeginTransaction(deferred:false);
        var snapshot=Snapshot(db,tx,session.Branch.Value.ToString());
        if(snapshot.Sequence!=worldSequence)throw new StoreConflictException("保存保护档期间世界状态已改变。");
        string id=Guid.NewGuid().ToString(),slot="protected:pre-final:"+preparationId,json=StrictJson.Serialize(snapshot);
        var authority=new ProtectedEndingCheckpoint(id,slot,session.Profile,session.Branch,session.Load,session.ReleaseId,snapshot.Sequence);
        SqliteSupport.Exec(db,tx,"INSERT INTO checkpoints VALUES($i,$b,$r,$v,$q,$j,$h); INSERT INTO save_slots VALUES($s,'protected',$i) ON CONFLICT(slot_id) DO UPDATE SET checkpoint_id=$i",("$i",id),("$b",session.Branch.Value.ToString()),("$r",snapshot.ReleaseId),("$v",snapshot.SchemaVersion),("$q",snapshot.Sequence),("$j",json),("$h",Hash(json)),("$s",slot));
        SqliteSupport.Exec(db,tx,"INSERT INTO settings VALUES($k,$j)",("$k","ending_checkpoint_authority:"+id),("$j",StrictJson.Serialize(authority)));
        _fault?.Invoke("prefinal_checkpoint_before_commit");tx.Commit();return authority;
    },cancellationToken);

    public ValueTask<ProtectedEndingCheckpoint?> FindAsync(string checkpointId,CancellationToken cancellationToken=default)=>_writer.Enqueue<ProtectedEndingCheckpoint?>(()=>
    {
        if(!Guid.TryParse(checkpointId,out _))return null;
        using var db=SqliteSupport.Open(_mainPath,readOnly:true);using var tx=db.BeginTransaction(deferred:true);
        string? json=SqliteSupport.Text(db,tx,"SELECT value_json FROM settings WHERE key=$k",("$k","ending_checkpoint_authority:"+checkpointId));
        if(json is null)return null;var authority=StrictJson.Deserialize<ProtectedEndingCheckpoint>(json);
        if(authority.Id!=checkpointId||authority.Profile!=Session.Profile||authority.Release!=Session.ReleaseId||!authority.Slot.StartsWith("protected:pre-final:",StringComparison.Ordinal)||!Guid.TryParse(authority.Slot["protected:pre-final:".Length..],out var preparation)||preparation==Guid.Empty)return null;
        using var command=SqliteSupport.Command(db,tx,"SELECT c.source_branch_id,c.release_id,c.snapshot_schema,c.sequence,c.snapshot_json,c.checksum FROM checkpoints c JOIN save_slots s ON s.checkpoint_id=c.checkpoint_id WHERE c.checkpoint_id=$i AND s.slot_id=$s AND s.category='protected'",("$i",checkpointId),("$s",authority.Slot));
        using var row=command.ExecuteReader();if(!row.Read())return null;
        string snapshotJson=row.GetString(4);
        if(row.GetString(0)!=authority.SourceBranch.Value.ToString()||row.GetString(1)!=authority.Release||row.GetInt64(3)!=authority.WorldSequence||Hash(snapshotJson)!=row.GetString(5))return null;
        var snapshot=StrictJson.Deserialize<Mist.Application.Storage.BranchSnapshot>(snapshotJson);
        if(snapshot.SchemaVersion!=row.GetInt64(2)||snapshot.ReleaseId!=authority.Release||snapshot.Sequence!=authority.WorldSequence)return null;
        return authority;
    },cancellationToken);
}
