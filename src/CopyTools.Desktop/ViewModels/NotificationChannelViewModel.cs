using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed class NotificationChannelViewModel(NotificationChannel channel)
{
    public NotificationChannel Channel { get; } = channel;
    public string Name => Channel.Name;
    public string Kind => Channel.Kind == NotificationChannelKind.Webhook ? "Webhook" : "电子邮件";
    public string Enabled => Channel.Enabled ? "启用" : "停用";
    public string Endpoint => Channel.Endpoint;
    public string Recipients => Channel.Kind == NotificationChannelKind.Smtp ? Channel.Recipients : "—";
    public string Events => $"成功：{(Channel.NotifyOnSuccess ? "是" : "否")}；失败：{(Channel.NotifyOnFailure ? "是" : "否")}";
}
