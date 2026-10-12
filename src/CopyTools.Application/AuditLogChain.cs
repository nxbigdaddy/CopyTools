using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CopyTools.Application;

/// <summary>追加式哈希链审计日志：每条记录引用前一条的哈希，篡改历史会使链条校验失败。</summary>
public sealed class AuditLogChain
{
    public sealed record AuditEntry(
        Guid Id,
        DateTime TimestampUtc,
        string Event,
        string Detail,
        string PreviousHash);

    private const string Genesis = "GENESIS";
    private readonly string _path;

    public AuditLogChain(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task AppendAsync(string @event, string detail, CancellationToken cancellationToken = default)
    {
        var entries = await ReadAllAsync(cancellationToken);
        var previousHash = entries.Count == 0 ? Genesis : ComputeHash(entries[^1]);
        var entry = new AuditEntry(Guid.NewGuid(), DateTime.UtcNow, @event, detail, previousHash);
        await File.AppendAllTextAsync(
            _path,
            JsonSerializer.Serialize(entry) + Environment.NewLine,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var result = new List<AuditEntry>();
        foreach (var line in await File.ReadAllLinesAsync(_path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<AuditEntry>(line);
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    public async Task<bool> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ReadAllAsync(cancellationToken);
        for (var index = 0; index < entries.Count; index++)
        {
            var expected = index == 0 ? Genesis : ComputeHash(entries[index - 1]);
            if (!string.Equals(entries[index].PreviousHash, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string ComputeHash(AuditEntry entry)
    {
        var payload = $"{entry.Id:D}|{entry.TimestampUtc:O}|{entry.Event}|{entry.Detail}|{entry.PreviousHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
