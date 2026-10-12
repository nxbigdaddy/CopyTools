using System.Windows;
using System.Windows.Controls;
using CopyTools.Domain;
using MessageBox = System.Windows.MessageBox;

namespace CopyTools.Desktop;

public partial class NotificationChannelDialog : Window
{
    private readonly NotificationChannel? _existing;
    public NotificationChannelDialog(NotificationChannel? existing = null)
    {
        InitializeComponent(); _existing = existing;
        NameInput.Text = existing?.Name ?? string.Empty;
        KindInput.SelectedIndex = existing?.Kind == NotificationChannelKind.Smtp ? 1 : 0;
        EnabledInput.IsChecked = existing?.Enabled ?? true;
        EndpointInput.Text = existing?.Endpoint ?? string.Empty;
        UserInput.Text = existing?.UserName ?? string.Empty;
        SenderInput.Text = existing?.Sender ?? string.Empty;
        RecipientsInput.Text = existing?.Recipients ?? string.Empty;
        SuccessInput.IsChecked = existing?.NotifyOnSuccess ?? true;
        FailureInput.IsChecked = existing?.NotifyOnFailure ?? true;
        UpdateVisibility();
    }

    public NotificationChannel? Channel { get; private set; }
    public string? Secret => SecretInput.Password.Length == 0 ? null : SecretInput.Password;
    private NotificationChannelKind Kind => KindInput.SelectedIndex == 1 ? NotificationChannelKind.Smtp : NotificationChannelKind.Webhook;

    private void KindChanged(object sender, SelectionChangedEventArgs e) => UpdateVisibility();
    private void UpdateVisibility()
    {
        if (!IsInitialized) return;
        var smtp = Kind == NotificationChannelKind.Smtp;
        UserLabel.Visibility = UserInput.Visibility = smtp ? Visibility.Visible : Visibility.Collapsed;
        SenderLabel.Visibility = SenderInput.Visibility = smtp ? Visibility.Visible : Visibility.Collapsed;
        RecipientsLabel.Visibility = RecipientsInput.Visibility = smtp ? Visibility.Visible : Visibility.Collapsed;
        EndpointHelp.Text = smtp ? "例如 smtps://smtp.example.com:465 或 smtp://smtp.example.com:587" : "必须使用完整的 http:// 或 https:// 地址。可选令牌通过 Bearer 头发送。";
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text) || string.IsNullOrWhiteSpace(EndpointInput.Text))
        { MessageBox.Show("名称和端点不能为空。", "通知频道", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var id = _existing?.Id ?? Guid.NewGuid();
        Channel = new NotificationChannel(id, NameInput.Text.Trim(), Kind, EnabledInput.IsChecked == true,
            EndpointInput.Text.Trim(), UserInput.Text.Trim(), _existing?.CredentialTarget ?? $"CopyTools/notification/{id:D}",
            SenderInput.Text.Trim(), RecipientsInput.Text.Trim(), SuccessInput.IsChecked == true,
            FailureInput.IsChecked == true, DateTime.UtcNow);
        DialogResult = true;
    }
}
