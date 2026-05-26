using System;
using System.IO;
using FluentAssertions;
using Kisous.Migrator.Models;
using Kisous.Migrator.Services;
using NUnit.Framework;

namespace Kisous.Migrator.Tests;

[TestFixture]
public class BootstrapMigrationFactoryTests
{
    private const string BootstrapId = "20260101_000000_initial_schema";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kisous-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void AugmentWithBootstrap_ShouldReturnOriginal_WhenBootstrapAlreadyPresent()
    {
        var sut = new BootstrapMigrationFactory();
        var existing = new[]
        {
            new MigrationScript(
                BootstrapId,
                "b.sql",
                "b.sql",
                "select 1;",
                "h",
                "b.rollback.sql",
                "b.rollback.sql",
                "rollback;",
                "rh")
        };

        var result = sut.AugmentWithBootstrap(existing, "ignored.sql");

        result.Should().BeSameAs(existing);
    }

    [Test]
    public void AugmentWithBootstrap_ShouldThrow_WhenPathEmpty()
    {
        var sut = new BootstrapMigrationFactory();

        Action act = () => sut.AugmentWithBootstrap(Array.Empty<MigrationScript>(), " ");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AugmentWithBootstrap_ShouldThrow_WhenFileMissing()
    {
        var sut = new BootstrapMigrationFactory();

        Action act = () => sut.AugmentWithBootstrap(Array.Empty<MigrationScript>(), Path.Combine(_tempDir, "missing.sql"));

        act.Should().Throw<FileNotFoundException>();
    }

    [Test]
    public void AugmentWithBootstrap_ShouldInsertScriptAndSortByMigrationId()
    {
        var sut = new BootstrapMigrationFactory();
        var bootstrapPath = Path.Combine(_tempDir, "init-db.sql");
        File.WriteAllText(bootstrapPath, "-- bootstrap\nselect 1;");

        var scripts = new[]
        {
            new MigrationScript("20270101_000001_future", "f.sql", "f.sql", "select 2;", "h2", null!, null!, null!, null!)
        };

        var result = sut.AugmentWithBootstrap(scripts, bootstrapPath);

        result.Should().HaveCount(2);
        result[0].MigrationId.Should().Be(BootstrapId);
        result[0].HasRollback.Should().BeTrue();
        result[1].MigrationId.Should().Be("20270101_000001_future");
    }

    [Test]
    public void PrepareBootstrapScript_ShouldRemoveDevRoleBlockAndTrim()
    {
        var sut = new BootstrapMigrationFactory();
                var input = @"
-- Create Silatigui_user role if not exists
DO $do$
BEGIN
  RAISE NOTICE 'x';
END $do$;


CREATE TABLE demo(id int);
";
        var bootstrapPath = Path.Combine(_tempDir, "init-db.sql");
        File.WriteAllText(bootstrapPath, input);

        var output = sut.AugmentWithBootstrap(Array.Empty<MigrationScript>(), bootstrapPath)[0].ForwardContent;

        output.Should().StartWith("CREATE TABLE demo");
        output.Should().NotContain("Silatigui_user");
    }

    [Test]
    public void PrepareBootstrapScript_ShouldThrow_WhenContentEmpty()
    {
        var sut = new BootstrapMigrationFactory();
        var bootstrapPath = Path.Combine(_tempDir, "init-db.sql");
        File.WriteAllText(bootstrapPath, " ");

        Action act = () => sut.AugmentWithBootstrap(Array.Empty<MigrationScript>(), bootstrapPath);

        act.Should().Throw<ArgumentException>();
    }
}
