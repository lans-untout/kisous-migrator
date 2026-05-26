using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Kisous.Migrator.Models;

namespace Kisous.Migrator.Services;

public sealed class MigrationPlanner
{
    private const string OfflineBudgetsMigrationId = "20260428_000002_add_offline_budgets";
    private const string OfflineVouchersMigrationId = "20260428_000003_add_offline_vouchers";

    public MigrationPlan BuildPlan(
        IReadOnlyList<MigrationScript> scripts,
        IReadOnlyList<MigrationJournalEntry> journalEntries,
        string requestedTarget)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(journalEntries);
        var orderedScripts = scripts;
        var entries = journalEntries;
        var target = string.IsNullOrWhiteSpace(requestedTarget) ? MigratorOptions.LatestTarget : requestedTarget.Trim();

        var indexedScripts = orderedScripts
            .Select((script, index) => new { script.MigrationId, index })
            .ToDictionary(item => item.MigrationId, item => item.index, StringComparer.Ordinal);

        EnsureAllJournalMigrationsExist(entries, indexedScripts);
        EnsureJournalChecksumsMatch(entries, orderedScripts);

        var activeMigrationIds = GetActiveMigrationIds(entries, orderedScripts);
        var currentIndex = activeMigrationIds.Count - 1;
        var targetIndex = ResolveTargetIndex(target, orderedScripts, indexedScripts);

        var currentMigrationId = currentIndex >= 0 ? orderedScripts[currentIndex].MigrationId : MigratorOptions.EmptyTarget;
        var targetMigrationId = targetIndex >= 0 ? orderedScripts[targetIndex].MigrationId : MigratorOptions.EmptyTarget;

        if (targetIndex == currentIndex)
        {
            return new MigrationPlan(currentMigrationId, targetMigrationId, Array.Empty<MigrationPlanStep>());
        }

        var steps = new List<MigrationPlanStep>();

        if (targetIndex > currentIndex)
        {
            for (var index = currentIndex + 1; index <= targetIndex; index++)
            {
                var script = orderedScripts[index];
                steps.Add(new MigrationPlanStep(
                    script.MigrationId,
                    MigrationDirection.Up,
                    MigrationExecutionMode.Execute,
                    script.ForwardScriptName,
                    script.ForwardContent,
                    script.ForwardChecksum));
            }
        }
        else
        {
            for (var index = currentIndex; index > targetIndex; index--)
            {
                var script = orderedScripts[index];
                if (!script.HasRollback)
                {
                    throw new InvalidOperationException(
                        $"Cannot roll back migration '{script.MigrationId}' because '{script.MigrationId}.rollback.sql' is missing.");
                }

                steps.Add(new MigrationPlanStep(
                    script.MigrationId,
                    MigrationDirection.Down,
                    MigrationExecutionMode.Execute,
                    script.RollbackScriptName,
                    script.RollbackContent,
                    script.RollbackChecksum));
            }
        }

