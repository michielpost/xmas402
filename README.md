# xmas402
Connecting Humans and AI Agents via USDC on Base.


https://www.xmas402.com


## Development
This project uses dotnet and EF Core with SQLite

### Migrations
Run in the root directory:
```
dotnet ef --startup-project xmas402/Server --project xmas402.Database migrations add <Name>
```

### Database and backups
The application stores everything in a single SQLite file. The location comes from the
`DefaultConnection` connection string (`App_Data/xmas402-live.db` by default); a relative path is
resolved from the application content root, so the server always uses the same file no matter which
directory it was started from.

On startup the server validates the file (`PRAGMA quick_check`), moves a corrupt file aside as
`<database>.corrupt-<timestamp>` and restores the newest valid backup, snapshots the database before
applying migrations and switches the file to write-ahead logging so readers and the single writer can
work side by side (see `xmas402/Server/Persistence/SqliteDatabaseMaintenance.cs`).

Backups are written with `VACUUM INTO` to `App_Data/backup/xmas402-live-<yyyyMMddHHmmss>.db`, which
produces a consistent snapshot; a plain file copy of a live database is not a valid backup. A new
backup is created on startup and then every `BackupIntervalMinutes`, and only the newest
`BackupsToKeep` backups are kept.

These settings can be changed in `appsettings.json`:

```json
"Database": {
  "BackupDirectory": "App_Data/backup",
  "BackupsToKeep": 10,
  "BackupIntervalMinutes": 360,
  "IntegrityCheckOnStartup": true
}
```

`BackupDirectory` is optional and defaults to a `backup` folder next to the database file.

To restore a backup manually, stop the server, copy the backup over `App_Data/xmas402-live.db` and
delete the `xmas402-live.db-wal` and `xmas402-live.db-shm` files next to it.