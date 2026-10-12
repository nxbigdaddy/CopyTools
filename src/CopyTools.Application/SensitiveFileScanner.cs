namespace CopyTools.Application;

public sealed record SensitiveFileResult(string RelativePath, bool IsSensitive, string Reason);

/// <summary>按文件名识别凭据、密钥与隐私文件，用于默认排除与同步提醒。</summary>
public sealed class SensitiveFileScanner
{
    private static readonly string[] SensitiveKeywords =
    [
        ".env", "id_rsa", "id_dsa", "id_ed25519", ".key", ".pem", ".p12", ".pfx",
        "secret", "credential", "token", "password", "密码", "凭据", "密钥", "私钥",
        "身份证", "银行卡", "病历", "户口", "护照", "驾驶证", "社保", "工资条",
    ];

    public SensitiveFileResult Scan(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        foreach (var keyword in SensitiveKeywords)
        {
            if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return new SensitiveFileResult(relativePath, true, $"文件名包含敏感关键词：{keyword}");
            }
        }

        return new SensitiveFileResult(relativePath, false, string.Empty);
    }
}
