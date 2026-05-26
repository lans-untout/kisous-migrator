using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Kisous.Migrator;
using Kisous.Migrator.Models;
using Kisous.Migrator.Services;

namespace Kisous.Migrator.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            var connectionString = builder.Configuration["connection-string"] ?? builder.Configuration["connectionString"];
            var provider = builder.Configuration["provider"]?.ToLowerInvariant();
            
            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(provider))
            {
                Console.WriteLine("Usage: kisous-migrate --provider [postgres|sqlserver] --connection-string YOUR_CONN_STR --migrations-path ./db");
                return 1;
            }

            builder.Services.AddKisousMigrator();
            
            if (provider == "postgres") builder.Services.UsePostgreSql(connectionString);
            else if (provider == "sqlserver") builder.Services.UseSqlServer(connectionString);
            else { Console.WriteLine("Unknown provider."); return 1; }

            // Bind remaining options
            builder.Services.Configure<MigratorOptions>(opts => 
            {
                opts.MigrationsPath = builder.Configuration["migrations-path"] ?? MigratorOptions.DefaultMigrationsPath;
                opts.TargetMigrationId = builder.Configuration["target"] ?? MigratorOptions.LatestTarget;
            });

            var app = builder.Build();

            // Resolve services to run the migration
            using var scope = app.Services.CreateScope();
            var discovery = scope.ServiceProvider.GetRequiredService<MigrationScriptDiscovery>();
            var factory = scope.ServiceProvider.GetRequiredService<BootstrapMigrationFactory>();
            var planner = scope.ServiceProvider.GetRequiredService<MigrationPlanner>();
            var executor = scope.ServiceProvider.GetRequiredService<MigrationExecutor>();
            var dbProvider = scope.ServiceProvider.GetRequiredService<Kisous.Migrator.Abstractions.IDatabaseProvider>();
            
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Starting Kisous Migration...");

            var token = app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
            var scripts = factory.AugmentWithBootstrap(discovery.Discover(builder.Configuration["migrations-path"] ?? MigratorOptions.DefaultMigrationsPath), MigratorOptions.DefaultBootstrapScriptPath);
            var entries = await dbProvider.GetEntriesAsync(token);
            var plan = planner.BuildPlan(scripts, entries, builder.Configuration["target"] ?? MigratorOptions.LatestTarget);
            
            if (!plan.HasChanges)
            {
                logger.LogInformation("No migrations required.");
                return 0;
            }

            await executor.ExecuteAsync(plan, "cli", Environment.UserName, token);
            logger.LogInformation("Migrations completed up to {TargetId}.", plan.TargetMigrationId);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed: " + ex.Message);
            return 1;
        }
    }
}
