using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Kisous.Migrator.Abstractions;
using Kisous.Migrator.Models;
using Kisous.Migrator.Services;
using Kisous.Migrator.PostgreSql;
using Kisous.Migrator.SqlServer;

namespace Kisous.Migrator;

public static class MigratorServiceCollectionExtensions
{
    public static IServiceCollection AddKisousMigrator(this IServiceCollection services)
    {
        services.AddOptions<MigratorOptions>();
        
        services.AddTransient<MigrationScriptDiscovery>();
        services.AddTransient<BootstrapMigrationFactory>();
        services.AddTransient<MigrationPlanner>();
        services.AddTransient<MigrationExecutor>();

        return services;
    }

    public static IServiceCollection UsePostgreSql(this IServiceCollection services, string connectionString)
    {
        services.Configure<MigratorOptions>(opts => opts.ConnectionString = connectionString);
        services.AddScoped<IDatabaseProvider, PostgreSqlDatabaseProvider>();
        return services;
    }

    public static IServiceCollection UseSqlServer(this IServiceCollection services, string connectionString)
    {
        services.Configure<MigratorOptions>(opts => opts.ConnectionString = connectionString);
        services.AddScoped<IDatabaseProvider, SqlServerDatabaseProvider>();
        return services;
    }
}