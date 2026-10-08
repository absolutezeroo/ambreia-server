using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Database.Backup;

public interface IDatabaseBackupService
{
    bool IsConfigured { get; }

    Task<DatabaseBackupResult> CreateAsync(string trigger, CancellationToken ct);

    IReadOnlyList<DatabaseBackup> List();
}
