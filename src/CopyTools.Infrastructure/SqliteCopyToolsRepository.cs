using System.Text.Json;
using System.Text.Json.Serialization;
using CopyTools.Application;
using CopyTools.Domain;

namespace CopyTools.Infrastructure;

public sealed class SqliteCopyToolsRepository : ICopyToolsRepository, CopyTools.Engine.ISyncSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteCopyToolsRepository(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                database.Execute("PRAGMA journal_mode=WAL;");
                database.Execute("PRAGMA foreign_keys=ON;");
                database.Execute("PRAGMA busy_timeout=5000;");
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS saved_tasks (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL,
                        request_json TEXT NOT NULL,
                        created_utc TEXT NOT NULL,
                        updated_utc TEXT NOT NULL
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS filter_rule_templates (
                        id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL,
                        template_json TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS task_templates (
                        id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL,
                        template_json TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS sync_snapshot (
                        synchronization_id TEXT NOT NULL,
                        relative_path TEXT NOT NULL,
                        source_exists INTEGER NOT NULL,
                        source_length INTEGER NULL,
                        source_modified_utc TEXT NULL,
                        source_hash TEXT NULL,
                        target_exists INTEGER NOT NULL,
                        target_length INTEGER NULL,
                        target_modified_utc TEXT NULL,
                        target_hash TEXT NULL,
                        PRIMARY KEY (synchronization_id, relative_path)
                    );
                    """);
                EnsureColumn(database, "sync_snapshot", "source_hash", "TEXT NULL");
                EnsureColumn(database, "sync_snapshot", "target_hash", "TEXT NULL");
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS task_schedules (
                        task_id TEXT PRIMARY KEY NOT NULL,
                        enabled INTEGER NOT NULL,
                        trigger INTEGER NOT NULL,
                        local_time_ticks INTEGER NOT NULL,
                        day_of_week INTEGER NOT NULL,
                        day_of_month INTEGER NOT NULL,
                        interval_ticks INTEGER NULL,
                        missed_run_policy INTEGER NOT NULL,
                        allow_overwrite INTEGER NOT NULL,
                        allow_quarantine INTEGER NOT NULL,
                        last_triggered_utc TEXT NULL,
                        next_run_utc TEXT NULL,
                        FOREIGN KEY (task_id) REFERENCES saved_tasks(id) ON DELETE CASCADE
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS auto_trigger_rules (
                        task_id TEXT PRIMARY KEY NOT NULL,
                        kind INTEGER NOT NULL,
                        enabled INTEGER NOT NULL,
                        debounce_ticks INTEGER NOT NULL,
                        allow_overwrite INTEGER NOT NULL,
                        allow_quarantine INTEGER NOT NULL,
                        FOREIGN KEY (task_id) REFERENCES saved_tasks(id) ON DELETE CASCADE
                    );
                    """);
                EnsureColumn(database, "task_schedules", "conditions_json", "TEXT NULL");
                EnsureColumn(database, "task_schedules", "window_json", "TEXT NULL");
                EnsureColumn(database, "auto_trigger_rules", "min_interval_ticks", "INTEGER NULL");
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS runs (
                        id TEXT PRIMARY KEY NOT NULL,
                        task_id TEXT NULL,
                        task_name TEXT NOT NULL,
                        request_json TEXT NOT NULL,
                        started_utc TEXT NOT NULL,
                        completed_utc TEXT NOT NULL,
                        status TEXT NOT NULL,
                        successful_files INTEGER NOT NULL,
                        skipped_files INTEGER NOT NULL,
                        failed_files INTEGER NOT NULL,
                        copied_bytes INTEGER NOT NULL
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS run_items (
                        run_id TEXT NOT NULL,
                        ordinal INTEGER NOT NULL,
                        relative_path TEXT NOT NULL,
                        action INTEGER NOT NULL,
                        result TEXT NOT NULL,
                        bytes INTEGER NOT NULL,
                        error_message TEXT NULL,
                        attempts INTEGER NOT NULL,
                        PRIMARY KEY (run_id, ordinal),
                        FOREIGN KEY (run_id) REFERENCES runs(id) ON DELETE CASCADE
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS quarantine_entries (
                        id TEXT PRIMARY KEY NOT NULL,
                        run_id TEXT NOT NULL,
                        original_path TEXT NOT NULL,
                        quarantine_path TEXT NOT NULL,
                        relative_path TEXT NOT NULL,
                        length INTEGER NOT NULL,
                        quarantined_utc TEXT NOT NULL,
                        restored_utc TEXT NULL,
                        FOREIGN KEY (run_id) REFERENCES runs(id) ON DELETE CASCADE
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS storage_connections (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL,
                        kind INTEGER NOT NULL,
                        endpoint TEXT NOT NULL,
                        root_path TEXT NOT NULL,
                        user_name TEXT NOT NULL,
                        credential_target TEXT NOT NULL,
                        region TEXT NOT NULL,
                        bucket TEXT NOT NULL,
                        created_utc TEXT NOT NULL,
                        updated_utc TEXT NOT NULL
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS task_execution_options (
                        task_id TEXT PRIMARY KEY NOT NULL,
                        options_json TEXT NOT NULL,
                        FOREIGN KEY (task_id) REFERENCES saved_tasks(id) ON DELETE CASCADE
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS incomplete_executions (
                        id TEXT PRIMARY KEY NOT NULL,
                        task_id TEXT NOT NULL UNIQUE,
                        request_json TEXT NOT NULL,
                        plan_created_utc TEXT NOT NULL,
                        started_utc TEXT NOT NULL,
                        FOREIGN KEY (task_id) REFERENCES saved_tasks(id) ON DELETE CASCADE
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS incomplete_execution_items (
                        execution_id TEXT NOT NULL,
                        relative_path TEXT NOT NULL,
                        action INTEGER NOT NULL,
                        length INTEGER NOT NULL,
                        source_modified_utc TEXT NOT NULL,
                        completed_utc TEXT NOT NULL,
                        PRIMARY KEY (execution_id, relative_path, action),
                        FOREIGN KEY (execution_id) REFERENCES incomplete_executions(id) ON DELETE CASCADE
                    );
                    """);
                database.Execute(
                    """
                    CREATE TABLE IF NOT EXISTS notification_channels (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL,
                        kind INTEGER NOT NULL,
                        enabled INTEGER NOT NULL,
                        endpoint TEXT NOT NULL,
                        user_name TEXT NOT NULL,
                        credential_target TEXT NOT NULL,
                        sender TEXT NOT NULL,
                        recipients TEXT NOT NULL,
                        notify_success INTEGER NOT NULL,
                        notify_failure INTEGER NOT NULL,
                        updated_utc TEXT NOT NULL
                    );
                    """);
                database.Execute("CREATE INDEX IF NOT EXISTS ix_runs_completed_utc ON runs(completed_utc DESC);");
                database.Execute("CREATE INDEX IF NOT EXISTS ix_runs_task_id ON runs(task_id, completed_utc DESC);");
                database.Execute(
                    "CREATE INDEX IF NOT EXISTS ix_quarantine_restored_utc "
                    + "ON quarantine_entries(restored_utc, quarantined_utc DESC);");
                return true;
            },
            cancellationToken);

    public Task<IReadOnlyList<SavedCopyTask>> GetTasksAsync(CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<SavedCopyTask>>(
            database =>
            {
                using var statement = database.Prepare(
                    "SELECT id, name, request_json, created_utc, updated_utc "
                    + "FROM saved_tasks ORDER BY name COLLATE NOCASE, updated_utc DESC;");
                var tasks = new List<SavedCopyTask>();
                while (statement.Read())
                {
                    tasks.Add(ReadTask(statement));
                }

                return tasks;
            },
            cancellationToken);

    public Task<SavedCopyTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<SavedCopyTask?>(
            database =>
            {
                using var statement = database.Prepare(
                    "SELECT id, name, request_json, created_utc, updated_utc "
                    + "FROM saved_tasks WHERE id = ?1;");
                statement.BindText(1, id.ToString("D"));
                return statement.Read() ? ReadTask(statement) : null;
            },
            cancellationToken);

    public Task SaveTaskAsync(SavedCopyTask task, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    INSERT INTO saved_tasks(id, name, request_json, created_utc, updated_utc)
                    VALUES(?1, ?2, ?3, ?4, ?5)
                    ON CONFLICT(id) DO UPDATE SET
                        name = excluded.name,
                        request_json = excluded.request_json,
                        updated_utc = excluded.updated_utc;
                    """);
                statement.BindText(1, task.Id.ToString("D"));
                statement.BindText(2, task.Name);
                statement.BindText(3, SerializeRequest(task.Request));
                statement.BindText(4, FormatDate(task.CreatedAtUtc));
                statement.BindText(5, FormatDate(task.UpdatedAtUtc));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare("DELETE FROM saved_tasks WHERE id = ?1;");
                statement.BindText(1, id.ToString("D"));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task SaveRunAsync(RunHistoryEntry run, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                database.Execute("BEGIN IMMEDIATE;");
                try
                {
                    InsertRun(database, run);
                    InsertRunItems(database, run);
                    InsertQuarantineEntries(database, run);
                    database.Execute("COMMIT;");
                }
                catch
                {
                    try
                    {
                        database.Execute("ROLLBACK;");
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    throw;
                }

                return true;
            },
            cancellationToken);

    public Task<IReadOnlyList<RunHistoryEntry>> GetRecentRunsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 1000);
        return WithDatabaseAsync<IReadOnlyList<RunHistoryEntry>>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT id, task_id, task_name, request_json, started_utc, completed_utc,
                           status, successful_files, skipped_files, failed_files, copied_bytes
                    FROM runs
                    ORDER BY completed_utc DESC
                    LIMIT ?1;
                    """);
                statement.BindInt(1, boundedLimit);
                var runs = new List<RunHistoryEntry>();
                while (statement.Read())
                {
                    runs.Add(ReadRun(database, statement));
                }

                return runs;
            },
            cancellationToken);
    }

    public Task<RunHistoryEntry?> GetRunAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<RunHistoryEntry?>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT id, task_id, task_name, request_json, started_utc, completed_utc,
                           status, successful_files, skipped_files, failed_files, copied_bytes
                    FROM runs WHERE id = ?1;
                    """);
                statement.BindText(1, id.ToString("D"));
                return statement.Read() ? ReadRun(database, statement) : null;
            },
            cancellationToken);

    public Task<IReadOnlyList<RunHistoryEntry>> QueryRunsAsync(
        RunHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();
        return WithDatabaseAsync<IReadOnlyList<RunHistoryEntry>>(database =>
        {
            using var statement = database.Prepare(
                """
                SELECT id, task_id, task_name, request_json, started_utc, completed_utc,
                       status, successful_files, skipped_files, failed_files, copied_bytes
                FROM runs
                WHERE (?1 IS NULL OR task_name LIKE ?1 ESCAPE '\')
                  AND (?2 IS NULL OR status = ?2)
                  AND (?3 IS NULL OR completed_utc >= ?3)
                  AND (?4 IS NULL OR completed_utc <= ?4)
                ORDER BY completed_utc DESC LIMIT ?5;
                """);
            statement.BindText(1, normalized.TaskName is null ? null : $"%{EscapeLike(normalized.TaskName)}%");
            statement.BindText(2, normalized.Status);
            statement.BindText(3, normalized.FromUtc is null ? null : FormatDate(normalized.FromUtc.Value));
            statement.BindText(4, normalized.ToUtc is null ? null : FormatDate(normalized.ToUtc.Value));
            statement.BindInt(5, normalized.Limit);
            var runs = new List<RunHistoryEntry>();
            while (statement.Read()) runs.Add(ReadRun(database, statement));
            return runs;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<QuarantineEntry>> GetQuarantineEntriesAsync(
        bool includeRestored = false,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<QuarantineEntry>>(
            database =>
            {
                var sql =
                    "SELECT id, run_id, original_path, quarantine_path, relative_path, length, "
                    + "quarantined_utc, restored_utc FROM quarantine_entries ";
                if (!includeRestored)
                {
                    sql += "WHERE restored_utc IS NULL ";
                }

                sql += "ORDER BY quarantined_utc DESC;";
                using var statement = database.Prepare(sql);
                var entries = new List<QuarantineEntry>();
                while (statement.Read())
                {
                    entries.Add(ReadQuarantineEntry(statement));
                }

                return entries;
            },
            cancellationToken);

    public Task MarkQuarantineRestoredAsync(
        Guid id,
        DateTime restoredAtUtc,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare(
                    "UPDATE quarantine_entries SET restored_utc = ?2 WHERE id = ?1 AND restored_utc IS NULL;");
                statement.BindText(1, id.ToString("D"));
                statement.BindText(2, FormatDate(restoredAtUtc));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task<IReadOnlyList<TaskSchedule>> GetSchedulesAsync(
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<TaskSchedule>>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT task_id, enabled, trigger, local_time_ticks, day_of_week,
                           day_of_month, interval_ticks, missed_run_policy,
                           allow_overwrite, allow_quarantine, last_triggered_utc, next_run_utc,
                           conditions_json, window_json
                    FROM task_schedules
                    ORDER BY next_run_utc, task_id;
                    """);
                var schedules = new List<TaskSchedule>();
                while (statement.Read())
                {
                    schedules.Add(ReadSchedule(statement));
                }

                return schedules;
            },
            cancellationToken);

    public Task<TaskSchedule?> GetScheduleAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<TaskSchedule?>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT task_id, enabled, trigger, local_time_ticks, day_of_week,
                           day_of_month, interval_ticks, missed_run_policy,
                           allow_overwrite, allow_quarantine, last_triggered_utc, next_run_utc,
                           conditions_json, window_json
                    FROM task_schedules WHERE task_id = ?1;
                    """);
                statement.BindText(1, taskId.ToString("D"));
                return statement.Read() ? ReadSchedule(statement) : null;
            },
            cancellationToken);

    public Task SaveScheduleAsync(
        TaskSchedule schedule,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                var normalized = schedule.Normalize();
                using var statement = database.Prepare(
                    """
                    INSERT INTO task_schedules(
                        task_id, enabled, trigger, local_time_ticks, day_of_week,
                        day_of_month, interval_ticks, missed_run_policy,
                        allow_overwrite, allow_quarantine, last_triggered_utc, next_run_utc,
                        conditions_json, window_json)
                    VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14)
                    ON CONFLICT(task_id) DO UPDATE SET
                        enabled = excluded.enabled,
                        trigger = excluded.trigger,
                        local_time_ticks = excluded.local_time_ticks,
                        day_of_week = excluded.day_of_week,
                        day_of_month = excluded.day_of_month,
                        interval_ticks = excluded.interval_ticks,
                        missed_run_policy = excluded.missed_run_policy,
                        allow_overwrite = excluded.allow_overwrite,
                        allow_quarantine = excluded.allow_quarantine,
                        last_triggered_utc = excluded.last_triggered_utc,
                        next_run_utc = excluded.next_run_utc,
                        conditions_json = excluded.conditions_json,
                        window_json = excluded.window_json;
                    """);
                statement.BindText(1, normalized.TaskId.ToString("D"));
                statement.BindInt(2, normalized.Enabled ? 1 : 0);
                statement.BindInt(3, (int)normalized.Trigger);
                statement.BindInt64(4, normalized.LocalTime.Ticks);
                statement.BindInt(5, (int)normalized.DayOfWeek);
                statement.BindInt(6, normalized.DayOfMonth);
                statement.BindNullableInt64(7, normalized.Interval?.Ticks);
                statement.BindInt(8, (int)normalized.MissedRunPolicy);
                statement.BindInt(9, normalized.AllowOverwrite ? 1 : 0);
                statement.BindInt(10, normalized.AllowQuarantine ? 1 : 0);
                statement.BindText(11, normalized.LastTriggeredAtUtc is null
                    ? null
                    : FormatDate(normalized.LastTriggeredAtUtc.Value));
                statement.BindText(12, normalized.NextRunAtUtc is null
                    ? null
                    : FormatDate(normalized.NextRunAtUtc.Value));
                statement.BindText(13, SerializeConditions(normalized.EffectiveConditions));
                statement.BindText(14, normalized.TimeWindow is null
                    ? null
                    : JsonSerializer.Serialize(normalized.TimeWindow, JsonOptions));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task DeleteScheduleAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare("DELETE FROM task_schedules WHERE task_id = ?1;");
                statement.BindText(1, taskId.ToString("D"));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task<IReadOnlyList<AutoTriggerRule>> GetAutoTriggerRulesAsync(
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<AutoTriggerRule>>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT task_id, kind, enabled, debounce_ticks, allow_overwrite, allow_quarantine, min_interval_ticks
                    FROM auto_trigger_rules ORDER BY task_id;
                    """);
                var rules = new List<AutoTriggerRule>();
                while (statement.Read())
                {
                    var minIntervalTicks = statement.GetNullableInt64(6);
                    rules.Add(new AutoTriggerRule(
                        Guid.Parse(statement.GetString(0)),
                        (AutoTriggerKind)statement.GetInt(1),
                        statement.GetInt(2) != 0,
                        TimeSpan.FromTicks(statement.GetInt64(3)),
                        statement.GetInt(4) != 0,
                        statement.GetInt(5) != 0,
                        minIntervalTicks is null ? null : TimeSpan.FromTicks(minIntervalTicks.Value)));
                }

                return rules;
            },
            cancellationToken);

    public Task SaveAutoTriggerRuleAsync(
        AutoTriggerRule rule,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                var normalized = rule.Normalize();
                using var statement = database.Prepare(
                    """
                    INSERT INTO auto_trigger_rules(
                        task_id, kind, enabled, debounce_ticks, allow_overwrite, allow_quarantine, min_interval_ticks)
                    VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7)
                    ON CONFLICT(task_id) DO UPDATE SET
                        kind = excluded.kind,
                        enabled = excluded.enabled,
                        debounce_ticks = excluded.debounce_ticks,
                        allow_overwrite = excluded.allow_overwrite,
                        allow_quarantine = excluded.allow_quarantine,
                        min_interval_ticks = excluded.min_interval_ticks;
                    """);
                statement.BindText(1, normalized.TaskId.ToString("D"));
                statement.BindInt(2, (int)normalized.Kind);
                statement.BindInt(3, normalized.Enabled ? 1 : 0);
                statement.BindInt64(4, normalized.DebounceDelay.Ticks);
                statement.BindInt(5, normalized.AllowOverwrite ? 1 : 0);
                statement.BindInt(6, normalized.AllowQuarantine ? 1 : 0);
                statement.BindNullableInt64(7, normalized.MinIntervalBetweenRuns?.Ticks);
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task DeleteAutoTriggerRuleAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare("DELETE FROM auto_trigger_rules WHERE task_id = ?1;");
                statement.BindText(1, taskId.ToString("D"));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task<IReadOnlyList<StorageConnectionProfile>> GetStorageConnectionsAsync(
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<StorageConnectionProfile>>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT id, name, kind, endpoint, root_path, user_name, credential_target,
                           region, bucket, created_utc, updated_utc
                    FROM storage_connections
                    ORDER BY name COLLATE NOCASE, updated_utc DESC;
                    """);
                var connections = new List<StorageConnectionProfile>();
                while (statement.Read())
                {
                    connections.Add(new StorageConnectionProfile(
                        Guid.Parse(statement.GetString(0)), statement.GetString(1),
                        (StorageConnectionKind)statement.GetInt(2), statement.GetString(3),
                        statement.GetString(4), statement.GetString(5), statement.GetString(6),
                        statement.GetString(7), statement.GetString(8),
                        ParseDate(statement.GetString(9)), ParseDate(statement.GetString(10))));
                }

                return connections;
            },
            cancellationToken);

    public Task SaveStorageConnectionAsync(
        StorageConnectionProfile connection,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    INSERT INTO storage_connections(
                        id, name, kind, endpoint, root_path, user_name, credential_target,
                        region, bucket, created_utc, updated_utc)
                    VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11)
                    ON CONFLICT(id) DO UPDATE SET
                        name = excluded.name,
                        kind = excluded.kind,
                        endpoint = excluded.endpoint,
                        root_path = excluded.root_path,
                        user_name = excluded.user_name,
                        credential_target = excluded.credential_target,
                        region = excluded.region,
                        bucket = excluded.bucket,
                        updated_utc = excluded.updated_utc;
                    """);
                statement.BindText(1, connection.Id.ToString("D"));
                statement.BindText(2, connection.Name);
                statement.BindInt(3, (int)connection.Kind);
                statement.BindText(4, connection.Endpoint);
                statement.BindText(5, connection.RootPath);
                statement.BindText(6, connection.UserName);
                statement.BindText(7, connection.CredentialTarget);
                statement.BindText(8, connection.Region);
                statement.BindText(9, connection.Bucket);
                statement.BindText(10, FormatDate(connection.CreatedAtUtc));
                statement.BindText(11, FormatDate(connection.UpdatedAtUtc));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task DeleteStorageConnectionAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare("DELETE FROM storage_connections WHERE id = ?1;");
                statement.BindText(1, id.ToString("D"));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task DeleteRunsBeforeAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare("DELETE FROM runs WHERE completed_utc < ?1;");
                statement.BindText(1, FormatDate(cutoffUtc));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task<TaskExecutionOptions?> GetTaskExecutionOptionsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<TaskExecutionOptions?>(
            database =>
            {
                using var statement = database.Prepare(
                    "SELECT options_json FROM task_execution_options WHERE task_id = ?1;");
                statement.BindText(1, taskId.ToString("D"));
                return statement.Read()
                    ? JsonSerializer.Deserialize<TaskExecutionOptions>(statement.GetString(0), JsonOptions)
                    : null;
            },
            cancellationToken);

    public Task<IReadOnlyList<TaskExecutionOptions>> GetAllTaskExecutionOptionsAsync(
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<TaskExecutionOptions>>(
            database =>
            {
                using var statement = database.Prepare(
                    "SELECT options_json FROM task_execution_options ORDER BY task_id;");
                var options = new List<TaskExecutionOptions>();
                while (statement.Read())
                {
                    var item = JsonSerializer.Deserialize<TaskExecutionOptions>(statement.GetString(0), JsonOptions);
                    if (item is not null)
                    {
                        options.Add(item);
                    }
                }

                return options;
            },
            cancellationToken);

    public Task SaveTaskExecutionOptionsAsync(
        TaskExecutionOptions options,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    INSERT INTO task_execution_options(task_id, options_json)
                    VALUES(?1, ?2)
                    ON CONFLICT(task_id) DO UPDATE SET options_json = excluded.options_json;
                    """);
                var normalized = options.Normalize();
                statement.BindText(1, normalized.TaskId.ToString("D"));
                statement.BindText(2, JsonSerializer.Serialize(normalized, JsonOptions));
                statement.Execute();
                return true;
            },
            cancellationToken);

    public Task<RunHistoryEntry?> GetLatestRunForTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<RunHistoryEntry?>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT id, task_id, task_name, request_json, started_utc, completed_utc,
                           status, successful_files, skipped_files, failed_files, copied_bytes
                    FROM runs WHERE task_id = ?1
                    ORDER BY completed_utc DESC LIMIT 1;
                    """);
                statement.BindText(1, taskId.ToString("D"));
                return statement.Read() ? ReadRun(database, statement) : null;
            },
            cancellationToken);

    public Task<IReadOnlyList<IncompleteExecution>> GetIncompleteExecutionsAsync(
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<IncompleteExecution>>(
            database =>
            {
                using var statement = database.Prepare(
                    "SELECT id, task_id, request_json, plan_created_utc, started_utc FROM incomplete_executions ORDER BY started_utc;");
                var output = new List<IncompleteExecution>();
                while (statement.Read())
                {
                    output.Add(ReadIncompleteExecution(database, statement));
                }

                return output;
            }, cancellationToken);

    public Task<IncompleteExecution?> GetIncompleteExecutionAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IncompleteExecution?>(
            database =>
            {
                using var statement = database.Prepare(
                    "SELECT id, task_id, request_json, plan_created_utc, started_utc FROM incomplete_executions WHERE task_id = ?1;");
                statement.BindText(1, taskId.ToString("D"));
                return statement.Read() ? ReadIncompleteExecution(database, statement) : null;
            }, cancellationToken);

    public Task SaveIncompleteExecutionAsync(
        IncompleteExecution execution,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                database.Execute("BEGIN IMMEDIATE;");
                try
                {
                    using (var old = database.Prepare("DELETE FROM incomplete_executions WHERE task_id = ?1;"))
                    {
                        old.BindText(1, execution.TaskId.ToString("D"));
                        old.Execute();
                    }

                    using var statement = database.Prepare(
                        """
                        INSERT INTO incomplete_executions(id, task_id, request_json, plan_created_utc, started_utc)
                        VALUES(?1, ?2, ?3, ?4, ?5);
                        """);
                    statement.BindText(1, execution.Id.ToString("D"));
                    statement.BindText(2, execution.TaskId.ToString("D"));
                    statement.BindText(3, SerializeRequest(execution.Request));
                    statement.BindText(4, FormatDate(execution.PlanCreatedAtUtc));
                    statement.BindText(5, FormatDate(execution.StartedAtUtc));
                    statement.Execute();
                    database.Execute("COMMIT;");
                }
                catch
                {
                    try { database.Execute("ROLLBACK;"); } catch (InvalidOperationException) { }
                    throw;
                }

                return true;
            }, cancellationToken);

    public Task SaveIncompleteExecutionItemAsync(
        Guid executionId,
        IncompleteExecutionItem item,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    INSERT INTO incomplete_execution_items(
                        execution_id, relative_path, action, length, source_modified_utc, completed_utc)
                    VALUES(?1, ?2, ?3, ?4, ?5, ?6)
                    ON CONFLICT(execution_id, relative_path, action) DO UPDATE SET
                        length = excluded.length,
                        source_modified_utc = excluded.source_modified_utc,
                        completed_utc = excluded.completed_utc;
                    """);
                statement.BindText(1, executionId.ToString("D"));
                statement.BindText(2, item.RelativePath);
                statement.BindInt(3, (int)item.Action);
                statement.BindInt64(4, item.Length);
                statement.BindText(5, FormatDate(item.SourceLastWriteTimeUtc));
                statement.BindText(6, FormatDate(item.CompletedAtUtc));
                statement.Execute();
                return true;
            }, cancellationToken);

    public Task DeleteIncompleteExecutionAsync(Guid executionId, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare("DELETE FROM incomplete_executions WHERE id = ?1;");
                statement.BindText(1, executionId.ToString("D"));
                statement.Execute();
                return true;
            }, cancellationToken);

    public Task<IReadOnlyList<NotificationChannel>> GetNotificationChannelsAsync(
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<NotificationChannel>>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT id, name, kind, enabled, endpoint, user_name, credential_target,
                           sender, recipients, notify_success, notify_failure, updated_utc
                    FROM notification_channels ORDER BY name COLLATE NOCASE;
                    """);
                var channels = new List<NotificationChannel>();
                while (statement.Read())
                {
                    channels.Add(new NotificationChannel(
                        Guid.Parse(statement.GetString(0)), statement.GetString(1),
                        (NotificationChannelKind)statement.GetInt(2), statement.GetInt(3) != 0,
                        statement.GetString(4), statement.GetString(5), statement.GetString(6),
                        statement.GetString(7), statement.GetString(8),
                        statement.GetInt(9) != 0, statement.GetInt(10) != 0,
                        ParseDate(statement.GetString(11))));
                }

                return channels;
            }, cancellationToken);

    public Task SaveNotificationChannelAsync(
        NotificationChannel channel,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    INSERT INTO notification_channels(
                        id, name, kind, enabled, endpoint, user_name, credential_target,
                        sender, recipients, notify_success, notify_failure, updated_utc)
                    VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12)
                    ON CONFLICT(id) DO UPDATE SET
                        name=excluded.name, kind=excluded.kind, enabled=excluded.enabled,
                        endpoint=excluded.endpoint, user_name=excluded.user_name,
                        credential_target=excluded.credential_target, sender=excluded.sender,
                        recipients=excluded.recipients, notify_success=excluded.notify_success,
                        notify_failure=excluded.notify_failure, updated_utc=excluded.updated_utc;
                    """);
                statement.BindText(1, channel.Id.ToString("D"));
                statement.BindText(2, channel.Name);
                statement.BindInt(3, (int)channel.Kind);
                statement.BindInt(4, channel.Enabled ? 1 : 0);
                statement.BindText(5, channel.Endpoint);
                statement.BindText(6, channel.UserName);
                statement.BindText(7, channel.CredentialTarget);
                statement.BindText(8, channel.Sender);
                statement.BindText(9, channel.Recipients);
                statement.BindInt(10, channel.NotifyOnSuccess ? 1 : 0);
                statement.BindInt(11, channel.NotifyOnFailure ? 1 : 0);
                statement.BindText(12, FormatDate(channel.UpdatedAtUtc ?? DateTime.UtcNow));
                statement.Execute();
                return true;
            }, cancellationToken);

    public Task DeleteNotificationChannelAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                using var statement = database.Prepare("DELETE FROM notification_channels WHERE id=?1;");
                statement.BindText(1, id.ToString("D"));
                statement.Execute();
                return true;
            }, cancellationToken);

    public Task<IReadOnlyList<FilterRuleTemplate>> GetFilterRuleTemplatesAsync(CancellationToken cancellationToken = default) =>
        GetTemplatesAsync<FilterRuleTemplate>("filter_rule_templates", cancellationToken);

    public Task SaveFilterRuleTemplateAsync(FilterRuleTemplate template, CancellationToken cancellationToken = default) =>
        SaveTemplateAsync("filter_rule_templates", template.Id, template.Name, template.CreatedAtUtc,
            template.UpdatedAtUtc, JsonSerializer.Serialize(template, JsonOptions), cancellationToken);

    public Task DeleteFilterRuleTemplateAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteTemplateAsync("filter_rule_templates", id, cancellationToken);

    public Task<IReadOnlyList<TaskTemplate>> GetTaskTemplatesAsync(CancellationToken cancellationToken = default) =>
        GetTemplatesAsync<TaskTemplate>("task_templates", cancellationToken);

    public Task SaveTaskTemplateAsync(TaskTemplate template, CancellationToken cancellationToken = default) =>
        SaveTemplateAsync("task_templates", template.Id, template.Name, template.CreatedAtUtc,
            template.UpdatedAtUtc, JsonSerializer.Serialize(template, JsonOptions), cancellationToken);

    public Task DeleteTaskTemplateAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteTemplateAsync("task_templates", id, cancellationToken);

    public Task ClearConfigurationAsync(CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                database.Execute("BEGIN IMMEDIATE;");
                try
                {
                    database.Execute("DELETE FROM sync_snapshot;");
                    database.Execute("DELETE FROM saved_tasks;");
                    database.Execute("DELETE FROM storage_connections;");
                    database.Execute("DELETE FROM notification_channels;");
                    database.Execute("DELETE FROM filter_rule_templates;");
                    database.Execute("DELETE FROM task_templates;");
                    database.Execute("COMMIT;");
                }
                catch
                {
                    try { database.Execute("ROLLBACK;"); } catch (InvalidOperationException) { }
                    throw;
                }
                return true;
            }, cancellationToken);

    private Task<IReadOnlyList<T>> GetTemplatesAsync<T>(string table, CancellationToken cancellationToken) =>
        WithDatabaseAsync<IReadOnlyList<T>>(
            database =>
            {
                using var statement = database.Prepare($"SELECT template_json FROM {table} ORDER BY name COLLATE NOCASE;");
                var output = new List<T>();
                while (statement.Read())
                {
                    var value = JsonSerializer.Deserialize<T>(statement.GetString(0), JsonOptions);
                    if (value is not null) output.Add(value);
                }
                return output;
            }, cancellationToken);

    private Task SaveTemplateAsync(
        string table, Guid id, string name, DateTime createdAtUtc, DateTime updatedAtUtc,
        string json, CancellationToken cancellationToken) =>
        WithDatabaseAsync(database =>
        {
            using var statement = database.Prepare(
                $"INSERT INTO {table}(id,name,template_json,created_utc,updated_utc) VALUES(?1,?2,?3,?4,?5) "
                + "ON CONFLICT(id) DO UPDATE SET name=excluded.name, template_json=excluded.template_json, updated_utc=excluded.updated_utc;");
            statement.BindText(1, id.ToString("D")); statement.BindText(2, name);
            statement.BindText(3, json); statement.BindText(4, FormatDate(createdAtUtc));
            statement.BindText(5, FormatDate(updatedAtUtc)); statement.Execute(); return true;
        }, cancellationToken);

    private Task DeleteTemplateAsync(string table, Guid id, CancellationToken cancellationToken) =>
        WithDatabaseAsync(database =>
        {
            using var statement = database.Prepare($"DELETE FROM {table} WHERE id=?1;");
            statement.BindText(1, id.ToString("D")); statement.Execute(); return true;
        }, cancellationToken);

    public Task<IReadOnlyList<SyncFileState>> GetSnapshotAsync(
        Guid synchronizationId,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync<IReadOnlyList<SyncFileState>>(
            database =>
            {
                using var statement = database.Prepare(
                    """
                    SELECT relative_path, source_exists, source_length, source_modified_utc,
                           target_exists, target_length, target_modified_utc, source_hash, target_hash
                    FROM sync_snapshot
                    WHERE synchronization_id = ?1
                    ORDER BY relative_path COLLATE NOCASE;
                    """);
                statement.BindText(1, synchronizationId.ToString("D"));
                var states = new List<SyncFileState>();
                while (statement.Read())
                {
                    var sourceModified = statement.GetNullableString(3);
                    var targetModified = statement.GetNullableString(6);
                    states.Add(new SyncFileState(
                        statement.GetString(0),
                        statement.GetInt(1) != 0,
                        statement.GetNullableInt64(2),
                        sourceModified is null ? null : ParseDate(sourceModified),
                        statement.GetInt(4) != 0,
                        statement.GetNullableInt64(5),
                        targetModified is null ? null : ParseDate(targetModified),
                        statement.GetNullableString(7),
                        statement.GetNullableString(8)));
                }

                return states;
            },
            cancellationToken);

    public Task ReplaceSnapshotAsync(
        Guid synchronizationId,
        IReadOnlyList<SyncFileState> states,
        CancellationToken cancellationToken = default) =>
        WithDatabaseAsync(
            database =>
            {
                database.Execute("BEGIN IMMEDIATE;");
                try
                {
                    using (var delete = database.Prepare(
                               "DELETE FROM sync_snapshot WHERE synchronization_id = ?1;"))
                    {
                        delete.BindText(1, synchronizationId.ToString("D"));
                        delete.Execute();
                    }

                    using var insert = database.Prepare(
                        """
                        INSERT INTO sync_snapshot(
                            synchronization_id, relative_path, source_exists, source_length,
                            source_modified_utc, target_exists, target_length, target_modified_utc,
                            source_hash, target_hash)
                        VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10);
                        """);
                    foreach (var state in states)
                    {
                        insert.BindText(1, synchronizationId.ToString("D"));
                        insert.BindText(2, state.RelativePath);
                        insert.BindInt(3, state.SourceExists ? 1 : 0);
                        insert.BindNullableInt64(4, state.SourceLength);
                        insert.BindText(5, state.SourceLastWriteTimeUtc is null
                            ? null
                            : FormatDate(state.SourceLastWriteTimeUtc.Value));
                        insert.BindInt(6, state.TargetExists ? 1 : 0);
                        insert.BindNullableInt64(7, state.TargetLength);
                        insert.BindText(8, state.TargetLastWriteTimeUtc is null
                            ? null
                            : FormatDate(state.TargetLastWriteTimeUtc.Value));
                        insert.BindText(9, state.SourceContentHash);
                        insert.BindText(10, state.TargetContentHash);
                        insert.Execute();
                        insert.Reset();
                    }

                    database.Execute("COMMIT;");
                }
                catch
                {
                    try
                    {
                        database.Execute("ROLLBACK;");
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    throw;
                }

                return true;
            },
            cancellationToken);

    private static void EnsureColumn(SqliteDatabase database, string table, string column, string declaration)
    {
        using var statement = database.Prepare($"PRAGMA table_info({table});");
        while (statement.Read())
            if (string.Equals(statement.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        database.Execute($"ALTER TABLE {table} ADD COLUMN {column} {declaration};");
    }

    private async Task<T> WithDatabaseAsync<T>(
        Func<SqliteDatabase, T> action,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var database = new SqliteDatabase(_databasePath);
                    database.Execute("PRAGMA foreign_keys=ON;");
                    database.Execute("PRAGMA busy_timeout=5000;");
                    return action(database);
                },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static TaskSchedule ReadSchedule(SqliteStatement statement)
    {
        var intervalTicks = statement.GetNullableInt64(6);
        var lastTriggered = statement.GetNullableString(10);
        var nextRun = statement.GetNullableString(11);
        var conditionsJson = statement.GetNullableString(12);
        var windowJson = statement.GetNullableString(13);
        return new TaskSchedule(
            Guid.Parse(statement.GetString(0)),
            statement.GetInt(1) != 0,
            (ScheduleTrigger)statement.GetInt(2),
            TimeSpan.FromTicks(statement.GetInt64(3)),
            (DayOfWeek)statement.GetInt(4),
            statement.GetInt(5),
            intervalTicks is null ? null : TimeSpan.FromTicks(intervalTicks.Value),
            (MissedRunPolicy)statement.GetInt(7),
            statement.GetInt(8) != 0,
            statement.GetInt(9) != 0,
            lastTriggered is null ? null : ParseDate(lastTriggered),
            nextRun is null ? null : ParseDate(nextRun),
            DeserializeConditions(conditionsJson),
            windowJson is null ? null : JsonSerializer.Deserialize<TimeWindow>(windowJson, JsonOptions));
    }

    private static IncompleteExecution ReadIncompleteExecution(
        SqliteDatabase database,
        SqliteStatement statement)
    {
        var id = Guid.Parse(statement.GetString(0));
        using var itemsStatement = database.Prepare(
            """
            SELECT relative_path, action, length, source_modified_utc, completed_utc
            FROM incomplete_execution_items WHERE execution_id = ?1
            ORDER BY completed_utc, relative_path COLLATE NOCASE;
            """);
        itemsStatement.BindText(1, id.ToString("D"));
        var items = new List<IncompleteExecutionItem>();
        while (itemsStatement.Read())
        {
            items.Add(new IncompleteExecutionItem(
                itemsStatement.GetString(0),
                (CopyAction)itemsStatement.GetInt(1),
                itemsStatement.GetInt64(2),
                ParseDate(itemsStatement.GetString(3)),
                ParseDate(itemsStatement.GetString(4))));
        }

        return new IncompleteExecution(
            id,
            Guid.Parse(statement.GetString(1)),
            DeserializeRequest(statement.GetString(2)),
            ParseDate(statement.GetString(3)),
            ParseDate(statement.GetString(4)),
            items);
    }

    private static SavedCopyTask ReadTask(SqliteStatement statement) =>
        new(
            Guid.Parse(statement.GetString(0)),
            statement.GetString(1),
            DeserializeRequest(statement.GetString(2)),
            ParseDate(statement.GetString(3)),
            ParseDate(statement.GetString(4)));

    private static void InsertRun(SqliteDatabase database, RunHistoryEntry run)
    {
        using var statement = database.Prepare(
            """
            INSERT INTO runs(
                id, task_id, task_name, request_json, started_utc, completed_utc, status,
                successful_files, skipped_files, failed_files, copied_bytes)
            VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11);
            """);
        statement.BindText(1, run.Id.ToString("D"));
        statement.BindText(2, run.TaskId?.ToString("D"));
        statement.BindText(3, run.TaskName);
        statement.BindText(4, SerializeRequest(run.Request));
        statement.BindText(5, FormatDate(run.StartedAtUtc));
        statement.BindText(6, FormatDate(run.CompletedAtUtc));
        statement.BindText(7, run.Status);
        statement.BindInt(8, run.SuccessfulFiles);
        statement.BindInt(9, run.SkippedFiles);
        statement.BindInt(10, run.FailedFiles);
        statement.BindInt64(11, run.CopiedBytes);
        statement.Execute();
    }

    private static void InsertRunItems(SqliteDatabase database, RunHistoryEntry run)
    {
        using var statement = database.Prepare(
            """
            INSERT INTO run_items(
                run_id, ordinal, relative_path, action, result, bytes, error_message, attempts)
            VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8);
            """);

        for (var index = 0; index < run.Items.Count; index++)
        {
            var item = run.Items[index];
            statement.BindText(1, run.Id.ToString("D"));
            statement.BindInt(2, index);
            statement.BindText(3, item.RelativePath);
            statement.BindInt(4, (int)item.Action);
            statement.BindText(5, item.Result);
            statement.BindInt64(6, item.Bytes);
            statement.BindText(7, item.ErrorMessage);
            statement.BindInt(8, item.Attempts);
            statement.Execute();
            statement.Reset();
        }
    }

    private static void InsertQuarantineEntries(SqliteDatabase database, RunHistoryEntry run)
    {
        using var statement = database.Prepare(
            """
            INSERT INTO quarantine_entries(
                id, run_id, original_path, quarantine_path, relative_path,
                length, quarantined_utc, restored_utc)
            VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8);
            """);

        foreach (var entry in run.EffectiveQuarantineEntries)
        {
            statement.BindText(1, entry.Id.ToString("D"));
            statement.BindText(2, run.Id.ToString("D"));
            statement.BindText(3, entry.OriginalPath);
            statement.BindText(4, entry.QuarantinePath);
            statement.BindText(5, entry.RelativePath);
            statement.BindInt64(6, entry.Length);
            statement.BindText(7, FormatDate(entry.QuarantinedAtUtc));
            statement.BindText(8, entry.RestoredAtUtc is null ? null : FormatDate(entry.RestoredAtUtc.Value));
            statement.Execute();
            statement.Reset();
        }
    }

    private static RunHistoryEntry ReadRun(SqliteDatabase database, SqliteStatement statement)
    {
        var id = Guid.Parse(statement.GetString(0));
        var taskIdText = statement.GetNullableString(1);
        return new RunHistoryEntry(
            id,
            taskIdText is null ? null : Guid.Parse(taskIdText),
            statement.GetString(2),
            DeserializeRequest(statement.GetString(3)),
            ParseDate(statement.GetString(4)),
            ParseDate(statement.GetString(5)),
            statement.GetString(6),
            statement.GetInt(7),
            statement.GetInt(8),
            statement.GetInt(9),
            statement.GetInt64(10),
            ReadRunItems(database, id),
            ReadRunQuarantineEntries(database, id));
    }

    private static IReadOnlyList<RunHistoryItem> ReadRunItems(SqliteDatabase database, Guid runId)
    {
        using var statement = database.Prepare(
            """
            SELECT relative_path, action, result, bytes, error_message, attempts
            FROM run_items WHERE run_id = ?1 ORDER BY ordinal;
            """);
        statement.BindText(1, runId.ToString("D"));
        var items = new List<RunHistoryItem>();
        while (statement.Read())
        {
            items.Add(new RunHistoryItem(
                statement.GetString(0),
                (CopyAction)statement.GetInt(1),
                statement.GetString(2),
                statement.GetInt64(3),
                statement.GetNullableString(4),
                statement.GetInt(5)));
        }

        return items;
    }

    private static IReadOnlyList<QuarantineEntry> ReadRunQuarantineEntries(
        SqliteDatabase database,
        Guid runId)
    {
        using var statement = database.Prepare(
            """
            SELECT id, run_id, original_path, quarantine_path, relative_path, length,
                   quarantined_utc, restored_utc
            FROM quarantine_entries WHERE run_id = ?1 ORDER BY quarantined_utc;
            """);
        statement.BindText(1, runId.ToString("D"));
        var entries = new List<QuarantineEntry>();
        while (statement.Read())
        {
            entries.Add(ReadQuarantineEntry(statement));
        }

        return entries;
    }

    private static QuarantineEntry ReadQuarantineEntry(SqliteStatement statement)
    {
        var restoredText = statement.GetNullableString(7);
        return new QuarantineEntry(
            Guid.Parse(statement.GetString(0)),
            Guid.Parse(statement.GetString(1)),
            statement.GetString(2),
            statement.GetString(3),
            statement.GetString(4),
            statement.GetInt64(5),
            ParseDate(statement.GetString(6)),
            restoredText is null ? null : ParseDate(restoredText));
    }

    private static string SerializeRequest(CopyTaskRequest request) =>
        JsonSerializer.Serialize(request, JsonOptions);

    private static CopyTaskRequest DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<CopyTaskRequest>(json, JsonOptions)
        ?? throw new InvalidOperationException("数据库中的任务配置无法解析。");

    private static string SerializeConditions(IReadOnlyList<RunCondition> conditions) =>
        JsonSerializer.Serialize(conditions, JsonOptions);

    private static IReadOnlyList<RunCondition>? DeserializeConditions(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<List<RunCondition>>(json, JsonOptions);

    private static string FormatDate(DateTime value) => value.ToUniversalTime().ToString("O");

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
}
