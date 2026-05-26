using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Kisous.Migrator.Models;

[ExcludeFromCodeCoverage]
public sealed class MigrationPlan
{
    public MigrationPlan(
        string currentMigrationId,
        string targetMigrationId,
        IReadOnlyList<MigrationPlanStep> steps)
    {
        CurrentMigrationId = currentMigrationId;
        TargetMigrationId = targetMigrationId;
        Steps = steps;
    }

    public string CurrentMigrationId { get; }

    public string TargetMigrationId { get; }

    public IReadOnlyList<MigrationPlanStep> Steps { get; }

    public bool HasChanges => Steps.Count > 0;

    public bool IsRollbackOnly => Steps.Count > 0 && Steps.All(step => step.Direction == MigrationDirection.Down);

    public bool IsBaselineOnly => Steps.Count > 0 && Steps.All(step => step.ExecutionMode == MigrationExecutionMode.Baseline);
}
