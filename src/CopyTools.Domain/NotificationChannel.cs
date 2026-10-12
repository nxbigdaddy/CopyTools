namespace CopyTools.Domain;

public sealed record NotificationChannel(
    Guid Id,
    string Name,
    NotificationChannelKind Kind,
    bool Enabled,
    string Endpoint,
    string UserName,
    string CredentialTarget,
    string Sender,
    string Recipients,
    bool NotifyOnSuccess = true,
    bool NotifyOnFailure = true,
    DateTime? UpdatedAtUtc = null);
