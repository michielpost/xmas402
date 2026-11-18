using Microsoft.Data.Sqlite;

namespace xmas402.Server.Persistence;

/// <summary>
/// Resolved settings for the SQLite database file used by the application.
/// </summary>
internal sealed class SqliteDatabaseOptions
{
    private const string BackupDirectoryKey = "Database:BackupDirectory";
    private const string BackupsToKeepKey = "Database:BackupsToKeep";
    private const string BackupIntervalMinutesKey = "Database:BackupIntervalMinutes";
    private const string IntegrityCheckOnStartupKey = "Database:IntegrityCheckOnStartup";

    public required string ConnectionString { get; init; }

    public required string DatabasePath { get; init; }

    public required string BackupDirectory { get; init; }

    public int BackupsToKeep { get; init; } = 10;

    public TimeSpan BackupInterval { get; init; } = TimeSpan.FromHours(6);

    public bool IntegrityCheckOnStartup { get; init; } = true;

    public static SqliteDatabaseOptions Create(string connectionString, IConfiguration configuration, string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("The 'DefaultConnection' connection string does not specify a 'Data Source'.");
        }

        var isInMemory = builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || builder.Mode == SqliteOpenMode.Memory;

        // Resolve relative paths against the content root instead of the process working directory.
        // Otherwise the app, the EF tooling and additional app instances can silently end up on different files.
        var databasePath = isInMemory || Path.IsPathRooted(builder.DataSource)
            ? builder.DataSource
            : Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));

        if (!isInMemory)
        {
            builder.DataSource = databasePath;
        }

        var databaseDirectory = isInMemory
            ? contentRootPath
            : Path.GetDirectoryName(databasePath) ?? contentRootPath;

        var backupDirectory = configuration[BackupDirectoryKey];
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            backupDirectory = Path.Combine(databaseDirectory, "backup");
        }
        else if (!Path.IsPathRooted(backupDirectory))
        {
            backupDirectory = Path.GetFullPath(Path.Combine(contentRootPath, backupDirectory));
        }

        var backupsToKeep = int.TryParse(configuration[BackupsToKeepKey], out var configuredBackups) && configuredBackups > 0
            ? configuredBackups
            : 10;

        var backupInterval = int.TryParse(configuration[BackupIntervalMinutesKey], out var configuredInterval) && configuredInterval > 0
            ? TimeSpan.FromMinutes(configuredInterval)
            : TimeSpan.FromHours(6);

        var integrityCheck = !bool.TryParse(configuration[IntegrityCheckOnStartupKey], out var configuredIntegrityCheck)
            || configuredIntegrityCheck;

        return new SqliteDatabaseOptions
        {
            ConnectionString = builder.ToString(),
            DatabasePath = databasePath,
            BackupDirectory = backupDirectory,
            BackupsToKeep = backupsToKeep,
            BackupInterval = backupInterval,
            IntegrityCheckOnStartup = integrityCheck,
        };
    }
}
