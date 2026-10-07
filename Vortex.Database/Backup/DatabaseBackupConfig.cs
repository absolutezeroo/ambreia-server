using System.ComponentModel.DataAnnotations;

namespace Vortex.Database.Backup;

/// <summary>
/// Where the safety net lives and how much of it is kept.
///
/// <para>
/// Bootstrap configuration rather than an admin-editable table on purpose: a backup that can be
/// turned off, redirected or starved of retention from the same admin surface whose mistakes it
/// exists to undo is not a safety net.
/// </para>
/// </summary>
public sealed class DatabaseBackupConfig
{
    public const string SECTION_NAME = "Vortex:Database:Backup";

    /// <summary>Off unless a deployment opts in — a dump needs a writable disk and a mysqldump binary,
    /// neither of which can be assumed.</summary>
    public bool Enabled { get; set; }

    /// <summary>Full path to <c>mysqldump</c>. No discovery: guessing at a binary that writes files
    /// somewhere is worse than saying it is not configured.</summary>
    public string MysqlDumpPath { get; set; } = string.Empty;

    /// <summary>Directory the dumps are written to. Created if missing.</summary>
    public string OutputDirectory { get; set; } = "backups";

    /// <summary>Hours between scheduled dumps. Zero disables the schedule, and with it every dump:
    /// nothing else triggers one.</summary>
    [Range(0, 24 * 30)]
    public int IntervalHours { get; set; } = 24;

    /// <summary>How many dumps to keep. The oldest beyond this are deleted after a successful new
    /// one — never before, so a failing dump cannot erode the history it failed to add to.</summary>
    [Range(1, 500)]
    public int RetentionCount { get; set; } = 14;

    /// <summary>How long a single dump may take before it is abandoned.</summary>
    [Range(1, 720)]
    public int TimeoutMinutes { get; set; } = 30;
}
