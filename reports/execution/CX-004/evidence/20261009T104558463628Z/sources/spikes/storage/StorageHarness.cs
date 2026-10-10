using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

public static class StorageHarness
{
    public static SqliteConnection Open(string path, bool existingOnly = false, int timeout = 5)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = existingOnly ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = timeout, Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            Execute(connection, null, "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static long Number(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public static Dictionary<string, object> Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "two_tables.sqlite");
        if (File.Exists(path)) throw new InvalidOperationException("Use a fresh evidence directory; existing databases are never reset.");
        var checks = new Dictionary<string, bool>();
        using var connection = Open(path);
        Execute(connection, null, "CREATE TABLE balances(id TEXT PRIMARY KEY, coins INTEGER NOT NULL CHECK(coins>=0)); CREATE TABLE receipts(id TEXT PRIMARY KEY, delta INTEGER NOT NULL); INSERT INTO balances VALUES('player',100);");
        try
        {
            using var tx = connection.BeginTransaction(deferred: false);
            Execute(connection, tx, "UPDATE balances SET coins=60 WHERE id='player';");
            throw new InvalidOperationException("injected failure between the two writes");
        }
        catch (InvalidOperationException error) when (error.Message.StartsWith("injected failure")) { }
        checks["injected_failure_rolls_back_both_tables"] = Number(connection, "SELECT coins FROM balances") == 100 && Number(connection, "SELECT count(*) FROM receipts") == 0;
        using (var tx = connection.BeginTransaction(deferred: false))
        {
            Execute(connection, tx, "UPDATE balances SET coins=60 WHERE id='player'; INSERT INTO receipts VALUES('commit-1',-40);");
            tx.Commit();
        }
        checks["two_tables_commit_together"] = Number(connection, "SELECT coins FROM balances") == 60 && Number(connection, "SELECT count(*) FROM receipts") == 1;
        try
        {
            using var tx = connection.BeginTransaction(deferred: false);
            Execute(connection, tx, "UPDATE balances SET coins=20 WHERE id='player'; INSERT INTO receipts VALUES('commit-1',-40);");
            tx.Commit();
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 19) { }
        checks["constraint_failure_rolls_back_first_table"] = Number(connection, "SELECT coins FROM balances") == 60 && Number(connection, "SELECT count(*) FROM receipts") == 1;
        string busyStatus = "not_observed";
        long busyMilliseconds;
        using (var contender = Open(path, true, timeout: 1))
        using (var owner = connection.BeginTransaction(deferred: false))
        {
            var watch = Stopwatch.StartNew();
            try { using var blocked = contender.BeginTransaction(deferred: false); }
            catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6) { busyStatus = "database_busy"; }
            watch.Stop();
            busyMilliseconds = watch.ElapsedMilliseconds;
        }
        checks["database_busy_has_explicit_state"] = busyStatus == "database_busy" && busyMilliseconds < 10000;
        var backupPath = Path.Combine(directory, "consistent_backup.sqlite");
        using (var backup = Open(backupPath)) connection.BackupDatabase(backup);
        using (var backup = Open(backupPath, true))
        {
            checks["backup_contains_committed_two_table_state"] = Number(backup, "SELECT coins FROM balances") == 60 && Number(backup, "SELECT count(*) FROM receipts") == 1;
            using var integrity = backup.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            checks["backup_integrity"] = Convert.ToString(integrity.ExecuteScalar()) == "ok";
        }
        using var version = connection.CreateCommand();
        version.CommandText = "SELECT sqlite_version();";
        var native = Path.Combine(AppContext.BaseDirectory, "e_sqlite3.dll");
        if (!File.Exists(native))
        {
            var rid = Path.Combine(AppContext.BaseDirectory, "runtimes/win-x64/native/e_sqlite3.dll");
            if (File.Exists(rid)) native = rid;
        }
        if (!File.Exists(native)) throw new FileNotFoundException("Native SQLite loaded but its deployment path could not be located for evidence.", native);
        var report = new Dictionary<string, object>
        {
            ["checks"] = checks, ["sqlite_version"] = Convert.ToString(version.ExecuteScalar())!,
            ["framework"] = RuntimeInformation.FrameworkDescription, ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["native_path"] = native, ["native_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))).ToLowerInvariant(),
            ["database"] = path, ["backup"] = backupPath, ["busy_status"] = busyStatus, ["busy_elapsed_ms"] = busyMilliseconds,
            ["busy_test_timeout_seconds"] = 1, ["normal_busy_timeout_ms"] = 5000
        };
        foreach (var check in checks) if (!check.Value) throw new InvalidOperationException("Failed check: " + check.Key);
        return report;
    }
}
