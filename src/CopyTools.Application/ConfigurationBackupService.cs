using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class ConfigurationBackupService(
    ICopyToolsRepository repository,
    ISettingsStore settingsStore)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var bundle = new ConfigurationExportBundle(
            1,
            DateTime.UtcNow,
            (await settingsStore.LoadAsync(cancellationToken)).Normalize(),
            await repository.GetTasksAsync(cancellationToken),
            await repository.GetSchedulesAsync(cancellationToken),
            await repository.GetAutoTriggerRulesAsync(cancellationToken),
            await repository.GetAllTaskExecutionOptionsAsync(cancellationToken),
            await repository.GetStorageConnectionsAsync(cancellationToken),
            await repository.GetNotificationChannelsAsync(cancellationToken),
            await repository.GetFilterRuleTemplatesAsync(cancellationToken),
            await repository.GetTaskTemplatesAsync(cancellationToken));

        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, bundle, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<ConfigurationImportResult> ImportAsync(
        string filePath,
        bool replaceExisting,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            Path.GetFullPath(filePath), FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bundle = await JsonSerializer.DeserializeAsync<ConfigurationExportBundle>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("配置备份为空或格式无效。");
        if (bundle.FormatVersion != 1)
            throw new InvalidDataException($"不支持的配置备份版本：{bundle.FormatVersion}。");

        var tasks = bundle.Tasks ?? throw new InvalidDataException("配置备份缺少任务列表。");
        if (replaceExisting) await repository.ClearConfigurationAsync(cancellationToken);

        var existingTaskIds = (await repository.GetTasksAsync(cancellationToken)).Select(item => item.Id).ToHashSet();
        var idMap = new Dictionary<Guid, Guid>();
        foreach (var task in tasks)
        {
            var id = existingTaskIds.Contains(task.Id) ? Guid.NewGuid() : task.Id;
            existingTaskIds.Add(id);
            idMap[task.Id] = id;
            var request = task.Request.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
                ? task.Request with { SynchronizationId = id }
                : task.Request;
            await repository.SaveTaskAsync(task with
            {
                Id = id,
                Name = id == task.Id ? task.Name : $"{task.Name}（恢复）",
                Request = request,
                UpdatedAtUtc = DateTime.UtcNow,
            }, cancellationToken);
        }

        foreach (var schedule in bundle.Schedules ?? [])
            if (idMap.TryGetValue(schedule.TaskId, out var taskId))
                await repository.SaveScheduleAsync(schedule with { TaskId = taskId, NextRunAtUtc = null }, cancellationToken);
        foreach (var rule in bundle.AutoTriggerRules ?? [])
            if (idMap.TryGetValue(rule.TaskId, out var taskId))
                await repository.SaveAutoTriggerRuleAsync(rule with { TaskId = taskId }, cancellationToken);
        foreach (var options in bundle.ExecutionOptions ?? [])
        {
            if (!idMap.TryGetValue(options.TaskId, out var taskId)) continue;
            var dependencies = options.EffectiveDependencies
                .Where(idMap.ContainsKey).Select(id => idMap[id]).Where(id => id != taskId).Distinct().ToArray();
            await repository.SaveTaskExecutionOptionsAsync(
                options with { TaskId = taskId, Dependencies = dependencies }, cancellationToken);
        }

        var connectionCount = 0;
        var existingConnectionIds = (await repository.GetStorageConnectionsAsync(cancellationToken))
            .Select(item => item.Id).ToHashSet();
        foreach (var connection in bundle.StorageConnections ?? [])
        {
            var id = existingConnectionIds.Contains(connection.Id) ? Guid.NewGuid() : connection.Id;
            existingConnectionIds.Add(id);
            await repository.SaveStorageConnectionAsync(connection with
            {
                Id = id,
                Name = id == connection.Id ? connection.Name : $"{connection.Name}（恢复）",
                CredentialTarget = $"CopyTools/Storage/{id:D}",
                UpdatedAtUtc = DateTime.UtcNow,
            }, cancellationToken);
            connectionCount++;
        }

        var channelCount = 0;
        var existingChannelIds = (await repository.GetNotificationChannelsAsync(cancellationToken))
            .Select(item => item.Id).ToHashSet();
        foreach (var channel in bundle.NotificationChannels ?? [])
        {
            var id = existingChannelIds.Contains(channel.Id) ? Guid.NewGuid() : channel.Id;
            existingChannelIds.Add(id);
            await repository.SaveNotificationChannelAsync(channel with
            {
                Id = id,
                Name = id == channel.Id ? channel.Name : $"{channel.Name}（恢复）",
                CredentialTarget = $"CopyTools/notification/{id:D}",
                UpdatedAtUtc = DateTime.UtcNow,
            }, cancellationToken);
            channelCount++;
        }

        var templateCount = 0;
        var filterIds = (await repository.GetFilterRuleTemplatesAsync(cancellationToken)).Select(item => item.Id).ToHashSet();
        foreach (var template in bundle.FilterRuleTemplates ?? [])
        {
            var id = filterIds.Contains(template.Id) ? Guid.NewGuid() : template.Id;
            filterIds.Add(id);
            await repository.SaveFilterRuleTemplateAsync(template with
            {
                Id = id, Name = id == template.Id ? template.Name : $"{template.Name}（恢复）", UpdatedAtUtc = DateTime.UtcNow,
            }, cancellationToken);
            templateCount++;
        }
        var taskTemplateIds = (await repository.GetTaskTemplatesAsync(cancellationToken)).Select(item => item.Id).ToHashSet();
        foreach (var template in bundle.TaskTemplates ?? [])
        {
            var id = taskTemplateIds.Contains(template.Id) ? Guid.NewGuid() : template.Id;
            taskTemplateIds.Add(id);
            await repository.SaveTaskTemplateAsync(template with
            {
                Id = id, Name = id == template.Id ? template.Name : $"{template.Name}（恢复）", UpdatedAtUtc = DateTime.UtcNow,
            }, cancellationToken);
            templateCount++;
        }

        var settings = (bundle.Settings ?? new ApplicationSettings()).Normalize() with
        {
            ProxyCredentialTarget = "CopyTools/Proxy",
        };
        await settingsStore.SaveAsync(settings, cancellationToken);
        return new ConfigurationImportResult(tasks.Count, connectionCount, channelCount, templateCount, replaceExisting);
    }
}
