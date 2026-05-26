using System;
using System.Diagnostics.CodeAnalysis;

namespace Kisous.Migrator.Models;

[ExcludeFromCodeCoverage]
public sealed class MigratorOptions
{
    public MigrationCommand Command { get; set; } = MigrationCommand.Apply;
    public string TargetMigrationId { get; set; } = LatestTarget;
    public string MigrationsPath { get; set; } = DefaultMigrationsPath;
    public string BootstrapScriptPath { get; set; } = DefaultBootstrapScriptPath;
    public string ConnectionString { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = "local";
    public string AppliedBy { get; set; } = Environment.UserName;

    public static string DefaultMigrationsPath => System.IO.Path.Combine(AppContext.BaseDirectory, "Migrations");
    public static string DefaultBootstrapScriptPath => System.IO.Path.Combine(AppContext.BaseDirectory, "Bootstrap", "init-db.sql");

    public const string LatestTarget = "latest";
    public const string EmptyTarget = "0";
}
