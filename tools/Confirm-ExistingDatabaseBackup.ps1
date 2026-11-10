#Requires -RunAsAdministrator
<#
One-time recovery for the old retry loop. Verifies an existing backup for today's
latest scheduled slot, then records completion in the deployed site's receipt
directory. Does not take a backup, delete files, or grant SQL permissions.
Deploy the fixed application before the next slot to prevent recurrence.
#>
[CmdletBinding()]
param(
    [string]$SqlServer = 'localhost',
    [string]$Database = 'HawalaExchangeQuettaDb',
    [string]$BackupDirectory = 'C:\San\K\Database',
    [string]$ReceiptDirectory = 'C:\inetpub\wwwroot\App_Data\DatabaseBackups',
    [string]$TimeZoneId = 'Asia/Kabul'
)

$ErrorActionPreference = 'Stop'
$localNow = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, $TimeZoneId)
$hour = if ($localNow.Hour -ge 17) { 17 } elseif ($localNow.Hour -ge 12) { 12 } else { 0 }
if ($hour -eq 0) { throw 'No backup slot is due yet today.' }
$slot = $localNow.ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture) + ('_{0:00}00' -f $hour)
$backupRoot = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\', '/')
$receiptRoot = [IO.Path]::GetFullPath($ReceiptDirectory)
if (-not [IO.Directory]::Exists($receiptRoot)) { throw 'The deployed backup receipt directory does not exist.' }
$marker = [IO.Path]::Combine($receiptRoot, "$slot.completed")
if ([IO.File]::Exists($marker)) { Write-Host "Slot $slot is already complete."; return }

$connectionSettings = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$connectionSettings.DataSource = $SqlServer
$connectionSettings.InitialCatalog = 'master'
$connectionSettings.IntegratedSecurity = $true
$connectionSettings.TrustServerCertificate = $true
$connectionSettings.ConnectTimeout = 15
$connection = [System.Data.SqlClient.SqlConnection]::new($connectionSettings.ConnectionString)
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 30
    $command.CommandText = @'
SELECT TOP (1000) f.physical_device_name
FROM msdb.dbo.backupset b
JOIN msdb.dbo.backupmediafamily f ON f.media_set_id = b.media_set_id
WHERE b.database_name = @database AND b.type = 'D'
  AND b.has_backup_checksums = 1 AND b.backup_finish_date IS NOT NULL
ORDER BY b.backup_finish_date DESC
'@
    $null = $command.Parameters.AddWithValue('@database', $Database)
    $reader = $command.ExecuteReader()
    $backupPath = $null
    try {
        while ($reader.Read()) {
            $candidate = $reader.GetString(0)
            if ([IO.Path]::GetDirectoryName($candidate).TrimEnd('\', '/') -ieq $backupRoot -and
                [IO.Path]::GetFileName($candidate) -match ('^hawala_' + [regex]::Escape($slot) + '_[a-fA-F0-9]+\.bak$') -and
                [IO.File]::Exists($candidate)) {
                $backupPath = $candidate
                break
            }
        }
    } finally { $reader.Dispose() }
    if (-not $backupPath) { throw "No completed SQL backup for slot $slot was found in this directory." }
    $command.Parameters.Clear()
    $command.CommandTimeout = 1800
    $command.CommandText = 'RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM'
    $null = $command.Parameters.AddWithValue('@path', $backupPath)
    $null = $command.ExecuteNonQuery()
    $temporary = $marker + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, $backupPath)
        [IO.File]::Move($temporary, $marker, $false)
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
    Write-Host "Verified $backupPath and recorded completion for $slot."
    Write-Host 'An already-running backup may finish; subsequent cycles will skip this slot.'
    Write-Host 'Deploy the fixed application before the next scheduled slot.'
} finally { $connection.Dispose() }
