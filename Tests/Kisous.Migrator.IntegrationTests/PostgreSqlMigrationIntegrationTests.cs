using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kisous.Migrator.Models;
using Kisous.Migrator.PostgreSql;
using Kisous.Migrator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace Kisous.Migrator.IntegrationTests;

[TestFixture]
[NonParallelizable]
public class PostgreSqlMigrationIntegrationTests
{
    [Test]
    public async Task HappyPath_ShouldApplyMigrationsAndWriteJournal()
    {
        await using var container = new PostgreSqlBuilder()
            .WithDatabase("migrator_it")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await container.StartAsync();

        var tableName = "it_pg_apply_" + Guid.NewGuid().ToString("N")[..8];
        var scripts = CreatePostgreSqlScripts(tableName);

        var options = Options.Create(new MigratorOptions { ConnectionString = container.GetConnectionString() });
        await using var provider = new PostgreSqlDatabaseProvider(options);
        var planner = new MigrationPlanner();
        var executor = new MigrationExecutor(provider, NullLogger<MigrationExecutor>.Instance);

        var initialJournal = await provider.GetEntriesAsync(CancellationToken.None);
        var plan = planner.BuildPlan(scripts, initialJournal, MigratorOptions.LatestTarget);

        await executor.ExecuteAsync(plan, "it", "integration-test", CancellationToken.None);

        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();

        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM public.\"{tableName}\";";
        var rowCount = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);

        var journal = await provider.GetEntriesAsync(CancellationToken.None);

        Assert.That(rowCount, Is.EqualTo(1));
        Assert.That(journal.Count, Is.EqualTo(2));
        Assert.That(journal[0].Direction, Is.EqualTo(MigrationDirection.Up));
        Assert.That(journal[1].Direction, Is.EqualTo(MigrationDirection.Up));
    }

    [Test]
    public async Task RollbackPath_ShouldRollbackToEmptyTarget()
    {
        await using var container = new PostgreSqlBuilder()
            .WithDatabase("migrator_it")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await container.StartAsync();

        var tableName = "it_pg_rollback_" + Guid.NewGuid().ToString("N")[..8];
        var scripts = CreatePostgreSqlScripts(tableName);

        var options = Options.Create(new MigratorOptions { ConnectionString = container.GetConnectionString() });
        await using var provider = new PostgreSqlDatabaseProvider(options);
        var planner = new MigrationPlanner();
        var executor = new MigrationExecutor(provider, NullLogger<MigrationExecutor>.Instance);

        var initialJournal = await provider.GetEntriesAsync(CancellationToken.None);
        var upPlan = planner.BuildPlan(scripts, initialJournal, MigratorOptions.LatestTarget);
        await executor.ExecuteAsync(upPlan, "it", "integration-test", CancellationToken.None);

        var afterUp = await provider.GetEntriesAsync(CancellationToken.None);
        var downPlan = planner.BuildPlan(scripts, afterUp, MigratorOptions.EmptyTarget);
        await executor.ExecuteAsync(downPlan, "it", "integration-test", CancellationToken.None);

        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();

        await using var existsCmd = connection.CreateCommand();
        existsCmd.CommandText = @"
SELECT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_schema = 'public' AND table_name = @name);
";
        existsCmd.Parameters.AddWithValue("name", tableName);
        var tableExists = (bool)(await existsCmd.ExecuteScalarAsync() ?? false);

        var journal = await provider.GetEntriesAsync(CancellationToken.None);

        Assert.That(tableExists, Is.False);
        Assert.That(journal.Count, Is.EqualTo(4));
        Assert.That(journal[2].Direction, Is.EqualTo(MigrationDirection.Down));
        Assert.That(journal[3].Direction, Is.EqualTo(MigrationDirection.Down));
    }

    private static MigrationScript[] CreatePostgreSqlScripts(string tableName)
    {
        var migrationId1 = "20270101_000001_create_" + tableName;
        var migrationId2 = "20270101_000002_seed_" + tableName;

        var up1 = $"CREATE TABLE public.\"{tableName}\" (id INT PRIMARY KEY, name TEXT NOT NULL);";
        var down1 = $"DROP TABLE IF EXISTS public.\"{tableName}\";";
        var up2 = $"INSERT INTO public.\"{tableName}\" (id, name) VALUES (1, 'ok');";
        var down2 = $"DELETE FROM public.\"{tableName}\" WHERE id = 1;";

        return new[]
        {
            CreateScript(migrationId1, up1, down1),
            CreateScript(migrationId2, up2, down2)
        };
    }

    private static MigrationScript CreateScript(string migrationId, string upSql, string downSql)
    {
        return new MigrationScript(
            migrationId,
            migrationId + ".sql",
            migrationId + ".sql",
            upSql,
            Checksum(upSql),
            migrationId + ".rollback.sql",
            migrationId + ".rollback.sql",
            downSql,
            Checksum(downSql));
    }

    private static string Checksum(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
