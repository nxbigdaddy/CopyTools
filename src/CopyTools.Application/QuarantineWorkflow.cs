using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Application;

public sealed class QuarantineWorkflow
{
    private readonly ICopyToolsRepository _repository;
    private readonly QuarantineService _quarantineService;

    public QuarantineWorkflow(
        ICopyToolsRepository repository,
        QuarantineService? quarantineService = null)
    {
        _repository = repository;
        _quarantineService = quarantineService ?? new QuarantineService();
    }

    public Task<IReadOnlyList<QuarantineEntry>> GetEntriesAsync(
        bool includeRestored = false,
        CancellationToken cancellationToken = default) =>
        _repository.GetQuarantineEntriesAsync(includeRestored, cancellationToken);

    public async Task RestoreAsync(
        QuarantineEntry entry,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () => _quarantineService.Restore(entry, overwrite),
            cancellationToken);
        await _repository.MarkQuarantineRestoredAsync(entry.Id, DateTime.UtcNow, cancellationToken);
    }
}
