using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kisous.Migrator.Models;
using Kisous.Migrator.Services;
using Kisous.Migrator.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Testcontainers.MsSql;

namespace Kisous.Migrator.IntegrationTests;

[TestFixture]
[NonParallelizable]
public class SqlServerMigrationIntegrationTests
{
    [Test]
    public async Task HappyPath_ShouldApplyMigrationsAndWriteJournal()
    {
        await using var container = new MsSqlBuilder()
            .WithPassword("StrongPassword!123")
            .Build();

        await container.StartAsync();

        var tableName = "ItSqlApply" + Guid.NewGuid().ToString("N")[..8];
        var scripts = CreateSqlServerScripts(tableName);

        var options = Options.Create(new MigratorOptions { ConnectionString = container.GetConnectionString() });
        await using var provider = new SqlServerDatabaseProvider(options);
        var planner = new MigrationPlanner();
        var executor = new MigrationExecutor(provider, NullLogger<MigrationExecutor>.Instance);

        var initialJournal = await provider.GetEntriesAsync(CancellationToken.None);
        var plan = planner.BuildPlan(scripts, initialJournal, MigratorOptions.LatestTarget);

        await executor.ExecuteAsync(plan, "it", "integration-test", CancellationToken.None);

        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.OpenAsync();

        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM [dbo].[{tableName}];";
        var rowCount = (int)(await countCmd.ExecuteScalarAsync() ?? 0);

        var journal = await provider.GetEntriesAsync(CancellationToken.None);

        Assert.That(rowCount, Is.EqualTo(1));
        Assert.That(journal.Count, Is.EqualTo(2));
        Assert.That(journal[0].Direction, Is.EqualTo(MigrationDirection.Up));
        Assert.That(journal[1].Direction, Is.EqualTo(MigrationDirection.Up));
    }

    [Test]
    public async Task RollbackPath_ShouldRollbackToEmptyTarget()
    {
        await using var container = new MsSqlBuilder()
            .WithPassword("StrongPassword!123")
            .Build();

        await container.StartAsync();

        var tableName = "ItSqlRollback" + Guid.NewGuid().ToString("N")[..8];
        var scripts = CreateSqlServerScripts(tableName);

        var options = Options.Create(new MigratorOptions { ConnectionString = container.GetConnectionString() });
        await using var provider = new SqlServerDatabaseProvider(options);
        var planner = new MigrationPlanner();
        var executor = new MigrationExecutor(provider, NullLogger<MigrationExecutor>.Instance);

        var initialJournal = await provider.GetEntriesAsync(CancellationToken.None);
        var upPlan = planner.BuildPlan(scripts, initialJournal, MigratorOptions.LatestTarget);
        await executor.ExecuteAsync(upPlan, "it", "integration-test", CancellationToken.None);

        var afterUp = await provider.GetEntriesAsync(CancellationToken.None);
        var downPlan = planner.BuildPlan(scripts, afterUp, MigratorOptions.EmptyTarget);
        await executor.ExecuteAsync(downPlan, "it", "integration-test", CancellationToken.None);

        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.OpenAsync();

        await using var existsCmd = connection.CreateCommand();
        existsCmd.CommandText = @"
SELECT COUNT(*)
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'dbo' AND t.name = @name;";
        existsCmd.Parameters.AddWithValue("name", tableName);
        var tableCount = (int)(await existsCmd.ExecuteScalarAsync() ?? 0);

        var journal = await provider.GetEntriesAsync(CancellationToken.None);

        Assert.That(tableCount, Is.EqualTo(0));
        Assert.That(journal.Count, Is.EqualTo(4));
        Assert.That(journal[2].Direction, Is.EqualTo(MigrationDirection.Down));
        Assert.That(journal[3].Direction, Is.EqualTo(MigrationDirection.Down));
    }

    private static MigrationScript[] CreateSqlServerScripts(string tableName)
    {
        var migrationId1 = "20270101_000001_create_" + tableName.ToLowerInvariant();
        var migrationId2 = "20270101_000002_seed_" + tableName.ToLowerInvariant();

        var up1 = $"CREATE TABLE [dbo].[{tableName}] ([Id] INT NOT NULL PRIMARY KEY, [Name] NVARCHAR(50) NOT NULL);";
        var down1 = $"IF OBJECT_ID(N'[dbo].[{tableName}]', N'U') IS NOT NULL DROP TABLE [dbo].[{tableName}];";
        var up2 = $"INSERT INTO [dbo].[{tableName}] ([Id], [Name]) VALUES (1, N'ok');";
        var down2 = $"DELETE FROM [dbo].[{tableName}] WHERE [Id] = 1;";

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
