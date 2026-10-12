using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Security.Cryptography;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int PersistLocalMachine = 2;
    private readonly string _fallbackDirectory;

    public WindowsCredentialStore(string? fallbackDirectory = null)
    {
        _fallbackDirectory = Path.GetFullPath(fallbackDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopyTools",
            "credentials"));
    }

    public void Save(string targetName, string userName, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        var bytes = Encoding.Unicode.GetBytes(secret);
        var pointer = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = pointer,
                Persist = PersistLocalMachine,
                UserName = userName,
            };
            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1312)
                {
                    SaveDpapiFallback(targetName, userName, secret);
                    return;
                }

                throw new InvalidOperationException($"保存 Windows 凭据失败：{error}。");
            }

            DeleteDpapiFallback(targetName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    public (string UserName, string Secret)? Read(string targetName)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var pointer))
        {
            return ReadDpapiFallback(targetName);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var secret = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2)
                    ?? string.Empty;
            return (credential.UserName ?? string.Empty, secret);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(string targetName)
    {
        if (!CredDelete(targetName, CredentialTypeGeneric, 0))
        {
            const int NotFound = 1168;
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound)
            {
                throw new InvalidOperationException($"删除 Windows 凭据失败：{error}。");
            }
        }

        DeleteDpapiFallback(targetName);
    }

    private void SaveDpapiFallback(string targetName, string userName, string secret)
    {
        Directory.CreateDirectory(_fallbackDirectory);
        var clear = Encoding.UTF8.GetBytes($"{userName}\n{secret}");
        try
        {
            var encrypted = Protect(clear);
            var path = GetFallbackPath(targetName);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, true);
            CryptographicOperations.ZeroMemory(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private (string UserName, string Secret)? ReadDpapiFallback(string targetName)
    {
        var path = GetFallbackPath(targetName);
        if (!File.Exists(path))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(path);
        var clear = Unprotect(encrypted);
        try
        {
            var value = Encoding.UTF8.GetString(clear);
            var separator = value.IndexOf('\n');
            if (separator < 0)
            {
                throw new InvalidDataException("加密凭据格式无效。");
            }

            return (value[..separator], value[(separator + 1)..]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private void DeleteDpapiFallback(string targetName)
    {
        var path = GetFallbackPath(targetName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetFallbackPath(string targetName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(targetName));
        return Path.Combine(_fallbackDirectory, $"{Convert.ToHexString(hash).ToLowerInvariant()}.cred");
    }

    private static byte[] Protect(byte[] clear) => TransformDpapi(clear, protect: true);

    private static byte[] Unprotect(byte[] encrypted) => TransformDpapi(encrypted, protect: false);

    private static byte[] TransformDpapi(byte[] input, bool protect)
    {
        var inputPointer = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var inputBlob = new DataBlob { DataLength = input.Length, Data = inputPointer };
            var success = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob);
            if (!success)
            {
                throw new InvalidOperationException($"Windows DPAPI 操作失败：{Marshal.GetLastWin32Error()}。");
            }

            try
            {
                var output = new byte[outputBlob.DataLength];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            for (var index = 0; index < input.Length; index++)
            {
                Marshal.WriteByte(inputPointer, index, 0);
            }

            Marshal.FreeHGlobal(inputPointer);
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataLength;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

}
