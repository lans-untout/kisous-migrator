namespace Kisous.Migrator.Models;

public sealed class MigrationPlanStep
{
    public MigrationPlanStep(
        string migrationId,
        MigrationDirection direction,
        MigrationExecutionMode executionMode,
        string scriptName,
        string scriptContent,
        string scriptChecksum)
    {
        MigrationId = migrationId;
        Direction = direction;
        ExecutionMode = executionMode;
        ScriptName = scriptName;
        ScriptContent = scriptContent;
        ScriptChecksum = scriptChecksum;
    }

    public string MigrationId { get; }

    public MigrationDirection Direction { get; }

    public MigrationExecutionMode ExecutionMode { get; }

    public string ScriptName { get; }

    public string ScriptContent { get; }

    public string ScriptChecksum { get; }

    public string DisplayAction => ExecutionMode == MigrationExecutionMode.Baseline
        ? "baseline"
        : Direction == MigrationDirection.Up ? "apply" : "rollback";
}
