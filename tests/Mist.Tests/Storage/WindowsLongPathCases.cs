using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Mist.Application.Contracts;
using Mist.Core.Contracts;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Storage;

public static class WindowsLongPathCases
{
    private const string Release="mistsea-internal-0.1";
    private static string DeepRoot(string name)
    {
        string path=Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"long-sqlite-"+name+"-"+Guid.NewGuid().ToString("N"));
        while(path.Length<285)path=Path.Combine(path,"Unicode资料 and rollback journal");
        return Path.GetFullPath(path);
    }
    private static string FileHash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static SqliteConnection Read(string path)
    {string full=Path.GetFullPath(path);string native=full.StartsWith(@"\\?\",StringComparison.Ordinal)?full:full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=native,Mode=SqliteOpenMode.ReadOnly,Pooling=false,Vfs="win32-longpath"}.ToString());db.Open();return db;}
    private static long Number(SqliteConnection db,string sql)
    {using var command=db.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
    public static void Register()
    {
        if(!OperatingSystem.IsWindows())return;
        Add("CX-076","Windows deep Unicode profile migrates backups restores and reopens with rollback journals beyond MAX_PATH",()=>
        {
            string root=DeepRoot("roundtrip");string active;ProfileId profile;
            using(var store=new ProfileStore(root,Release))
            {
                Require(store.MainPath.Length>260&&store.RootPath==root);
                Require(Done(store.ExecuteAsync(Plan(store,"storage-long-path:original",0,"proof"))).Status==CommitStatus.Committed);
                string original=store.MainPath,hash=FileHash(original);profile=store.Session.Profile;
                Done(store.EnsureInventorySchemaAsync());
                Require(store.MainPath!=original&&FileHash(original)==hash&&Done(store.SnapshotAsync()).SchemaVersion==2);
                var backup=Done(store.BackupAsync());Require(backup.Path.Length>260&&new FileInfo(backup.Path).Length>0);
                Require(Done(store.ExecuteAsync(Plan(store,"storage-long-path:later",0,"proof",0))).Status==CommitStatus.Committed);
                Require(Done(store.SnapshotAsync()).Sequence==2);
                Done(store.RestoreBackupAsync(backup.Id));active=store.MainPath;
                var restored=Done(store.SnapshotAsync());var diagnostics=Done(store.DiagnosticsAsync());
                Require(active.Length>260&&restored.SchemaVersion==2&&restored.Sequence==1&&restored.Entities.Single(e=>e.Id=="proof").Revision==0&&FileHash(original)==hash);
                Require(diagnostics.JournalMode=="delete"&&diagnostics.Synchronous==2&&diagnostics.ForeignKeys==1);
                Require(restored.Balances.GetValueOrDefault("marks")==0);
            }
            using var reopened=new ProfileStore(root,Release);
            Require(reopened.MainPath==active&&reopened.Session.Profile==profile&&Done(reopened.SnapshotAsync()).Sequence==1);
        });
        Add("CX-076","Windows deep migration interruption preserves original bytes and active pointer with valid unpromoted candidate",()=>
        {
            string root=DeepRoot("interrupted");string original,hash;
            using(var store=new ProfileStore(root,Release,phase=>{if(phase=="inventory_migration_before_pointer")throw new IOException("injected pointer interruption");}))
            {
                original=store.MainPath;hash=FileHash(original);
                Throws<IOException>(()=>Done(store.EnsureInventorySchemaAsync()));
                Require(store.MainPath==original&&FileHash(original)==hash&&Done(store.SnapshotAsync()).SchemaVersion==1);
                string candidate=Directory.EnumerateFiles(Path.Combine(root,"recovery"),"inventory-v2-*.sqlite").Single();
                Require(candidate.Length>260&&new FileInfo(candidate).Length>0);
                using var db=Read(candidate);Require(Number(db,"PRAGMA user_version")==2);
            }
            using var reopened=new ProfileStore(root,Release);
            Require(reopened.MainPath==original&&FileHash(original)==hash&&Done(reopened.SnapshotAsync()).SchemaVersion==1);
            Done(reopened.EnsureInventorySchemaAsync());Require(reopened.MainPath!=original&&Done(reopened.SnapshotAsync()).SchemaVersion==2&&FileHash(original)==hash);
        });
    }
}
