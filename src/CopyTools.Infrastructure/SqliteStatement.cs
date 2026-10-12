using System.Runtime.InteropServices;

namespace CopyTools.Infrastructure;

internal sealed class SqliteStatement : IDisposable
{
    private readonly SqliteDatabase _database;
    private IntPtr _handle;

    public SqliteStatement(SqliteDatabase database, IntPtr handle)
    {
        _database = database;
        _handle = handle;
    }

    public void BindText(int index, string? value)
    {
        var result = value is null
            ? NativeSqlite.BindNull(_handle, index)
            : NativeSqlite.BindText(_handle, index, value, -1, NativeSqlite.Transient);
        EnsureSuccess(result, "绑定文本参数");
    }

    public void BindInt(int index, int value) =>
        EnsureSuccess(NativeSqlite.BindInt(_handle, index, value), "绑定整数参数");

    public void BindInt64(int index, long value) =>
        EnsureSuccess(NativeSqlite.BindInt64(_handle, index, value), "绑定长整数参数");

    public bool Read()
    {
        var result = NativeSqlite.Step(_handle);
        if (result == NativeSqlite.Row)
        {
            return true;
        }

        if (result == NativeSqlite.Done)
        {
            return false;
        }

        throw new InvalidOperationException($"读取 SQLite 数据失败：{_database.GetErrorMessage()}");
    }

    public void Execute()
    {
        while (true)
        {
            var result = NativeSqlite.Step(_handle);
            if (result == NativeSqlite.Done)
            {
                return;
            }

            if (result != NativeSqlite.Row)
            {
                throw new InvalidOperationException($"执行 SQLite 语句失败：{_database.GetErrorMessage()}");
            }
        }
    }

    public string GetString(int column)
    {
        var pointer = NativeSqlite.ColumnText(_handle, column);
        return pointer == IntPtr.Zero
            ? string.Empty
            : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }

    public string? GetNullableString(int column) =>
        NativeSqlite.ColumnType(_handle, column) == 5 ? null : GetString(column);

    public int GetInt(int column) => NativeSqlite.ColumnInt(_handle, column);

    public long GetInt64(int column) => NativeSqlite.ColumnInt64(_handle, column);

    public long? GetNullableInt64(int column) =>
        NativeSqlite.ColumnType(_handle, column) == 5 ? null : GetInt64(column);

    public void BindNullableInt64(int index, long? value)
    {
        if (value is null)
        {
            EnsureSuccess(NativeSqlite.BindNull(_handle, index), "绑定空长整数参数");
            return;
        }

        BindInt64(index, value.Value);
    }

    public void Reset()
    {
        EnsureSuccess(NativeSqlite.Reset(_handle), "重置语句");
        EnsureSuccess(NativeSqlite.ClearBindings(_handle), "清除语句参数");
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeSqlite.Finalize(_handle);
        _handle = IntPtr.Zero;
    }

    private void EnsureSuccess(int result, string operation)
    {
        if (result != NativeSqlite.Ok)
        {
            throw new InvalidOperationException($"SQLite {operation}失败：{_database.GetErrorMessage()}");
        }
    }
}
