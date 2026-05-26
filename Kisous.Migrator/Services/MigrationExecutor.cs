using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Kisous.Migrator.Models;
using Kisous.Migrator.Abstractions;

namespace Kisous.Migrator.Services;

public sealed class MigrationExecutor
{
    private readonly IDatabaseProvider _provider;
    private readonly ILogger<MigrationExecutor> _logger;

    public MigrationExecutor(IDatabaseProvider provider, ILogger<MigrationExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);
        _provider = provider;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        MigrationPlan plan,
        string packageVersion,
        string appliedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await _provider.EnsureJournalTableAsync(cancellationToken);

        foreach (var step in plan.Steps)
        {
            _logger.LogInformation("Executing migration step {MigrationId} ({Direction}, {Mode}). File: {ScriptName}", 
                step.MigrationId, step.Direction, step.ExecutionMode, step.ScriptName);
                
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                await _provider.ExecuteStepAsync(step, packageVersion, appliedBy, cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation("Successfully executed {MigrationId} in {Elapsed}ms.", 
                    step.MigrationId, stopwatch.ElapsedMilliseconds);
            }
            catch(Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Failed to execute migration {MigrationId} after {Elapsed}ms.", 
                    step.MigrationId, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
