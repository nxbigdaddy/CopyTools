using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class RunLogExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task ExportJsonAsync(
        RunHistoryEntry run,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, run, JsonOptions, cancellationToken);
    }

    public Task ExportCsvAsync(
        RunHistoryEntry run,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine("相对路径,操作,结果,字节数,尝试次数,错误");
        foreach (var item in run.Items)
        {
            builder.Append(Csv(item.RelativePath)).Append(',')
                .Append(Csv(item.Action.ToString())).Append(',')
                .Append(Csv(item.Result)).Append(',')
                .Append(item.Bytes).Append(',')
                .Append(item.Attempts).Append(',')
                .Append(Csv(item.ErrorMessage ?? string.Empty)).AppendLine();
        }

        return File.WriteAllTextAsync(filePath, builder.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    public Task ExportTextAsync(
        RunHistoryEntry run,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder()
            .AppendLine($"任务：{run.TaskName}")
            .AppendLine($"开始：{run.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"结束：{run.CompletedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"结果：{run.Status}")
            .AppendLine($"成功：{run.SuccessfulFiles}，跳过：{run.SkippedFiles}，失败：{run.FailedFiles}")
            .AppendLine();
        foreach (var item in run.Items)
        {
            builder.Append('[').Append(item.Result).Append("] ")
                .Append(item.RelativePath).Append(" / ").Append(item.Action);
            if (!string.IsNullOrEmpty(item.ErrorMessage))
            {
                builder.Append(" / ").Append(item.ErrorMessage);
            }

            builder.AppendLine();
        }

        return File.WriteAllTextAsync(filePath, builder.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
