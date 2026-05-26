using FluentAssertions;
using Kisous.Migrator.Models;
using NUnit.Framework;

namespace Kisous.Migrator.Tests;

[TestFixture]
public class MigrationPlanStepTests
{
    [Test]
    public void DisplayAction_ShouldBeBaseline_ForBaselineMode()
    {
        var step = new MigrationPlanStep("001", MigrationDirection.Up, MigrationExecutionMode.Baseline, "001.sql", "--", "h");

        step.DisplayAction.Should().Be("baseline");
        step.ScriptChecksum.Should().Be("h");
    }

    [Test]
    public void DisplayAction_ShouldBeApply_ForExecuteUp()
    {
        var step = new MigrationPlanStep("001", MigrationDirection.Up, MigrationExecutionMode.Execute, "001.sql", "--", "h");

        step.DisplayAction.Should().Be("apply");
    }

    [Test]
    public void DisplayAction_ShouldBeRollback_ForExecuteDown()
    {
        var step = new MigrationPlanStep("001", MigrationDirection.Down, MigrationExecutionMode.Execute, "001.rollback.sql", "--", "h");

        step.DisplayAction.Should().Be("rollback");
    }
}
