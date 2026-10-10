using Mist.Core.Contracts;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore
{
    public ValueTask EnsureInventorySchemaAsync()=>new(_writer.Enqueue(() =>
    {
        using(var current=SqliteSupport.Open(_mainPath,readOnly:true))
        {
            long version=SqliteSupport.Number(current,null,"PRAGMA user_version");if(version==2)return true;if(version!=1)throw new Mist.Application.Storage.StoreVersionException("Inventory migration requires a recognized schema-1 source.");
        }
        string original=_mainPath,originalHash=FileHash(original);Directory.CreateDirectory(ManagedPath("recovery"));string relative="recovery/inventory-v2-"+Guid.NewGuid()+".sqlite",candidate=ManagedPath(relative);
        using var control=SqliteSupport.Open(ControlPath);string expectedPointer=SqliteSupport.Text(control,null,"SELECT current_path FROM recovery_pointers WHERE profile_id=$i",("$i",Session.Profile.Value.ToString()))??throw new InvalidDataException("Missing active pointer.");SqliteSupport.Exec(control,null,"UPDATE recovery_pointers SET pending_candidate=$p WHERE profile_id=$i",("$p",relative),("$i",Session.Profile.Value.ToString()));
        using(var source=SqliteSupport.Open(original,readOnly:true))using(var target=SqliteSupport.Open(candidate))source.BackupDatabase(target);
        using(var db=SqliteSupport.Open(candidate))
        {
            using(var tx=db.BeginTransaction(deferred:false)){SqliteSupport.Exec(db,tx,Schema("002_inventory.sql"));_fault?.Invoke("inventory_migration_before_commit");tx.Commit();}
            ValidateDatabase(db,Session.ReleaseId,Session.Profile.Value.ToString());ApplyErasureJobs(control,db);
            if(originalHash!=FileHash(original))throw new InvalidDataException("Original profile changed during candidate migration.");
            _fault?.Invoke("inventory_migration_before_pointer");using(var promote=SqliteSupport.Command(control,null,"UPDATE recovery_pointers SET current_path=$p,pending_candidate=NULL WHERE profile_id=$i AND current_path=$old",("$p",relative),("$i",Session.Profile.Value.ToString()),("$old",expectedPointer)))if(promote.ExecuteNonQuery()!=1)throw new Mist.Application.Storage.StoreConflictException("Migration pointer changed in another process; candidate not promoted.");
            _mainPath=candidate;_session=ReadSession(db,Session.Profile,Session.Branch,new LoadContext(Guid.NewGuid()));
        }
        return true;
    }).AsTask());
}
