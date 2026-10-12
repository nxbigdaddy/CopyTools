using System.Runtime.Versioning;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class VssBackupService(
    WindowsVssSnapshotService? snapshots = null,
    BackupRepositoryService? backups = null)
{
    private readonly WindowsVssSnapshotService _snapshots = snapshots ?? new WindowsVssSnapshotService();
    private readonly BackupRepositoryService _backups = backups ?? new BackupRepositoryService();

    public async Task<BackupResult> CreateBackupAsync(
        BackupTaskRequest request,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var snapshot = await _snapshots.CreateAsync(request.SourceDirectory, cancellationToken);
        return await _backups.CreateBackupAsync(
            request with
            {
                SourceDirectory = snapshot.SnapshotPath,
                ManifestSourceDirectory = Path.GetFullPath(request.SourceDirectory),
            },
            progress,
            cancellationToken);
    }
}
