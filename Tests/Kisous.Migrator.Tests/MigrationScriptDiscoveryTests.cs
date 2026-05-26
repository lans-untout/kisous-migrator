using System;
using System.IO;
using FluentAssertions;
using Kisous.Migrator.Services;
using NUnit.Framework;

namespace Kisous.Migrator.Tests;

[TestFixture]
public class MigrationScriptDiscoveryTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kisous-discovery-" + Guid.NewGuid().ToString("N"));
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
    public void Discover_ShouldThrow_WhenPathMissing()
    {
        var sut = new MigrationScriptDiscovery();

        Action act = () => sut.Discover(" ");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Discover_ShouldThrow_WhenDirectoryDoesNotExist()
    {
        var sut = new MigrationScriptDiscovery();

        Action act = () => sut.Discover(Path.Combine(_tempDir, "missing"));

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Test]
    public void Discover_ShouldThrow_WhenFileNameInvalid()
    {
        var sut = new MigrationScriptDiscovery();
        File.WriteAllText(Path.Combine(_tempDir, "badname.sql"), "select 1;");

        Action act = () => sut.Discover(_tempDir);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid migration file name*");
    }

    [Test]
    public void Discover_ShouldThrow_WhenRollbackWithoutForward()
    {
        var sut = new MigrationScriptDiscovery();
        File.WriteAllText(Path.Combine(_tempDir, "20260101_000001_init.rollback.sql"), "rollback;");

        Action act = () => sut.Discover(_tempDir);

        act.Should().Throw<InvalidOperationException>().WithMessage("*without a forward script*");
    }

    [Test]
    public void Discover_ShouldReadAndOrderScriptsWithRollback()
    {
        var sut = new MigrationScriptDiscovery();

        File.WriteAllText(Path.Combine(_tempDir, "20260101_000002_second.sql"), "select 2;");
        File.WriteAllText(Path.Combine(_tempDir, "20260101_000001_first.sql"), "select 1;");
        File.WriteAllText(Path.Combine(_tempDir, "20260101_000001_first.rollback.sql"), "rollback 1;");

        var scripts = sut.Discover(_tempDir);

        scripts.Should().HaveCount(2);
        scripts[0].MigrationId.Should().Be("20260101_000001_first");
        scripts[1].MigrationId.Should().Be("20260101_000002_second");
        scripts[0].HasRollback.Should().BeTrue();
        scripts[1].HasRollback.Should().BeFalse();
        scripts[0].RollbackScriptName.Should().Be("20260101_000001_first.rollback.sql");
        scripts[0].ForwardContent.Should().Be("select 1;");
    }
}
