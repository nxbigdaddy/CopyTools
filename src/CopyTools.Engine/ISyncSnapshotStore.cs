using CopyTools.Domain;

namespace CopyTools.Engine;

public interface ISyncSnapshotStore
{
    Task<IReadOnlyList<SyncFileState>> GetSnapshotAsync(
        Guid synchronizationId,
        CancellationToken cancellationToken = default);

    Task ReplaceSnapshotAsync(
        Guid synchronizationId,
        IReadOnlyList<SyncFileState> states,
        CancellationToken cancellationToken = default);
}
