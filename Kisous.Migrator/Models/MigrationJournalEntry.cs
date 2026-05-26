using System;

namespace Kisous.Migrator.Models;

public sealed class MigrationJournalEntry
{
    public MigrationJournalEntry(
        long journalId,
        string migrationId,
        MigrationDirection direction,
        MigrationExecutionMode executionMode,
        string scriptName,
        string scriptChecksum,
        string packageVersion,
        string appliedBy,
        DateTimeOffset appliedAtUtc,
        long executionDurationMs)
    {
        JournalId = journalId;
        MigrationId = migrationId;
        Direction = direction;
        ExecutionMode = executionMode;
        ScriptName = scriptName;
        ScriptChecksum = scriptChecksum;
        PackageVersion = packageVersion;
        AppliedBy = appliedBy;
        AppliedAtUtc = appliedAtUtc;
        ExecutionDurationMs = executionDurationMs;
    }

    public long JournalId { get; }

    public string MigrationId { get; }

    public MigrationDirection Direction { get; }

    public MigrationExecutionMode ExecutionMode { get; }

    public string ScriptName { get; }

    public string ScriptChecksum { get; }

    public string PackageVersion { get; }

    public string AppliedBy { get; }

    public DateTimeOffset AppliedAtUtc { get; }

    public long ExecutionDurationMs { get; }
}
