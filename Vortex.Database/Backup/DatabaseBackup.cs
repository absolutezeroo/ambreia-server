using System;

namespace Vortex.Database.Backup;

/// <summary>One dump on disk.</summary>
public sealed record DatabaseBackup(string FileName, long SizeBytes, DateTime CreatedUtc);
