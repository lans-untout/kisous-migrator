using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kisous.Migrator.Abstractions;

public interface IDatabaseProvider
{
    Task EnsureJournalTableAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Kisous.Migrator.Models.MigrationJournalEntry>> GetEntriesAsync(CancellationToken cancellationToken);
    Task ExecuteStepAsync(Kisous.Migrator.Models.MigrationPlanStep step, string packageVersion, string appliedBy, CancellationToken cancellationToken);
}
