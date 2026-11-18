using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;
using xmas402.Database;

namespace xmas402.Server.Persistence;

/// <summary>
/// Creates, validates, repairs and backs up the SQLite database file.
/// </summary>
/// <remarks>
/// SQLite is a single file database with no server process. Every process that opens the file is a writer,
/// so the application must make sure the file is a consistent database before it starts using it and must
/// create backups through SQLite itself (a raw file copy of a live database is not a valid backup).
/// </remarks>
internal sealed partial class SqliteDatabaseMaintenance(
    ApplicationDbContext dbContext,
    SqliteDatabaseOptions options,
    ILogger<SqliteDatabaseMaintenance> logger)
{
    private const string QuickCheckCommand = "PRAGMA quick_check;";
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm", "-journal"];

    private readonly SemaphoreSlim _backupLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Verifies the database file, replaces it with a backup (or an empty database) when it is corrupt and applies pending migrations.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        logger.LogInformation("Using SQLite database '{DatabasePath}' with backups in '{BackupDirectory}'",
            options.DatabasePath, options.BackupDirectory);

        var databaseDirectory = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrEmpty(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        Directory.CreateDirectory(options.BackupDirectory);

        var hadDatabase = File.Exists(options.DatabasePath);

        if (options.IntegrityCheckOnStartup)
        {
            if (!hadDatabase)
            {
                RemoveStaleSidecarFiles();
            }
            else if (!IsHealthy(out var report))
            {
                logger.LogCritical(
                    "SQLite database '{DatabasePath}' is corrupt and will be replaced. Integrity check result: {Report}",
                    options.DatabasePath, report);

                Quarantine();
                RestoreLatestBackup();
            }
        }

        await ConfigureJournalAsync(cancellationToken);

        if (hadDatabase)
        {
            // Snapshot the database before applying migrations, so a failed migration can be rolled back manually.
            await BackupAsync(cancellationToken);
        }

        await MigrateAsync(cancellationToken);

        if (!hadDatabase)
        {
            await BackupAsync(cancellationToken);
        }

        _initialized = true;
    }

    /// <summary>
    /// Writes a transactionally consistent copy of the database using <c>VACUUM INTO</c> and prunes old backups.
    /// </summary>
    public async Task<string?> BackupAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.DatabasePath))
        {
            return null;
        }

        await _backupLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(options.BackupDirectory);

            var backupPath = Path.Combine(
                options.BackupDirectory,
                $"{Path.GetFileNameWithoutExtension(options.DatabasePath)}-{DateTime.UtcNow:yyyyMMddHHmmss}.db");

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            var temporaryPath = backupPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            try
            {
                await using (var connection = new SqliteConnection(BuildConnectionString(SqliteOpenMode.ReadWrite)))
                {
                    await connection.OpenAsync(cancellationToken);

                    await using var command = connection.CreateCommand();
                    command.CommandText = "VACUUM INTO $target;";
                    command.Parameters.AddWithValue("$target", temporaryPath);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                if (!IsHealthy(temporaryPath, out var report))
                {
                    logger.LogError("Discarding SQLite backup '{BackupPath}' because it failed the integrity check: {Report}",
                        temporaryPath, report);
                    File.Delete(temporaryPath);
                    return null;
                }

                File.Move(temporaryPath, backupPath, overwrite: true);
            }
            catch (Exception ex) when (ex is SqliteException or IOException)
            {
                logger.LogError(ex, "Could not create a SQLite backup in '{BackupDirectory}'", options.BackupDirectory);

                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                return null;
            }

            logger.LogInformation("Created SQLite backup '{BackupPath}'", backupPath);
            PruneOldBackups();

            return backupPath;
        }
        finally
        {
            _backupLock.Release();
        }
    }

    private async Task ConfigureJournalAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(BuildConnectionString(SqliteOpenMode.ReadWriteCreate));
        await connection.OpenAsync(cancellationToken);

        // Write-ahead logging lets readers and the single writer work concurrently. The setting is stored in the
        // database file, so it applies to every connection. It is not available on every file system (network
        // shares, for example), in which case SQLite silently keeps using the rollback journal.
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";

        var journalMode = await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(journalMode as string, "wal", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("SQLite journal mode is '{JournalMode}' instead of 'WAL'; concurrent access may fail",
                journalMode);
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dbContext.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5 /* SQLITE_BUSY */ && attempt < maxAttempts)
            {
                logger.LogWarning(ex, "The database is busy while applying migrations, retrying (attempt {Attempt}/{MaxAttempts})",
                    attempt, maxAttempts);

                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Runs <c>PRAGMA quick_check</c> on a database file without leaving pooled connections behind.
    /// </summary>
    private bool IsHealthy(out string report) => IsHealthy(options.DatabasePath, out report);

    private bool IsHealthy(string databasePath, out string report)
    {
        report = string.Empty;

        try
        {
            if (!HasSqliteHeader(databasePath))
            {
                report = "The file does not start with a SQLite header.";
                return false;
            }

            using var connection = new SqliteConnection(BuildConnectionString(SqliteOpenMode.ReadWrite, databasePath));
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = QuickCheckCommand;

            using var reader = command.ExecuteReader();

            var messages = new List<string>();
            while (reader.Read())
            {
                messages.Add(reader.GetString(0));
            }

            report = messages.Count == 0 ? "quick_check returned no rows." : string.Join("; ", messages);

            return messages is ["ok"];
        }
        catch (SqliteException ex)
        {
            report = ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            report = ex.Message;
            return false;
        }
    }

    private static bool HasSqliteHeader(string databasePath)
    {
        using var stream = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[SqliteHeader.Length];

        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
            && header.SequenceEqual(SqliteHeader);
    }

    /// <summary>
    /// Moves the corrupt database (and its sidecar files) out of the way so the application keeps running.
    /// </summary>
    private void Quarantine()
    {
        SqliteConnection.ClearAllPools();

        var suffix = $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";

        foreach (var path in new[] { options.DatabasePath }.Concat(SidecarSuffixes.Select(s => options.DatabasePath + s)))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Move(path, path + suffix, overwrite: true);
                logger.LogWarning("Moved '{SourcePath}' to '{DestinationPath}'", path, path + suffix);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Could not move the corrupt database file '{Path}'", path);
            }
        }
    }

    private void RestoreLatestBackup()
    {
        // Picking the newest usable snapshot also recovers a snapshot that still has a write-ahead log next to it.
        var backup = FindBackups(timestampedOnly: false).FirstOrDefault(candidate => IsHealthy(candidate, out _));

        if (backup == null)
        {
            logger.LogCritical("No valid SQLite backup is available, starting with an empty database");
            RemoveStaleSidecarFiles();
            return;
        }

        SqliteConnection.ClearAllPools();
        RemoveStaleSidecarFiles();

        try
        {
            File.Copy(backup, options.DatabasePath, overwrite: true);

            // A snapshot taken with VACUUM INTO is a single self-contained file, but older copies may still have a
            // write-ahead log next to them. Both files belong together, so copy the log along and let SQLite recover it.
            var backupLog = backup + "-wal";
            if (File.Exists(backupLog))
            {
                File.Copy(backupLog, options.DatabasePath + "-wal", overwrite: true);
            }

            logger.LogWarning(
                "Restored the SQLite database from backup '{BackupPath}'. Data written after this backup is lost",
                backup);
        }
        catch (IOException ex)
        {
            logger.LogCritical(ex, "Could not restore the SQLite database from backup '{BackupPath}'", backup);
        }
    }

    private void RemoveStaleSidecarFiles()
    {
        foreach (var suffix in SidecarSuffixes)
        {
            var path = options.DatabasePath + suffix;
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
                logger.LogWarning("Removed stale SQLite file '{Path}'", path);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Could not remove stale SQLite file '{Path}'", path);
            }
        }
    }

    private void PruneOldBackups()
    {
        // Only keep the newest usable backups; files that are not SQLite databases do not count towards the limit
        // and are left alone because they may be manual copies created by someone else.
        var staleBackups = FindBackups(timestampedOnly: true)
            .Where(HasSqliteHeader)
            .Skip(options.BackupsToKeep)
            .ToList();

        foreach (var staleBackup in staleBackups)
        {
            try
            {
                File.Delete(staleBackup);
                logger.LogInformation("Deleted old SQLite backup '{BackupPath}'", staleBackup);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete old SQLite backup '{BackupPath}'", staleBackup);
            }
        }
    }

    /// <summary>
    /// Lists backup candidates, newest first. Backups are named <c>&lt;database&gt;-yyyyMMddHHmmss.db</c>; when
    /// restoring, older copies that share the database file name are accepted as well.
    /// </summary>
    private IEnumerable<string> FindBackups(bool timestampedOnly)
    {
        if (!Directory.Exists(options.BackupDirectory))
        {
            return [];
        }

        var prefix = Path.GetFileNameWithoutExtension(options.DatabasePath);

        var candidates = Directory.EnumerateFiles(options.BackupDirectory, $"{prefix}*.db")
            .Where(path => !timestampedOnly || IsTimestampedBackup(path))
            .ToList();

        // Order by the timestamp in the file name rather than by the file system timestamp, so the newest
        // backup is picked even when files were copied around.
        return candidates
            .Select(path => (Path: path, Timestamp: GetBackupTimestamp(path)))
            .OrderByDescending(candidate => candidate.Timestamp)
            .Select(candidate => candidate.Path);
    }

    private bool IsTimestampedBackup(string path) => BackupFileNamePattern().IsMatch(Path.GetFileName(path));

    private DateTime GetBackupTimestamp(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var prefix = Path.GetFileNameWithoutExtension(options.DatabasePath);

        if (name.Length == prefix.Length + 15
            && DateTime.TryParseExact(name[^14..], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
        {
            return timestamp;
        }

        return File.GetLastWriteTimeUtc(path);
    }

    [GeneratedRegex(@"-\d{14}\.db$")]
    private static partial Regex BackupFileNamePattern();

    private string BuildConnectionString(SqliteOpenMode mode, string? databasePath = null)
    {
        var builder = new SqliteConnectionStringBuilder(options.ConnectionString)
        {
            DataSource = databasePath ?? options.DatabasePath,
            Mode = mode,
            Pooling = false,
        };

        return builder.ToString();
    }
}
