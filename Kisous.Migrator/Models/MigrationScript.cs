using System.Diagnostics.CodeAnalysis;

namespace Kisous.Migrator.Models;

[ExcludeFromCodeCoverage]
public sealed class MigrationScript
{
    public MigrationScript(
        string migrationId,
        string forwardPath,
        string forwardScriptName,
        string forwardContent,
        string forwardChecksum,
        string rollbackPath,
        string rollbackScriptName,
        string rollbackContent,
        string rollbackChecksum)
    {
        MigrationId = migrationId;
        ForwardPath = forwardPath;
        ForwardScriptName = forwardScriptName;
        ForwardContent = forwardContent;
        ForwardChecksum = forwardChecksum;
        RollbackPath = rollbackPath;
        RollbackScriptName = rollbackScriptName;
        RollbackContent = rollbackContent;
        RollbackChecksum = rollbackChecksum;
    }

    public string MigrationId { get; }

    public string ForwardPath { get; }

    public string ForwardScriptName { get; }

    public string ForwardContent { get; }

    public string ForwardChecksum { get; }

    public string RollbackPath { get; }

    public string RollbackScriptName { get; }

    public string RollbackContent { get; }

    public string RollbackChecksum { get; }

    public bool HasRollback => !string.IsNullOrWhiteSpace(RollbackPath);
}
