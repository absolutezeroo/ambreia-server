using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Vortex.Database.Configuration;

namespace Vortex.Database.Backup;

/// <summary>One dump on disk.</summary>
public sealed record DatabaseBackup(string FileName, long SizeBytes, DateTime CreatedUtc);

/// <summary>Outcome of asking for one.</summary>
public sealed record DatabaseBackupResult(bool Success, string Message, string? FileName = null);

public interface IDatabaseBackupService
{
    bool IsConfigured { get; }

    Task<DatabaseBackupResult> CreateAsync(string trigger, CancellationToken ct);

    IReadOnlyList<DatabaseBackup> List();
}

/// <summary>
/// Runs <c>mysqldump</c> and prunes what is older than the retention window.
///
/// <para>
/// This is the coarse net, complementary to the audit's per-row before/after: that one tells you
/// what a single write replaced, this one is what you reach for when something got past everything
/// else. Restoring a dump undoes every change since it was taken, which is precisely why it is the
/// last resort rather than the undo button.
/// </para>
/// </summary>
public sealed class DatabaseBackupService(
    IOptions<DatabaseBackupConfig> backupConfig,
    IOptions<DatabaseConfig> databaseConfig,
    ILogger<DatabaseBackupService> logger
) : IDatabaseBackupService
{
    private const string Extension = ".sql";
    private const string Prefix = "vortex-";

    private readonly DatabaseBackupConfig _config = backupConfig.Value;
    private readonly DatabaseConfig _database = databaseConfig.Value;

    // Two dumps of the same database at once would compete for disk and produce a half-written file
    // under one of the two names. The scheduler and the dashboard button share this.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsConfigured =>
        _config.Enabled && !string.IsNullOrWhiteSpace(_config.MysqlDumpPath);

    public async Task<DatabaseBackupResult> CreateAsync(string trigger, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return new DatabaseBackupResult(false, "backup_not_configured");
        }

        if (!File.Exists(_config.MysqlDumpPath))
        {
            logger.LogError(
                "Database backup cannot run: no mysqldump at {Path}.",
                _config.MysqlDumpPath
            );

            return new DatabaseBackupResult(false, "mysqldump_not_found");
        }

        if (!await _gate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            return new DatabaseBackupResult(false, "backup_already_running");
        }

        try
        {
            return await RunAsync(trigger, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<DatabaseBackup> List()
    {
        string directory = ResolveDirectory();

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. new DirectoryInfo(directory)
                .EnumerateFiles($"{Prefix}*{Extension}")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new DatabaseBackup(f.Name, f.Length, f.CreationTimeUtc)),
        ];
    }

    private async Task<DatabaseBackupResult> RunAsync(string trigger, CancellationToken ct)
    {
        MySqlConnectionStringBuilder connection = new(_database.ConnectionString);

        string directory = ResolveDirectory();

        Directory.CreateDirectory(directory);

        // Sortable, and unambiguous across a DST change because it is UTC.
        string fileName =
            $"{Prefix}{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{Extension}";
        string path = Path.Combine(directory, fileName);

        // mysqldump streams into this, and it is renamed to `path` only once the process has exited
        // cleanly. The leading dot is what keeps it out of the `{Prefix}*{Extension}` glob that
        // List() and Prune() use, so a dump still being written is not a backup yet.
        //
        // It used to stream straight into the final name. A cancellation from the caller -- a host
        // shutdown -- escapes both catches at the end of this method (the first filters that case
        // out, the second excludes the type), so a half-written dump stayed on disk under the name
        // of a good one, and List() offered it as the newest backup with nothing marking it
        // partial. Finding that out during a restore is finding it out too late.
        string partialPath = Path.Combine(directory, $".{fileName}.part");

        ProcessStartInfo startInfo = new()
        {
            FileName = _config.MysqlDumpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add($"--host={connection.Server}");
        startInfo.ArgumentList.Add(
            $"--port={connection.Port.ToString(CultureInfo.InvariantCulture)}"
        );
        startInfo.ArgumentList.Add($"--user={connection.UserID}");
        startInfo.ArgumentList.Add("--single-transaction");
        startInfo.ArgumentList.Add("--routines");
        startInfo.ArgumentList.Add("--events");
        startInfo.ArgumentList.Add("--default-character-set=utf8mb4");
        startInfo.ArgumentList.Add(connection.Database);

        // Never on the command line: process arguments are readable by any other process on the box.
        startInfo.EnvironmentVariables["MYSQL_PWD"] = connection.Password;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);

        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _config.TimeoutMinutes)));

        try
        {
            using Process process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException("mysqldump did not start");

            await using (
                FileStream file = new(
                    partialPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                Task copy = process.StandardOutput.BaseStream.CopyToAsync(file, timeout.Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);

                try
                {
                    await copy.ConfigureAwait(false);
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Disposing the Process object does not end the process: a timed-out or
                    // cancelled dump would keep running, and keep its server connection open.
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // Already exited.
                    }

                    throw;
                }

                if (process.ExitCode != 0)
                {
                    string detail = await errors.ConfigureAwait(false);

                    logger.LogError(
                        "mysqldump exited {ExitCode} for backup {FileName}: {Detail}",
                        process.ExitCode,
                        fileName,
                        detail.Trim()
                    );

                    // Leave nothing that could be mistaken for a usable backup.
                    file.Close();
                    TryDelete(partialPath);

                    return new DatabaseBackupResult(false, "mysqldump_failed");
                }
            }

            // The stream is closed and mysqldump exited 0: this is now a backup, and naming it is
            // the single step that says so.
            File.Move(partialPath, path, overwrite: true);

            long size = new FileInfo(path).Length;

            logger.LogInformation(
                "Database backup {FileName} written ({SizeBytes} bytes, trigger {Trigger}).",
                fileName,
                size,
                trigger
            );

            // Only after a good one landed: a run of failures must not eat the history.
            Prune(directory);

            return new DatabaseBackupResult(true, "ok", fileName);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                "Database backup {FileName} timed out after {Minutes} minutes.",
                fileName,
                _config.TimeoutMinutes
            );

            TryDelete(partialPath);

            return new DatabaseBackupResult(false, "backup_timed_out");
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled -- a host shutdown, usually. Nothing cleaned up after this case
            // before: the catch above filters it out and the one below excludes the type, so the
            // partial dump simply stayed. It still propagates, as it always did; it just no longer
            // leaves anything behind.
            TryDelete(partialPath);

            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Database backup {FileName} failed.", fileName);

            TryDelete(partialPath);

            return new DatabaseBackupResult(false, "backup_failed");
        }
    }

    private void Prune(string directory)
    {
        try
        {
            // Clamped rather than trusted: a mistyped 0 in the config would otherwise make pruning
            // delete the backup it was called to protect.
            int keep = Math.Max(1, _config.RetentionCount);

            List<FileInfo> stale =
            [
                .. new DirectoryInfo(directory)
                    .EnumerateFiles($"{Prefix}*{Extension}")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(keep),
            ];

            foreach (FileInfo file in stale)
            {
                file.Delete();

                logger.LogInformation(
                    "Pruned database backup {FileName} beyond the {Retention} kept.",
                    file.Name,
                    keep
                );
            }
        }
        catch (Exception ex)
        {
            // A backup that landed is worth more than a tidy directory; never fail the run for this.
            logger.LogWarning(
                ex,
                "Could not prune old database backups in {Directory}.",
                directory
            );
        }
    }

    private string ResolveDirectory() =>
        Path.IsPathRooted(_config.OutputDirectory)
            ? _config.OutputDirectory
            : Path.Combine(AppContext.BaseDirectory, _config.OutputDirectory);

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove the partial backup {Path}.", path);
        }
    }
}
