using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kisous.Migrator.Models;

namespace Kisous.Migrator.Services;

public sealed class MigrationScriptDiscovery
{
    private static readonly Regex ScriptNamePattern = new Regex(
        "^(?<id>\\d{8}_\\d{6}_[a-z0-9_]+)(?<rollback>\\.rollback)?\\.sql$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public IReadOnlyList<MigrationScript> Discover(string migrationsPath)
    {
        if (string.IsNullOrWhiteSpace(migrationsPath))
        {
            throw new ArgumentException("Migrations path is required.", nameof(migrationsPath));
        }

        if (!Directory.Exists(migrationsPath))
        {
            throw new DirectoryNotFoundException($"Migrations path '{migrationsPath}' does not exist.");
        }

        var sqlFiles = Directory.GetFiles(migrationsPath, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var grouped = new Dictionary<string, (string ForwardPath, string RollbackPath)>(StringComparer.Ordinal);

        foreach (var file in sqlFiles)
        {
            var fileName = Path.GetFileName(file);
            var match = ScriptNamePattern.Match(fileName);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Invalid migration file name '{fileName}'. Expected yyyyMMdd_HHmmss_description.sql or yyyyMMdd_HHmmss_description.rollback.sql.");
            }

            var migrationId = match.Groups["id"].Value;
            var isRollback = match.Groups["rollback"].Success;
            grouped.TryGetValue(migrationId, out var current);

            if (isRollback)
            {
                if (!string.IsNullOrWhiteSpace(current.RollbackPath))
                {
                    throw new InvalidOperationException($"Duplicate rollback migration found for '{migrationId}'.");
                }

                current.RollbackPath = file;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(current.ForwardPath))
                {
                    throw new InvalidOperationException($"Duplicate forward migration found for '{migrationId}'.");
                }

                current.ForwardPath = file;
            }

            grouped[migrationId] = current;
        }

        var scripts = new List<MigrationScript>();

        foreach (var pair in grouped.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(pair.Value.ForwardPath))
            {
                throw new InvalidOperationException($"Rollback script exists without a forward script for '{pair.Key}'.");
            }

            var forwardContent = File.ReadAllText(pair.Value.ForwardPath, Encoding.UTF8);
            var rollbackContent = string.IsNullOrWhiteSpace(pair.Value.RollbackPath)
                ? null
                : File.ReadAllText(pair.Value.RollbackPath, Encoding.UTF8);

            scripts.Add(new MigrationScript(
                pair.Key,
                pair.Value.ForwardPath,
                Path.GetFileName(pair.Value.ForwardPath),
                forwardContent,
                ComputeChecksum(forwardContent),
                pair.Value.RollbackPath,
                string.IsNullOrWhiteSpace(pair.Value.RollbackPath) ? string.Empty : Path.GetFileName(pair.Value.RollbackPath),
                rollbackContent ?? string.Empty,
                rollbackContent == null ? string.Empty : ComputeChecksum(rollbackContent)));
        }

        return scripts;
    }

    private static string ComputeChecksum(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
