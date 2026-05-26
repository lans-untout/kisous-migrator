using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Kisous.Migrator.Abstractions;
using Kisous.Migrator.Models;

namespace Kisous.Migrator.SqlServer;

public sealed class SqlServerDatabaseProvider : IDatabaseProvider, IDisposable, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private static readonly StringComparison SqlComparison = StringComparison.OrdinalIgnoreCase;

    public SqlServerDatabaseProvider(IOptions<MigratorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connection = new SqlConnection(options.Value.ConnectionString);
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
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[_SchemaMigrationJournal]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[_SchemaMigrationJournal] (
        [id] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [MigrationId] NVARCHAR(255) NOT NULL,
        [MigrationDirection] NVARCHAR(10) NOT NULL CHECK ([MigrationDirection] IN ('up', 'down')),
        [ExecutionMode] NVARCHAR(20) NOT NULL CHECK ([ExecutionMode] IN ('execute', 'baseline')),
        [ScriptName] NVARCHAR(255) NULL,
        [ScriptChecksum] NVARCHAR(128) NULL,
        [PackageVersion] NVARCHAR(128) NULL,
        [AppliedBy] NVARCHAR(255) NOT NULL,
        [AppliedAtUtc] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        [ExecutionDurationMs] BIGINT NOT NULL DEFAULT 0
    );

    CREATE NONCLUSTERED INDEX [IDX__SchemaMigrationJournal_MigrationId]
        ON [dbo].[_SchemaMigrationJournal] ([MigrationId], [id] DESC);
END";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MigrationJournalEntry>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectionAsync(cancellationToken);
        
        await using var existsCmd = _connection.CreateCommand();
        existsCmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[_SchemaMigrationJournal]') AND type in (N'U')
) THEN 1 ELSE 0 END";

        var exists = (int)(await existsCmd.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
        if (!exists)
        {
            return Array.Empty<MigrationJournalEntry>();
        }

        var entries = new List<MigrationJournalEntry>();
        await using var command = _connection.CreateCommand();
        command.CommandText = @"
SELECT [id], [MigrationId], [MigrationDirection], [ExecutionMode], 
       ISNULL([ScriptName], ''), ISNULL([ScriptChecksum], ''), ISNULL([PackageVersion], ''), 
       [AppliedBy], [AppliedAtUtc], [ExecutionDurationMs]
FROM [dbo].[_SchemaMigrationJournal]
ORDER BY [id];";

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
        
        // SQL Server online index builds or full text catalogs typically are the things that can't run in a trans, 
        // but typically DDL is transactional in SQL Server.
        // For simplicity we will wrap everything in a transaction unless it explicitly drops a database etc
        // Actually, we'll try a local transaction.
        await using SqlTransaction transaction = (SqlTransaction)await _connection.BeginTransactionAsync(cancellationToken);

        if (step.ExecutionMode == MigrationExecutionMode.Execute && !string.IsNullOrWhiteSpace(step.ScriptContent))
        {
            // Note: SqlClient does not natively handle GO batches. Real implementation might need to split by "GO"
            // For now, assume script is a single batch or GO is processed beforehand.
            await using var scriptCommand = _connection.CreateCommand();
            scriptCommand.Transaction = transaction;
            scriptCommand.CommandText = step.ScriptContent;
            
            // To handle basic batch timeout. We can bump this up if needed.
            scriptCommand.CommandTimeout = 0; 
            await scriptCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        stopwatch.Stop();

        await using var appendCommand = _connection.CreateCommand();
        appendCommand.Transaction = transaction;
        appendCommand.CommandText = @"
INSERT INTO [dbo].[_SchemaMigrationJournal] (
    [MigrationId], [MigrationDirection], [ExecutionMode], [ScriptName], 
    [ScriptChecksum], [PackageVersion], [AppliedBy], [ExecutionDurationMs])
VALUES (
    @MigrationId, @MigrationDirection, @ExecutionMode, @ScriptName, 
    @ScriptChecksum, @PackageVersion, @AppliedBy, @ExecutionDurationMs);";

        appendCommand.Parameters.AddWithValue("MigrationId", step.MigrationId);
        appendCommand.Parameters.AddWithValue("MigrationDirection", step.Direction == MigrationDirection.Up ? "up" : "down");
        appendCommand.Parameters.AddWithValue("ExecutionMode", step.ExecutionMode == MigrationExecutionMode.Execute ? "execute" : "baseline");
        appendCommand.Parameters.AddWithValue("ScriptName", (object)step.ScriptName ?? DBNull.Value);
        appendCommand.Parameters.AddWithValue("ScriptChecksum", (object)step.ScriptChecksum ?? DBNull.Value);
        appendCommand.Parameters.AddWithValue("PackageVersion", (object)packageVersion ?? DBNull.Value);
        appendCommand.Parameters.AddWithValue("AppliedBy", appliedBy);
        appendCommand.Parameters.AddWithValue("ExecutionDurationMs", stopwatch.ElapsedMilliseconds);

        await appendCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public void Dispose() => _connection?.Dispose();
    public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? default;
}