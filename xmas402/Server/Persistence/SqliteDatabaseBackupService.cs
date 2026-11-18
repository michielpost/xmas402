namespace xmas402.Server.Persistence;

/// <summary>
/// Creates a consistent SQLite backup on a fixed interval so a corrupt database can be restored.
/// </summary>
internal sealed class SqliteDatabaseBackupService(
    IServiceScopeFactory scopeFactory,
    SqliteDatabaseOptions options,
    ILogger<SqliteDatabaseBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.BackupInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<SqliteDatabaseMaintenance>();

                await maintenance.BackupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("SQLite backup service stopped");
        }
    }
}
