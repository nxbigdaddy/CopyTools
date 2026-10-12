using System.Runtime.InteropServices;

namespace CopyTools.Infrastructure;

internal sealed class SqliteDatabase : IDisposable
{
    private IntPtr _handle;

    public SqliteDatabase(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var result = NativeSqlite.Open(
            path,
            out _handle,
            NativeSqlite.OpenReadWrite | NativeSqlite.OpenCreate | NativeSqlite.OpenFullMutex,
            IntPtr.Zero);
        if (result != NativeSqlite.Ok)
        {
            var message = GetErrorMessage();
            Dispose();
            throw new InvalidOperationException($"无法打开 SQLite 数据库：{message}");
        }
    }

    public SqliteStatement Prepare(string sql)
    {
        var result = NativeSqlite.Prepare(_handle, sql, -1, out var statement, IntPtr.Zero);
        if (result != NativeSqlite.Ok)
        {
            throw new InvalidOperationException($"无法准备 SQLite 语句：{GetErrorMessage()}");
        }

        return new SqliteStatement(this, statement);
    }

    public void Execute(string sql)
    {
        using var statement = Prepare(sql);
        statement.Execute();
    }

    public string GetErrorMessage()
    {
        if (_handle == IntPtr.Zero)
        {
            return "数据库句柄不可用";
        }

        return Marshal.PtrToStringUTF8(NativeSqlite.ErrorMessage(_handle)) ?? "未知 SQLite 错误";
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeSqlite.Close(_handle);
        _handle = IntPtr.Zero;
    }
}