        return new MigrationPlan(currentMigrationId, targetMigrationId, steps);
    }

    public MigrationPlan BuildBaselinePlan(
        IReadOnlyList<MigrationScript> scripts,
        IReadOnlyList<MigrationJournalEntry> journalEntries,
        string requestedTarget)
    {
        if (journalEntries == null)
        {
            throw new ArgumentNullException(nameof(journalEntries));
        }

        if (journalEntries.Count > 0)
        {
            throw new InvalidOperationException("Baseline is only allowed when the migration journal is empty.");
        }

        ArgumentNullException.ThrowIfNull(scripts);
        var orderedScripts = scripts;
        var indexedScripts = orderedScripts
            .Select((script, index) => new { script.MigrationId, index })
            .ToDictionary(item => item.MigrationId, item => item.index, StringComparer.Ordinal);
        var targetIndex = ResolveTargetIndex(requestedTarget, orderedScripts, indexedScripts);

        if (targetIndex < 0)
        {
            return new MigrationPlan(MigratorOptions.EmptyTarget, MigratorOptions.EmptyTarget, Array.Empty<MigrationPlanStep>());
        }

        var steps = orderedScripts
            .Take(targetIndex + 1)
            .Select(script => new MigrationPlanStep(
                script.MigrationId,
                MigrationDirection.Up,
                MigrationExecutionMode.Baseline,
                script.ForwardScriptName,
                string.Empty,
                script.ForwardChecksum))
            .ToArray();

        return new MigrationPlan(MigratorOptions.EmptyTarget, orderedScripts[targetIndex].MigrationId, steps);
    }

    private static void EnsureAllJournalMigrationsExist(
        IEnumerable<MigrationJournalEntry> journalEntries,
        IReadOnlyDictionary<string, int> indexedScripts)
    {
        // Any journal entry whose ID is lexicographically before the earliest
        // *non-bootstrap* script in the current package is a pre-consolidation entry:
        // it was legitimately applied before a migration epoch reset and the original
        // script file has since been archived.  Skip the existence check for these
        // entries so that deployed databases can advance past the consolidation boundary
        // without manual journal surgery.
        //
        // The synthetic bootstrap script (20260101_000000_initial_schema) is excluded
        // from the epoch computation because it is always prepended and predates all
        // real migration IDs by design.
        var epochId = indexedScripts.Keys
            .Where(id => !string.Equals(id, BootstrapMigrationFactory.BootstrapMigrationId, StringComparison.Ordinal))
            .OrderBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault();

        foreach (var entry in journalEntries)
        {
            if (epochId != null && string.CompareOrdinal(entry.MigrationId, epochId) < 0)
                continue; // pre-consolidation history – existence check skipped

            if (!indexedScripts.ContainsKey(entry.MigrationId))
            {
                throw new InvalidOperationException(
                    $"Migration journal contains '{entry.MigrationId}', but no script with that ID exists in the current package.");
            }
        }
    }

    private static void EnsureJournalChecksumsMatch(
        IReadOnlyList<MigrationJournalEntry> journalEntries,
        IReadOnlyList<MigrationScript> orderedScripts)
    {
        var scriptsById = orderedScripts.ToDictionary(script => script.MigrationId, StringComparer.Ordinal);
        var latestEntries = journalEntries
            .GroupBy(entry => entry.MigrationId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(entry => entry.JournalId).Last());

        foreach (var entry in latestEntries)
        {
            if (!scriptsById.TryGetValue(entry.MigrationId, out var script))
            {
                continue;
            }

            var expectedChecksum = entry.Direction == MigrationDirection.Up
                ? script.ForwardChecksum
                : script.RollbackChecksum ?? string.Empty;

            var journalChecksum = entry.ScriptChecksum ?? string.Empty;
            if (!string.Equals(journalChecksum, expectedChecksum, StringComparison.Ordinal)
                && !IsKnownLegacyChecksumMatch(entry, script, journalChecksum))
            {
                throw new InvalidOperationException(
                    $"Migration '{entry.MigrationId}' checksum mismatch: the script content in the current package differs from what was previously journaled.");
            }
        }
    }

    private static bool IsKnownLegacyChecksumMatch(
        MigrationJournalEntry entry,
        MigrationScript script,
        string journalChecksum)
    {
        if (entry.Direction != MigrationDirection.Up)
        {
            return false;
        }

        if (!string.Equals(script.MigrationId, OfflineBudgetsMigrationId, StringComparison.Ordinal)
            && !string.Equals(script.MigrationId, OfflineVouchersMigrationId, StringComparison.Ordinal))
        {
            return false;
        }

        var variants = new HashSet<string>(StringComparer.Ordinal)
        {
            script.ForwardContent
        };

        // 20260428_000002: historical package variants used either Unicode arrow or ASCII arrow.
        variants.Add(script.ForwardContent.Replace("\u2192", "->", StringComparison.Ordinal));
        variants.Add(script.ForwardContent.Replace("->", "\u2192", StringComparison.Ordinal));

        // 20260428_000003: historical package variants used either em dash or hyphen in one comment.
        variants.Add(script.ForwardContent.Replace(
            " || ts) \u2014 set when ack QR scanned",
            " || ts) - set when ack QR scanned",
            StringComparison.Ordinal));
        variants.Add(script.ForwardContent.Replace(
            " || ts) - set when ack QR scanned",
            " || ts) \u2014 set when ack QR scanned",
            StringComparison.Ordinal));

        return variants
            .Select(ComputeChecksum)
            .Any(checksum => string.Equals(checksum, journalChecksum, StringComparison.Ordinal));
    }

    private static string ComputeChecksum(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    internal IReadOnlyList<string> GetActiveMigrationIds(
        IReadOnlyList<MigrationJournalEntry> journalEntries,
        IReadOnlyList<MigrationScript> orderedScripts)
    {
        var lastDirectionByMigration = journalEntries
            .GroupBy(entry => entry.MigrationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entry => entry.JournalId).Last().Direction,
                StringComparer.Ordinal);

        var activeIds = orderedScripts
            .Where(script =>
                lastDirectionByMigration.TryGetValue(script.MigrationId, out var direction)
                && direction == MigrationDirection.Up)
            .Select(script => script.MigrationId)
            .ToList();

        EnsurePrefixState(activeIds, orderedScripts);
        return activeIds;
    }

    private static void EnsurePrefixState(
        IReadOnlyList<string> activeMigrationIds,
        IReadOnlyList<MigrationScript> orderedScripts)
    {
        var activeLookup = new HashSet<string>(activeMigrationIds, StringComparer.Ordinal);
        var seenGap = false;

        foreach (var script in orderedScripts)
        {
            var isActive = activeLookup.Contains(script.MigrationId);
            if (!isActive)
            {
                seenGap = true;
                continue;
            }

            if (seenGap)
            {
                throw new InvalidOperationException(
                    "Effective migration state is not a prefix of the ordered scripts. Resolve the journal manually before continuing.");
            }
        }
    }

    private static int ResolveTargetIndex(
        string requestedTarget,
        IReadOnlyList<MigrationScript> orderedScripts,
        IReadOnlyDictionary<string, int> indexedScripts)
    {
        if (orderedScripts.Count == 0)
        {
            return -1;
        }

        if (string.Equals(requestedTarget, MigratorOptions.LatestTarget, StringComparison.OrdinalIgnoreCase))
        {
            return orderedScripts.Count - 1;
        }

        if (string.Equals(requestedTarget, MigratorOptions.EmptyTarget, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        if (!indexedScripts.TryGetValue(requestedTarget, out var indexedScript))
        {
            throw new InvalidOperationException($"Unknown migration target '{requestedTarget}'.");
        }

        return indexedScript;
    }
}
