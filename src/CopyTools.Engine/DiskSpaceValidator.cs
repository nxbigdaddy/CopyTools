using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class DiskSpaceValidator
{
    public void Validate(CopyPlan plan)
    {
        var requiredBytes = plan.BytesToCopy;
        if (requiredBytes <= 0)
        {
            return;
        }

        var root = Path.GetPathRoot(plan.Request.TargetDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
            {
                throw new CopyTaskValidationException(
                    $"目标磁盘可用空间不足：需要至少 {FormatBytes(requiredBytes)}，"
                    + $"当前可用 {FormatBytes(drive.AvailableFreeSpace)}。");
            }
        }
        catch (CopyTaskValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // 某些 UNC/虚拟文件系统不支持 DriveInfo，复制时仍由文件系统返回准确错误。
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
