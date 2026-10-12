namespace CopyTools.Domain;

public sealed record StorageConnectionProfile(
    Guid Id,
    string Name,
    StorageConnectionKind Kind,
    string Endpoint,
    string RootPath,
    string UserName,
    string CredentialTarget,
    string Region,
    string Bucket,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static StorageConnectionProfile Create(
        string name,
        StorageConnectionKind kind,
        string endpoint,
        string rootPath = "",
        string userName = "",
        string region = "",
        string bucket = "")
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        return new StorageConnectionProfile(
            id, name.Trim(), kind, endpoint.Trim(), rootPath.Trim(), userName.Trim(),
            $"CopyTools/Storage/{id:D}", region.Trim(), bucket.Trim(), now, now);
    }
}
