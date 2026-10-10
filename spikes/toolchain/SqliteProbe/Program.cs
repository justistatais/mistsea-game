using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

if (args.Length != 1) throw new ArgumentException("Pass the evidence output directory.");
var directory = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var database = Path.Combine(directory, "probe.sqlite");
var backupFile = Path.Combine(directory, "backup.sqlite");
using var connection = new SqliteConnection($"Data Source={database}");
connection.Open();

void Execute(string sql, SqliteTransaction? transaction = null)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Transaction = transaction;
    command.ExecuteNonQuery();
}

object? Scalar(string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar();
}

Execute("PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;");
Execute("CREATE TABLE sample(value INTEGER NOT NULL); INSERT INTO sample VALUES(121);");
using (var transaction = connection.BeginTransaction())
{
    Execute("INSERT INTO sample VALUES(41);", transaction);
    transaction.Rollback();
}
if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sample;")) != 1)
    throw new InvalidOperationException("Rollback did not restore the pre-transaction state.");
using (var transaction = connection.BeginTransaction())
{
    Execute("INSERT INTO sample VALUES(42);", transaction);
    transaction.Commit();
}
if (Convert.ToInt64(Scalar("SELECT SUM(value) FROM sample;")) != 163)
    throw new InvalidOperationException("Committed values did not persist.");
using (var backup = new SqliteConnection($"Data Source={backupFile}"))
{
    backup.Open();
    connection.BackupDatabase(backup);
    using var query = backup.CreateCommand();
    query.CommandText = "SELECT SUM(value) FROM sample;";
    if (Convert.ToInt64(query.ExecuteScalar()) != 163)
        throw new InvalidOperationException("SQLite BackupDatabase did not preserve the committed state.");
}
if (Convert.ToString(Scalar("PRAGMA integrity_check;")) != "ok")
    throw new InvalidOperationException("SQLite integrity_check failed.");
var nativeFiles = Directory.GetFiles(AppContext.BaseDirectory, "e_sqlite3.dll", SearchOption.AllDirectories);
if (nativeFiles.Length == 0) throw new FileNotFoundException("Native e_sqlite3.dll was not deployed.");
var native = nativeFiles.Select(path => new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() }).ToArray();
var result = new
{
    scope = "CX-002 native toolchain probe, not production persistence validation",
    framework = RuntimeInformation.FrameworkDescription,
    process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    sqlite_version = Convert.ToString(Scalar("SELECT sqlite_version();")),
    provider_assembly = typeof(SqliteConnection).Assembly.FullName,
    journal_mode = Convert.ToString(Scalar("PRAGMA journal_mode;")),
    synchronous = Convert.ToInt64(Scalar("PRAGMA synchronous;")),
    rollback_verified = true,
    commit_verified = true,
    backup_verified = true,
    integrity_check = "ok",
    native_files = native,
    database,
    backup = backupFile
};
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(directory, "result.json"), json);
Console.WriteLine(json);
