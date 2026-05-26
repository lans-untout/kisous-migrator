using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Kisous.Migrator.Models;
using Kisous.Migrator.Services;
using NUnit.Framework;

namespace Kisous.Migrator.Tests;

[TestFixture]
public class MigrationPlannerTests
{
    private const string BootstrapId = "20260101_000000_initial_schema";
    private readonly MigrationPlanner _planner = new();

    [Test]
    public void BuildPlan_ShouldThrow_WhenScriptsIsNull()
    {
        Action act = () => _planner.BuildPlan(null!, Array.Empty<MigrationJournalEntry>(), MigratorOptions.LatestTarget);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void BuildPlan_ShouldThrow_WhenJournalIsNull()
    {
        Action act = () => _planner.BuildPlan(Array.Empty<MigrationScript>(), null!, MigratorOptions.LatestTarget);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void BuildPlan_ShouldReturnEmptyPlan_WhenNoScripts()
    {
        var plan = _planner.BuildPlan(Array.Empty<MigrationScript>(), Array.Empty<MigrationJournalEntry>(), MigratorOptions.LatestTarget);

        plan.HasChanges.Should().BeFalse();
        plan.Steps.Should().BeEmpty();
        plan.CurrentMigrationId.Should().Be(MigratorOptions.EmptyTarget);
        plan.TargetMigrationId.Should().Be(MigratorOptions.EmptyTarget);
    }

    [Test]
    public void BuildPlan_ShouldApplyAll_WhenEmptyStateAndLatest()
    {
        var scripts = new[]
        {
            Script("20260101_000001_a"),
            Script("20260101_000002_b")
        };

        var plan = _planner.BuildPlan(scripts, Array.Empty<MigrationJournalEntry>(), "  ");

        plan.HasChanges.Should().BeTrue();
        plan.Steps.Should().HaveCount(2);
        plan.Steps.Select(s => s.Direction).Should().OnlyContain(d => d == MigrationDirection.Up);
        plan.Steps.Select(s => s.ExecutionMode).Should().OnlyContain(m => m == MigrationExecutionMode.Execute);
        plan.CurrentMigrationId.Should().Be(MigratorOptions.EmptyTarget);
        plan.TargetMigrationId.Should().Be("20260101_000002_b");
    }

    [Test]
    public void BuildPlan_ShouldReturnNoSteps_WhenAlreadyAtTarget()
    {
        var script = Script("20260101_000001_a");
        var journal = new[] { Entry(1, script.MigrationId, MigrationDirection.Up, script.ForwardChecksum) };

        var plan = _planner.BuildPlan(new[] { script }, journal, MigratorOptions.LatestTarget);

        plan.HasChanges.Should().BeFalse();
        plan.Steps.Should().BeEmpty();
    }

    [Test]
    public void BuildPlan_ShouldThrow_WhenTargetUnknown()
    {
        var scripts = new[] { Script("20260101_000001_a") };

        Action act = () => _planner.BuildPlan(scripts, Array.Empty<MigrationJournalEntry>(), "20260101_999999_missing");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown migration target*");
    }

    [Test]
    public void BuildPlan_ShouldRollback_WhenTargetIsEmpty()
    {
        var s1 = Script("20260101_000001_a");
        var s2 = Script("20260101_000002_b", withRollback: true);
        var scripts = new[] { s1, s2 };
        var journal = new[]
        {
            Entry(1, s1.MigrationId, MigrationDirection.Up, s1.ForwardChecksum),
            Entry(2, s2.MigrationId, MigrationDirection.Up, s2.ForwardChecksum)
        };

        var plan = _planner.BuildPlan(scripts, journal, MigratorOptions.EmptyTarget);

        plan.IsRollbackOnly.Should().BeTrue();
        plan.Steps.Should().HaveCount(2);
        plan.Steps[0].MigrationId.Should().Be(s2.MigrationId);
        plan.Steps[1].MigrationId.Should().Be(s1.MigrationId);
    }

    [Test]
    public void BuildPlan_ShouldThrow_WhenRollbackScriptMissing()
    {
        var s1 = Script("20260101_000001_a");
        var s2 = Script("20260101_000002_b", withRollback: false);
        var scripts = new[] { s1, s2 };
        var journal = new[]
        {
            Entry(1, s1.MigrationId, MigrationDirection.Up, s1.ForwardChecksum),
            Entry(2, s2.MigrationId, MigrationDirection.Up, s2.ForwardChecksum)
        };

        Action act = () => _planner.BuildPlan(scripts, journal, MigratorOptions.EmptyTarget);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot roll back migration*");
    }

    [Test]
    public void BuildPlan_ShouldThrow_WhenJournalContainsUnknownCurrentEpochMigration()
    {
        var scripts = new[] { Script("20260101_000001_a") };
        var journal = new[] { Entry(1, "20260101_000099_missing", MigrationDirection.Up, "X") };

        Action act = () => _planner.BuildPlan(scripts, journal, MigratorOptions.LatestTarget);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no script with that ID exists*");
    }

    [Test]
    public void BuildPlan_ShouldIgnorePreConsolidationUnknownJournalEntries()
    {
        var scripts = new[]
        {
            Script(BootstrapId),
            Script("20260101_000001_a")
        };
        var journal = new[] { Entry(1, "20231231_235959_legacy", MigrationDirection.Up, "X") };

        var plan = _planner.BuildPlan(scripts, journal, MigratorOptions.LatestTarget);

        plan.HasChanges.Should().BeTrue();
        plan.Steps.Should().ContainSingle(s => s.MigrationId == "20260101_000001_a");
    }

    [Test]
    public void BuildPlan_ShouldThrow_WhenChecksumMismatches()
    {
        var script = Script("20260101_000001_a", forwardContent: "SELECT 1;");
        var journal = new[] { Entry(1, script.MigrationId, MigrationDirection.Up, "NOT_A_REAL_CHECKSUM") };

        Action act = () => _planner.BuildPlan(new[] { script }, journal, MigratorOptions.LatestTarget);

        act.Should().Throw<InvalidOperationException>().WithMessage("*checksum mismatch*");
    }

    [Test]
    public void BuildPlan_ShouldAcceptLegacyBudgetChecksumVariant()
    {
        const string migrationId = "20260428_000002_add_offline_budgets";
        const string content = "UPDATE t SET txt = '\u2192';";
        var script = Script(migrationId, forwardContent: content);
        var legacyChecksum = ComputeChecksum(content.Replace("\u2192", "->", StringComparison.Ordinal));
        var journal = new[] { Entry(1, migrationId, MigrationDirection.Up, legacyChecksum) };

        var plan = _planner.BuildPlan(new[] { script }, journal, MigratorOptions.LatestTarget);

        plan.HasChanges.Should().BeFalse();
    }

    [Test]
    public void BuildPlan_ShouldThrow_WhenEffectiveStateIsNotPrefix()
    {
        var s1 = Script("20260101_000001_a");
        var s2 = Script("20260101_000002_b");
        var s3 = Script("20260101_000003_c");
        var journal = new[]
        {
            Entry(1, s1.MigrationId, MigrationDirection.Up, s1.ForwardChecksum),
            Entry(2, s3.MigrationId, MigrationDirection.Up, s3.ForwardChecksum)
        };

        Action act = () => _planner.BuildPlan(new[] { s1, s2, s3 }, journal, MigratorOptions.LatestTarget);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not a prefix*");
    }

    [Test]
    public void BuildBaselinePlan_ShouldThrow_WhenJournalNotEmpty()
    {
        var script = Script("20260101_000001_a");
        var journal = new[] { Entry(1, script.MigrationId, MigrationDirection.Up, script.ForwardChecksum) };

        Action act = () => _planner.BuildBaselinePlan(new[] { script }, journal, MigratorOptions.LatestTarget);

        act.Should().Throw<InvalidOperationException>().WithMessage("*journal is empty*");
    }

    [Test]
    public void BuildBaselinePlan_ShouldReturnEmpty_WhenTargetEmpty()
    {
        var scripts = new[] { Script("20260101_000001_a") };

        var plan = _planner.BuildBaselinePlan(scripts, Array.Empty<MigrationJournalEntry>(), MigratorOptions.EmptyTarget);

        plan.HasChanges.Should().BeFalse();
        plan.TargetMigrationId.Should().Be(MigratorOptions.EmptyTarget);
    }

    [Test]
    public void BuildBaselinePlan_ShouldThrow_WhenTargetUnknown()
    {
        var scripts = new[] { Script("20260101_000001_a") };

        Action act = () => _planner.BuildBaselinePlan(scripts, Array.Empty<MigrationJournalEntry>(), "unknown");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown migration target*");
    }

    [Test]
    public void BuildBaselinePlan_ShouldIncludeStepsUpToTarget()
    {
        var scripts = new[]
        {
            Script("20260101_000001_a"),
            Script("20260101_000002_b")
        };

        var plan = _planner.BuildBaselinePlan(scripts, Array.Empty<MigrationJournalEntry>(), MigratorOptions.LatestTarget);

        plan.IsBaselineOnly.Should().BeTrue();
        plan.Steps.Should().HaveCount(2);
        plan.Steps.Should().OnlyContain(step => step.ScriptContent == string.Empty);
    }

    private static MigrationScript Script(string migrationId, bool withRollback = true, string? forwardContent = null)
    {
        var content = forwardContent ?? $"-- {migrationId}";
        var rollbackContent = withRollback ? $"-- rollback {migrationId}" : null;
        return new MigrationScript(
            migrationId,
            $"{migrationId}.sql",
            $"{migrationId}.sql",
            content,
            ComputeChecksum(content),
            withRollback ? $"{migrationId}.rollback.sql" : null!,
            withRollback ? $"{migrationId}.rollback.sql" : null!,
            rollbackContent!,
            rollbackContent is null ? null! : ComputeChecksum(rollbackContent));
    }

    private static MigrationJournalEntry Entry(long id, string migrationId, MigrationDirection direction, string checksum)
        => new(
            id,
            migrationId,
            direction,
            MigrationExecutionMode.Execute,
            migrationId + ".sql",
            checksum,
            "1.0.0",
            "tester",
            DateTimeOffset.UtcNow,
            1);

    private static string ComputeChecksum(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
