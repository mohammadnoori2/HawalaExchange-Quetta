# Automatic database backups

The web server takes a full backup at **12:00 and 17:00, Asia/Kabul**, every day.
This is enabled by default and works without an open browser. The web application
must remain running; suspended computers and stopped services cannot execute jobs.
The worker checks once per minute. On startup it catches up the latest due slot of
the current local day; earlier slots and previous days are not reconstructed.
Failures retry once per minute. Each backup is limited to 30 minutes. Once a file
is created, its path is saved separately in a `.created` receipt. Verification
failures retry that same file rather than taking another backup, including after
a web application restart. A `.completed` receipt means verification also passed.

Settings can be supplied in private appsettings or environment variables:

```json
{
  "DatabaseBackup": {
    "Enabled": true,
    "TimeZoneId": "Asia/Kabul",
    "Directory": "App_Data/DatabaseBackups",
    "PgDumpPath": "pg_dump",
    "SqlServerDirectory": null
  }
}
```

Relative directories resolve against the web application's content root. The
directory must be private and outside `wwwroot`; on Linux it is restricted to the
application user. Preserve it across deployments: it contains `.created` and `.completed` markers
and a process lock as well as PostgreSQL archives. Use a separate directory for
each database and share it between instances backing up the same database.
Keep this directory on a local filesystem that supports exclusive file locks.

For the Ubuntu deployment set this in the existing private `app.env`:

```bash
DatabaseBackup__Directory=/home/kcl/hawala-server/data/backups
DatabaseBackup__TimeZoneId=Asia/Kabul
DatabaseBackup__PgDumpPath=/usr/bin/pg_dump
```

## PostgreSQL

Install `pg_dump` on the **web host**, matching the database server's major version
(a newer compatible client also works). It reuses `ConnectionStrings:PostgreSQL`;
the password is passed through the child process environment, never command-line
arguments or logs. Host, port, database, username, SSL mode and root certificate are
mapped to libpq environment variables. If the deployment uses client certificate
authentication or other libpq-specific settings, configure the corresponding
`PGSSL*` environment variables on the web service too.

Files are compressed custom archives named `hawala_yyyyMMdd_1200.dump` and
`hawala_yyyyMMdd_1700.dump`. Incomplete `.partial` files never count as successful
backups and are removed on failure. A process crash may leave a partial file; the
next attempt replaces it. Restore into a separate database with `pg_restore` and
check application data before using a restored database. Roles and uploaded files
are outside this database backup.

## SQL Server

The backup runs through the existing `DefaultConnection`, using full `COPY_ONLY`
and `CHECKSUM`, followed by `RESTORE VERIFYONLY`. The connection user needs BACKUP
and VERIFYONLY permissions. A login may have BACKUP permission while VERIFYONLY
fails with `CREATE DATABASE permission denied in database 'master'`. That failure
does not mean the backup itself failed, and must never trigger a fresh file every
minute. An authorized maintenance account can perform verification separately;
the application does not automatically grant itself extra permissions.
Files are written on the **SQL Server host**, to its
default backup directory, or `DatabaseBackup:SqlServerDirectory` when supplied.
The directory must already exist and be writable by the SQL Server service
account. This setting is a path on the database host, even when the app runs on a
different machine. The app's local directory stores only schedule markers for
this provider. SQL filenames use a stable database identifier and scheduled slot,
such as `hawala_20261108_1200_<database-id>.bak`, so retries after a crash do not
create additional files. `INIT` replaces only that slot's backup set instead of
appending repeated sets. Do not delete receipts to request a second backup in the
same slot: the filename is intentionally reused. Failed attempts can leave
incomplete `.bak` files on SQL Server; only backups with a corresponding
completion marker passed verification. If a slot remains unverified when the
next slot becomes due, its `.created` receipt remains available for maintenance;
the scheduler proceeds with the new slot.

## Operation

For a deployed version with the old verification retry bug, an administrator with
SQL verification rights can run `tools/Confirm-ExistingDatabaseBackup.ps1` locally
on the Windows IIS/SQL host. It verifies the most recent existing backup for the
current due slot and writes a completion marker to stop subsequent duplicate
attempts for that slot. It never takes or deletes a backup or grants permissions.
Its defaults match the Quetta IIS deployment; inspect parameters before running.
An already-running backup can still finish. Deploy the fixed application before
the next slot, because this recovery script only handles the current slot.

Restart the web service after deploying this change or changing configuration.
Check service logs for `Database backup for ... completed` and confirm that backup
files exist. Provider error types are logged without connection strings or raw
provider diagnostics. No live backup or restore is triggered by the test suite.
Backup files are retained without automatic deletion; monitor available disk space
and copy successful backups to a separate protected storage location. A successful
job and VERIFYONLY do not replace a periodic restore drill.

References: [PostgreSQL pg_dump](https://www.postgresql.org/docs/current/app-pgdump.html),
[SQL Server BACKUP](https://learn.microsoft.com/en-us/sql/t-sql/statements/backup-transact-sql?view=sql-server-ver17).
