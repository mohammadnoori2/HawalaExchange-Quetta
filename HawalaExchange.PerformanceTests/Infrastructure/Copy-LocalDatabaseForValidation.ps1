param([ValidateSet('HawalaExchangeQuettaDb')][string]$SourceDatabase='HawalaExchangeQuettaDb')
$ErrorActionPreference='Stop'
# Never overwrite a database or backup, and never change the source recovery/backup chain.
$suffix=Get-Date -Format 'yyyyMMdd_HHmmss'
$token=[Guid]::NewGuid().ToString('N').Substring(0,8)
$targetDatabase="HawalaExchangeQuettaTestCopy_${suffix}_${token}"
if($targetDatabase -notmatch '^HawalaExchangeQuettaTestCopy_[0-9]{8}_[0-9]{6}_[a-f0-9]{8}$'){throw 'Unsafe target database name.'}
$sql=[System.Data.SqlClient.SqlConnection]::new('Server=.;Database=master;Integrated Security=True;Encrypt=False;TrustServerCertificate=True')
function Query([string]$text){
    $command=$sql.CreateCommand();$command.CommandText=$text;$command.CommandTimeout=600
    $table=[System.Data.DataTable]::new();$reader=$command.ExecuteReader()
    try{$table.Load($reader)}finally{$reader.Dispose();$command.Dispose()}
    return ,$table
}
function Execute([string]$text){$command=$sql.CreateCommand();$command.CommandText=$text;$command.CommandTimeout=600;try{[void]$command.ExecuteNonQuery()}finally{$command.Dispose()}}
function Literal([string]$value){return "N'"+$value.Replace("'","''")+"'"}
try{
    $sql.Open()
    $paths=Query "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath')) BackupPath,CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath')) DataPath,CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath')) LogPath"
    $backup=Join-Path ([string]$paths.Rows[0].BackupPath) "${SourceDatabase}_validation_${suffix}_${token}.bak"
    if(Test-Path -LiteralPath $backup){throw 'Backup target already exists.'}
    $backupLiteral=Literal $backup
    Execute "BACKUP DATABASE [$SourceDatabase] TO DISK=$backupLiteral WITH COPY_ONLY,CHECKSUM,STATS=10"
    Execute "RESTORE VERIFYONLY FROM DISK=$backupLiteral WITH CHECKSUM"
    $files=Query "RESTORE FILELISTONLY FROM DISK=$backupLiteral"
    $moves=@()
    foreach($file in $files.Rows){
        if($file.Type -notin @('D','L')){throw 'Unsupported database file type; restore requires manual review.'}
        $directory=if($file.Type -eq 'L'){[string]$paths.Rows[0].LogPath}else{[string]$paths.Rows[0].DataPath}
        $extension=if($file.Type -eq 'L'){'.ldf'}elseif($file.FileId -eq 1){'.mdf'}else{'.ndf'}
        $destination=Join-Path $directory ($targetDatabase+'_'+$file.FileId+$extension)
        if(Test-Path -LiteralPath $destination){throw 'Restore file already exists.'}
        $moves+='MOVE '+(Literal ([string]$file.LogicalName))+' TO '+(Literal $destination)
    }
    $exists=Query "SELECT database_id FROM sys.databases WHERE name=$(Literal $targetDatabase)"
    if($exists.Rows.Count -ne 0){throw 'Restore database already exists.'}
    Execute "RESTORE DATABASE [$targetDatabase] FROM DISK=$backupLiteral WITH $($moves -join ','),CHECKSUM,RECOVERY,STATS=10"
    # Make database-wide message delivery unavailable in this local clone.
    Execute "ALTER DATABASE [$targetDatabase] SET DISABLE_BROKER WITH NO_WAIT"
    [pscustomobject]@{Source=$SourceDatabase;Target=$targetDatabase;Backup=$backup;Server='.'}|ConvertTo-Json -Compress
}finally{$sql.Dispose()}
