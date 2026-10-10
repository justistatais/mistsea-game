using Microsoft.Data.Sqlite;
using Mist.Application.Storage;

namespace Mist.Infrastructure.Storage;

internal static class SqliteSupport
{
    private static string NativeFilePath(string path)
    {
        if(!OperatingSystem.IsWindows())return path;
        string full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\?\",StringComparison.Ordinal))return full;
        return full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;
    }
    public static SqliteConnection Open(string path, bool readOnly = false, int timeoutSeconds = 5)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = NativeFilePath(path), Mode = readOnly ? SqliteOpenMode.ReadOnly : File.Exists(path) ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = timeoutSeconds, Pooling = false,
            // The native Win32 call needs the extended prefix even with the long
            // VFS; profile pointers and managed paths keep their logical names.
            Vfs = OperatingSystem.IsWindows() ? "win32-longpath" : ""
        }.ToString());
        try
        {
            connection.Open();
            if (!readOnly)
            {
                long version = Number(connection, null, "PRAGMA user_version;");
                if (version > 2) throw new StoreVersionException("Database schema is newer than this build; write access denied.");
                Exec(connection, null, "PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    public static void Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(c, tx, sql, values);
        command.ExecuteNonQuery();
    }
    public static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values)
    {
        var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var v in values) command.Parameters.AddWithValue(v.Name, v.Value ?? DBNull.Value);
        return command;
    }
    public static long Number(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(c, tx, sql, values);
        return Convert.ToInt64(command.ExecuteScalar());
    }
    public static string? Text(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(c, tx, sql, values);
        return command.ExecuteScalar() is string text ? text : null;
    }
}
