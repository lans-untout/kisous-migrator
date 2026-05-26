using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using Kisous.Migrator.Abstractions;
using Kisous.Migrator.Models;

namespace Kisous.Migrator.PostgreSql;

public sealed class PostgreSqlDatabaseProvider : IDatabaseProvider, IDisposable, IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private static readonly StringComparison SqlComparison = StringComparison.OrdinalIgnoreCase;

    public PostgreSqlDatabaseProvider(IOptions<MigratorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connection = new NpgsqlConnection(options.Value.ConnectionString);
    }

    private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
    }

    public async Task EnsureJournalTableAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectionAsync(cancellationToken);
        await using var command = _connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS public._schema_migration_journal (
    id BIGSERIAL PRIMARY KEY,
    migration_id VARCHAR(255) NOT NULL,
    migration_direction VARCHAR(10) NOT NULL CHECK (migration_direction IN ('up', 'down')),
    execution_mode VARCHAR(20) NOT NULL CHECK (execution_mode IN ('execute', 'baseline')),
    script_name VARCHAR(255),
    script_checksum VARCHAR(128),
    package_version VARCHAR(128),
    applied_by VARCHAR(255) NOT NULL,
    applied_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    execution_duration_ms BIGINT NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx__schema_migration_journal_migration_id
    ON public._schema_migration_journal (migration_id, id DESC);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MigrationJournalEntry>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectionAsync(cancellationToken);
        
        await using var existsCmd = _connection.CreateCommand();
        existsCmd.CommandText = @"
SELECT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_schema = 'public'
      AND table_name = '_schema_migration_journal');";

        var exists = (bool)(await existsCmd.ExecuteScalarAsync(cancellationToken) ?? false);
        if (!exists)
        {
            return Array.Empty<MigrationJournalEntry>();
        }

        var entries = new List<MigrationJournalEntry>();
        await using var command = _connection.CreateCommand();
        command.CommandText = @"
SELECT id, migration_id, migration_direction, execution_mode, 
       COALESCE(script_name, ''), COALESCE(script_checksum, ''), COALESCE(package_version, ''), 
       applied_by, applied_at_utc, execution_duration_ms
FROM public._schema_migration_journal
ORDER BY id;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new MigrationJournalEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "down", StringComparison.OrdinalIgnoreCase) ? MigrationDirection.Down : MigrationDirection.Up,
                string.Equals(reader.GetString(3), "baseline", StringComparison.OrdinalIgnoreCase) ? MigrationExecutionMode.Baseline : MigrationExecutionMode.Execute,
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetInt64(9)));
        }

        return entries;
    }

    public async Task ExecuteStepAsync(MigrationPlanStep step, string packageVersion, string appliedBy, CancellationToken cancellationToken)
    {
        await EnsureConnectionAsync(cancellationToken);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var executeWithoutTransaction = RequiresOutOfTransactionExecution(step);
        
        await using var transaction = executeWithoutTransaction 
            ? null 
            : await _connection.BeginTransactionAsync(cancellationToken);

        if (step.ExecutionMode == MigrationExecutionMode.Execute && !string.IsNullOrWhiteSpace(step.ScriptContent))
        {
            await using var scriptCommand = _connection.CreateCommand();
            scriptCommand.Transaction = transaction;
            scriptCommand.CommandText = step.ScriptContent;
            await scriptCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        stopwatch.Stop();

        await using var appendCommand = _connection.CreateCommand();
        appendCommand.Transaction = transaction;
        appendCommand.CommandText = @"
INSERT INTO public._schema_migration_journal (
    migration_id, migration_direction, execution_mode, script_name, 
    script_checksum, package_version, applied_by, execution_duration_ms)
VALUES (
    @migration_id, @migration_direction, @execution_mode, @script_name, 
    @script_checksum, @package_version, @applied_by, @execution_duration_ms);";

        appendCommand.Parameters.AddWithValue("migration_id", step.MigrationId);
        appendCommand.Parameters.AddWithValue("migration_direction", step.Direction == MigrationDirection.Up ? "up" : "down");
        appendCommand.Parameters.AddWithValue("execution_mode", step.ExecutionMode == MigrationExecutionMode.Execute ? "execute" : "baseline");
        appendCommand.Parameters.AddWithValue("script_name", (object)step.ScriptName ?? DBNull.Value);
        appendCommand.Parameters.AddWithValue("script_checksum", (object)step.ScriptChecksum ?? DBNull.Value);
        appendCommand.Parameters.AddWithValue("package_version", (object)packageVersion ?? DBNull.Value);
        appendCommand.Parameters.AddWithValue("applied_by", appliedBy);
        appendCommand.Parameters.AddWithValue("execution_duration_ms", stopwatch.ElapsedMilliseconds);

        await appendCommand.ExecuteNonQueryAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static bool RequiresOutOfTransactionExecution(MigrationPlanStep step)
    {
        if (step.ExecutionMode != MigrationExecutionMode.Execute || string.IsNullOrWhiteSpace(step.ScriptContent))
            return false;

        return step.ScriptContent.Contains("CREATE INDEX CONCURRENTLY", SqlComparison)
            || step.ScriptContent.Contains("DROP INDEX CONCURRENTLY", SqlComparison)
            || step.ScriptContent.Contains("REINDEX INDEX CONCURRENTLY", SqlComparison)
            || step.ScriptContent.Contains("REINDEX TABLE CONCURRENTLY", SqlComparison)
            || step.ScriptContent.Contains("REINDEX SCHEMA CONCURRENTLY", SqlComparison)
            || step.ScriptContent.Contains("REINDEX DATABASE CONCURRENTLY", SqlComparison);
    }

    public void Dispose() => _connection?.Dispose();
    public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? default;
}