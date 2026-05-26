using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kisous.Migrator.Abstractions;
using Kisous.Migrator.Models;
using Kisous.Migrator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Kisous.Migrator.Tests;

[TestFixture]
public class MigrationExecutorTests
{
    [Test]
    public void Constructor_ShouldThrow_WhenProviderNull()
    {
        Action act = () => new MigrationExecutor(null!, NullLogger<MigrationExecutor>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Constructor_ShouldThrow_WhenLoggerNull()
    {
        Action act = () => new MigrationExecutor(new FakeProvider(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ExecuteAsync_ShouldThrow_WhenPlanNull()
    {
        var sut = new MigrationExecutor(new FakeProvider(), NullLogger<MigrationExecutor>.Instance);

        Func<Task> act = () => sut.ExecuteAsync(null!, "1.0.0", "tester", CancellationToken.None);

        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task ExecuteAsync_ShouldCallProviderForEachStep()
    {
        var provider = new FakeProvider();
        var sut = new MigrationExecutor(provider, NullLogger<MigrationExecutor>.Instance);
        var steps = new[]
        {
            new MigrationPlanStep("001", MigrationDirection.Up, MigrationExecutionMode.Execute, "001.sql", "select 1;", "h1"),
            new MigrationPlanStep("002", MigrationDirection.Up, MigrationExecutionMode.Execute, "002.sql", "select 2;", "h2")
        };
        var plan = new MigrationPlan(MigratorOptions.EmptyTarget, "002", steps);

        await sut.ExecuteAsync(plan, "1.0.0", "tester", CancellationToken.None);

        provider.EnsureCalled.Should().Be(1);
        provider.ExecutedSteps.Should().HaveCount(2);
        provider.ExecutedSteps[0].MigrationId.Should().Be("001");
        provider.ExecutedSteps[1].MigrationId.Should().Be("002");
    }

    [Test]
    public async Task ExecuteAsync_ShouldRethrow_WhenProviderFails()
    {
        var provider = new FakeProvider { ThrowOnStep = 1 };
        var sut = new MigrationExecutor(provider, NullLogger<MigrationExecutor>.Instance);
        var steps = new[]
        {
            new MigrationPlanStep("001", MigrationDirection.Up, MigrationExecutionMode.Execute, "001.sql", "select 1;", "h1"),
            new MigrationPlanStep("002", MigrationDirection.Up, MigrationExecutionMode.Execute, "002.sql", "select 2;", "h2")
        };
        var plan = new MigrationPlan(MigratorOptions.EmptyTarget, "002", steps);

        Func<Task> act = () => sut.ExecuteAsync(plan, "1.0.0", "tester", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        provider.ExecutedSteps.Should().HaveCount(1);
    }

    private sealed class FakeProvider : IDatabaseProvider
    {
        public int EnsureCalled { get; private set; }
        public List<MigrationPlanStep> ExecutedSteps { get; } = new();
        public int ThrowOnStep { get; set; } = -1;

        public Task EnsureJournalTableAsync(CancellationToken cancellationToken)
        {
            EnsureCalled++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MigrationJournalEntry>> GetEntriesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MigrationJournalEntry>>(Array.Empty<MigrationJournalEntry>());

        public Task ExecuteStepAsync(MigrationPlanStep step, string packageVersion, string appliedBy, CancellationToken cancellationToken)
        {
            ExecutedSteps.Add(step);

            if (ThrowOnStep == ExecutedSteps.Count)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.CompletedTask;
        }
    }
}
