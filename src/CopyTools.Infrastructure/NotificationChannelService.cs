using System.Runtime.Versioning;
using CopyTools.Application;
using CopyTools.Domain;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class NotificationChannelService(
    ICopyToolsRepository repository,
    WindowsCredentialStore credentials,
    TaskNotificationService notifications)
{
    public Task<IReadOnlyList<NotificationChannel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        repository.GetNotificationChannelsAsync(cancellationToken);

    public async Task SaveAsync(NotificationChannel channel, string? secret, CancellationToken cancellationToken = default)
    {
        Validate(channel);
        if (!string.IsNullOrEmpty(secret)) credentials.Save(channel.CredentialTarget, channel.UserName, secret);
        await repository.SaveNotificationChannelAsync(channel with { UpdatedAtUtc = DateTime.UtcNow }, cancellationToken);
    }

    public async Task DeleteAsync(NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        await repository.DeleteNotificationChannelAsync(channel.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(channel.CredentialTarget)) credentials.Delete(channel.CredentialTarget);
    }

    public Task TestAsync(NotificationChannel channel, CancellationToken cancellationToken = default) =>
        notifications.SendChannelAsync(channel, new TaskResultNotification(
            Guid.Empty, "CopyTools 通知测试", "成功", DateTime.UtcNow, 1, 0, 0,
            "这是一条手动发送的测试消息。"), cancellationToken);

    private static void Validate(NotificationChannel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Name) || string.IsNullOrWhiteSpace(channel.Endpoint))
            throw new ArgumentException("通知频道名称和端点不能为空。");
        if (channel.Kind == NotificationChannelKind.Smtp
            && (string.IsNullOrWhiteSpace(channel.Sender) || string.IsNullOrWhiteSpace(channel.Recipients)))
            throw new ArgumentException("邮件通知必须填写发件人和至少一个收件人。");
    }
}
