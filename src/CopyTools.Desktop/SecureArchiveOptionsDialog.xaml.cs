using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CopyTools.Desktop;

public partial class SecureArchiveOptionsDialog : Window
{
    private readonly bool _restoreMode;

    public SecureArchiveOptionsDialog(bool restoreMode, bool encryptedArchive = false)
    {
        InitializeComponent();
        _restoreMode = restoreMode;
        if (restoreMode)
        {
            HeadingText.Text = "恢复安全归档";
            DescriptionText.Text = encryptedArchive
                ? "该归档已加密，请输入创建时使用的密码。"
                : "该归档未加密，可直接恢复。";
            CompressCheckBox.Visibility = Visibility.Collapsed;
            EncryptCheckBox.Visibility = Visibility.Collapsed;
            PasswordPanel.Visibility = encryptedArchive ? Visibility.Visible : Visibility.Visible;
            ConfirmLabel.Visibility = Visibility.Collapsed;
            ConfirmPasswordInput.Visibility = Visibility.Collapsed;
            OverwriteCheckBox.Visibility = Visibility.Visible;
            PasswordInput.IsEnabled = encryptedArchive;
        }
        else
        {
            HeadingText.Text = "创建安全归档";
            DescriptionText.Text = "生成标准 ZIP，或使用带完整认证的 CopyTools 加密归档（.ctbackup）。";
            OverwriteCheckBox.Visibility = Visibility.Collapsed;
            EncryptCheckBox.IsChecked = true;
            EncryptionChanged(this, new RoutedEventArgs());
        }
    }

    public bool Compress => CompressCheckBox.IsChecked == true;
    public bool Encrypt => !_restoreMode && EncryptCheckBox.IsChecked == true;
    public string? Password => PasswordInput.IsEnabled && PasswordInput.Password.Length > 0
        ? PasswordInput.Password
        : null;
    public bool Overwrite => OverwriteCheckBox.IsChecked == true;

    private void EncryptionChanged(object sender, RoutedEventArgs e)
    {
        if (_restoreMode) return;
        var enabled = EncryptCheckBox.IsChecked == true;
        PasswordInput.IsEnabled = enabled;
        ConfirmPasswordInput.IsEnabled = enabled;
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!_restoreMode && Encrypt && string.IsNullOrEmpty(PasswordInput.Password))
        {
            MessageBox.Show("启用加密时密码不能为空。", "安全归档", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_restoreMode && Encrypt && PasswordInput.Password != ConfirmPasswordInput.Password)
        {
            MessageBox.Show("两次输入的密码不一致。", "安全归档", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
