using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CopyTools.Application;
using CopyTools.Domain;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class TaskNotificationService(
    ICopyToolsRepository repository,
    WindowsCredentialStore credentialStore,
    HttpClient? httpClient = null) : ITaskResultNotifier
{
    private readonly HttpClient _client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<string>> SendAsync(
        TaskResultNotification notification,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var channels = await repository.GetNotificationChannelsAsync(cancellationToken);
        foreach (var channel in channels.Where(channel => channel.Enabled
                     && (notification.Status == "成功" ? channel.NotifyOnSuccess : channel.NotifyOnFailure)))
        {
            try
            {
                await SendWithRetryAsync(channel, notification, cancellationToken);
            }
            catch (Exception exception) when (exception is
                       HttpRequestException or SmtpException or IOException or InvalidOperationException)
            {
                errors.Add($"{channel.Name}：{Sanitize(exception.Message)}");
            }
        }

        return errors;
    }

    public Task SendChannelAsync(
        NotificationChannel channel,
        TaskResultNotification notification,
        CancellationToken cancellationToken = default) =>
        SendWithRetryAsync(channel, notification, cancellationToken);

    private async Task SendWithRetryAsync(
        NotificationChannel channel,
        TaskResultNotification notification,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (channel.Kind == NotificationChannelKind.Webhook)
                {
                    await SendWebhookAsync(channel, notification, cancellationToken);
                }
                else if (channel.Kind == NotificationChannelKind.Smtp)
                {
                    await SendEmailAsync(channel, notification, cancellationToken);
                }
                else
                {
                    await SendImAsync(channel, notification, cancellationToken);
                }
                return;
            }
            catch (Exception exception) when (exception is
                       HttpRequestException or SmtpException or IOException or InvalidOperationException)
            {
                last = exception;
                if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(attempt * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException(last?.Message ?? "通知发送失败。");
    }

    private async Task SendWebhookAsync(
        NotificationChannel channel,
        TaskResultNotification notification,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(channel.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("Webhook 端点无效。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(notification),
        };
        var credential = credentialStore.Read(channel.CredentialTarget);
        if (credential is { Secret.Length: > 0 })
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Value.Secret);
        }
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendEmailAsync(
        NotificationChannel channel,
        TaskResultNotification notification,
        CancellationToken cancellationToken)
    {
        var endpoint = Uri.TryCreate(channel.Endpoint, UriKind.Absolute, out var uri)
            ? uri : throw new InvalidOperationException("SMTP 端点无效，应为 smtp://host:port 或 smtps://host:port。");
        if (endpoint.Scheme is not ("smtp" or "smtps")) throw new InvalidOperationException("SMTP 端点协议无效。");
        var credential = credentialStore.Read(channel.CredentialTarget);
        using var client = new SmtpClient(endpoint.Host, endpoint.IsDefaultPort ? 587 : endpoint.Port)
        {
            EnableSsl = endpoint.Scheme == "smtps" || endpoint.Port == 465,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = credential is null,
            Credentials = credential is null ? null : new NetworkCredential(
                string.IsNullOrWhiteSpace(channel.UserName) ? credential.Value.UserName : channel.UserName,
                credential.Value.Secret),
            Timeout = 30_000,
        };
        using var message = new MailMessage
        {
            From = new MailAddress(channel.Sender),
            Subject = $"CopyTools：{notification.TaskName} - {notification.Status}",
            Body = $"任务：{notification.TaskName}\r\n状态：{notification.Status}\r\n"
                + $"成功文件：{notification.SuccessfulFiles}\r\n失败文件：{notification.FailedFiles}\r\n"
                + $"复制字节：{notification.CopiedBytes}\r\n完成时间：{notification.CompletedAtUtc:O}\r\n"
                + $"消息：{notification.Message}",
            IsBodyHtml = false,
        };
        foreach (var recipient in channel.Recipients.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            message.To.Add(recipient);
        }
        if (message.To.Count == 0) throw new InvalidOperationException("SMTP 通知没有收件人。");
        await client.SendMailAsync(message, cancellationToken);
    }

    private async Task SendImAsync(
        NotificationChannel channel,
        TaskResultNotification notification,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(channel.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("即时通讯 Webhook 端点无效。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                BuildImBody(channel.Kind, channel, notification), Encoding.UTF8, "application/json"),
        };
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static readonly JsonSerializerOptions ImJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string BuildImBody(
        NotificationChannelKind kind,
        NotificationChannel channel,
        TaskResultNotification notification)
    {
        var text = FormatMessage(notification);
        return kind switch
        {
            NotificationChannelKind.DingTalk or NotificationChannelKind.WeCom =>
                JsonSerializer.Serialize(new { msgtype = "text", text = new { content = text } }, ImJsonOptions),
            NotificationChannelKind.Feishu =>
                JsonSerializer.Serialize(new { msg_type = "text", content = new { text } }, ImJsonOptions),
            NotificationChannelKind.Slack =>
                JsonSerializer.Serialize(new { text }, ImJsonOptions),
            NotificationChannelKind.Telegram =>
                JsonSerializer.Serialize(new { chat_id = channel.Recipients, text }, ImJsonOptions),
            _ => throw new NotSupportedException($"不支持的通知类型 {kind}。"),
        };
    }

    private static string FormatMessage(TaskResultNotification notification) =>
        $"任务：{notification.TaskName}\n状态：{notification.Status}\n"
        + $"成功 {notification.SuccessfulFiles} 个，失败 {notification.FailedFiles} 个\n"
        + $"复制字节：{notification.CopiedBytes}\n{notification.Message}";

    private static string Sanitize(string value)
    {
        var sanitized = Regex.Replace(value,
            "(?i)(access[_-]?token|refresh[_-]?token|token|api[_-]?key|secret|password)=([^&\\s]+)",
            "$1=[已隐藏]");
        sanitized = Regex.Replace(sanitized, "(?i)Bearer\\s+[A-Za-z0-9._~+/-]+=*", "Bearer [已隐藏]");
        return sanitized.Length <= 500 ? sanitized : sanitized[..500];
    }
}
