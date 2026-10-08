namespace Vortex.Database.Backup;

/// <summary>Outcome of asking for one.</summary>
public sealed record DatabaseBackupResult(bool Success, string Message, string? FileName = null);
