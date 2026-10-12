namespace CopyTools.Application;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record AppNotification(
    Guid Id,
    string Title,
    string Message,
    NotificationSeverity Severity,
    DateTime OccurredAtUtc,
    bool IsRead = false);

/// <summary>应用内通知中心：环形缓冲 + 已读状态，支撑集中式事件流。</summary>
public sealed class NotificationCenter
{
    private readonly List<AppNotification> _notifications = [];
    private readonly object _sync = new();
    private readonly int _capacity;

    public NotificationCenter(int capacity = 500)
    {
        _capacity = Math.Max(1, capacity);
    }

    public AppNotification Push(
        string title,
        string message,
        NotificationSeverity severity = NotificationSeverity.Info)
    {
        var notification = new AppNotification(Guid.NewGuid(), title, message, severity, DateTime.UtcNow);
        lock (_sync)
        {
            _notifications.Add(notification);
            if (_notifications.Count > _capacity)
            {
                _notifications.RemoveAt(0);
            }
        }

        return notification;
    }

    public IReadOnlyList<AppNotification> GetNotifications(bool unreadOnly = false)
    {
        lock (_sync)
        {
            var query = unreadOnly
                ? _notifications.Where(notification => !notification.IsRead)
                : _notifications.AsEnumerable();
            return query.OrderByDescending(notification => notification.OccurredAtUtc).ToArray();
        }
    }

    public void MarkAllRead()
    {
        lock (_sync)
        {
            for (var index = 0; index < _notifications.Count; index++)
            {
                _notifications[index] = _notifications[index] with { IsRead = true };
            }
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_sync)
            {
                return _notifications.Count(notification => !notification.IsRead);
            }
        }
    }
}
