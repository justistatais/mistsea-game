using Microsoft.Data.Sqlite;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Infrastructure.Storage;
using static Mist.Tests.TestRegistry;
using static Mist.Tests.Storage.StorageCases;

namespace Mist.Tests.Storage;

public static class EndingCheckpointCases
{
    private static string Root()=>Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"prefinal-storage-"+Guid.NewGuid());
    public static void Register()
    {
        Add("CX-074","Production prefinal checkpoint stores checked provenance atomically and survives actual profile reopen",()=>
        {
            string root=Root();Mist.Core.ReleaseReadiness.ProtectedEndingCheckpoint checkpoint;
            using(var store=new ProfileStore(root,"mistsea-internal-0.1"))
            {
                var before=Done(store.SnapshotAsync());checkpoint=Done(store.SavePreFinalAsync(store.Session,before.Sequence,Guid.NewGuid()));
                Require(Done(store.FindAsync(checkpoint.Id))==checkpoint&&Done(store.SnapshotAsync()).Sequence==before.Sequence&&Done(store.SlotsAsync())[checkpoint.Slot]==checkpoint.Id);
            }
            using var reopened=new ProfileStore(root,"mistsea-internal-0.1");Require(Done(reopened.FindAsync(checkpoint.Id))==checkpoint&&checkpoint.SourceLoad!=reopened.Session.Load);
        });
        Add("CX-074","Stale load or world sequence cannot produce a prefinal checkpoint and storage failure leaves no partial authority",()=>
        {
            using var store=new ProfileStore(Root(),"mistsea-internal-0.1",point=>{if(point=="prefinal_checkpoint_before_commit")throw new IOException("isolated checkpoint commit failure");});
            var session=store.Session;long sequence=Done(store.SnapshotAsync()).Sequence;
            Throws<StoreConflictException>(()=>Done(store.SavePreFinalAsync(session,sequence+1,Guid.NewGuid())));
            Done(store.ContinueAsync());Throws<StoreConflictException>(()=>Done(store.SavePreFinalAsync(session,sequence,Guid.NewGuid())));
            Throws<IOException>(()=>Done(store.SavePreFinalAsync(store.Session,sequence,Guid.NewGuid())));
            Require(Done(store.SlotsAsync()).Count==0);
            using var db=new SqliteConnection("Data Source="+store.MainPath);db.Open();using var command=db.CreateCommand();command.CommandText="SELECT COUNT(*) FROM settings WHERE key LIKE 'ending_checkpoint_authority:%'";Require(Convert.ToInt64(command.ExecuteScalar())==0);
        });
        Add("CX-074","A modified actual protected snapshot cannot be reported as an available final checkpoint",()=>
        {
            using var store=new ProfileStore(Root(),"mistsea-internal-0.1");var checkpoint=Done(store.SavePreFinalAsync(store.Session,Done(store.SnapshotAsync()).Sequence,Guid.NewGuid()));
            using(var db=new SqliteConnection("Data Source="+store.MainPath)){db.Open();using var command=db.CreateCommand();command.CommandText="UPDATE checkpoints SET checksum='invalid-isolated-test-hash' WHERE checkpoint_id=$id";command.Parameters.AddWithValue("$id",checkpoint.Id);Require(command.ExecuteNonQuery()==1);}
            Require(Done(store.FindAsync(checkpoint.Id)) is null&&Done(store.SlotsAsync())[checkpoint.Slot]==checkpoint.Id);
        });
    }
}
