using System.Runtime.InteropServices;

namespace CopyTools.Infrastructure;

internal static partial class NativeSqlite
{
    internal const int Ok = 0;
    internal const int Row = 100;
    internal const int Done = 101;
    internal const int OpenReadWrite = 0x00000002;
    internal const int OpenCreate = 0x00000004;
    internal const int OpenFullMutex = 0x00010000;

    internal static readonly IntPtr Transient = new(-1);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Open(
        string filename,
        out IntPtr database,
        int flags,
        IntPtr virtualFileSystem);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_close_v2")]
    internal static partial int Close(IntPtr database);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Prepare(
        IntPtr database,
        string sql,
        int sqlBytes,
        out IntPtr statement,
        IntPtr tail);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_step")]
    internal static partial int Step(IntPtr statement);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize")]
    internal static partial int Finalize(IntPtr statement);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_reset")]
    internal static partial int Reset(IntPtr statement);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_clear_bindings")]
    internal static partial int ClearBindings(IntPtr statement);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_text", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int BindText(
        IntPtr statement,
        int index,
        string value,
        int valueBytes,
        IntPtr destructor);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_null")]
    internal static partial int BindNull(IntPtr statement, int index);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_int")]
    internal static partial int BindInt(IntPtr statement, int index, int value);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_int64")]
    internal static partial int BindInt64(IntPtr statement, int index, long value);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text")]
    internal static partial IntPtr ColumnText(IntPtr statement, int column);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int")]
    internal static partial int ColumnInt(IntPtr statement, int column);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64")]
    internal static partial long ColumnInt64(IntPtr statement, int column);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_column_type")]
    internal static partial int ColumnType(IntPtr statement, int column);

    [LibraryImport("winsqlite3.dll", EntryPoint = "sqlite3_errmsg")]
    internal static partial IntPtr ErrorMessage(IntPtr database);
}
