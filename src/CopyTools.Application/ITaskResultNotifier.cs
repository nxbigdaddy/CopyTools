using CopyTools.Domain;

namespace CopyTools.Application;

public interface ITaskResultNotifier
{
    Task<IReadOnlyList<string>> SendAsync(
        TaskResultNotification notification,
        CancellationToken cancellationToken = default);
}
