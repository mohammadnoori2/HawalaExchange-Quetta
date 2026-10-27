[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PublishPath = (Join-Path $PSScriptRoot '..\publish\navigation-optimized-20261025'),
    [string]$TargetPath = 'C:\inetpub\wwwroot'
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishPath).Path
$target = (Resolve-Path -LiteralPath $TargetPath).Path
$files = @('HawalaExchange.Web.dll', 'HawalaExchange.Infrastructure.dll',
    'HawalaExchange.Web.pdb', 'HawalaExchange.Infrastructure.pdb')
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath (Join-Path $source $file)) -or
        !(Test-Path -LiteralPath (Join-Path $target $file))) {
        throw "Missing deployment file: $file"
    }
}
if (!$PSCmdlet.ShouldProcess($target, 'Back up and replace the two navigation optimization assemblies, then restart the application pool')) { return }
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in PowerShell as Administrator.'
}

Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
$manager = [Microsoft.Web.Administration.ServerManager]::new()
try {
    $sites = @($manager.Sites | Where-Object {
        [Environment]::ExpandEnvironmentVariables($_.Applications['/'].VirtualDirectories['/'].PhysicalPath).TrimEnd('\') -ieq $target.TrimEnd('\')
    })
    if ($sites.Count -ne 1) { throw 'Expected exactly one IIS site at the target path.' }
    $pool = $manager.ApplicationPools[$sites[0].Applications['/'].ApplicationPoolName]
    $backup = Join-Path (Split-Path $target -Parent) ('HawalaExchange-Backups\navigation-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $target $file) -Destination (Join-Path $backup $file) }
    $stopped = $false
    try {
        if ($pool.State -ne 'Stopped') { [void]$pool.Stop() }
        $deadline = (Get-Date).AddSeconds(15)
        while ($pool.State -ne 'Stopped') {
            if ((Get-Date) -gt $deadline) { throw 'The application pool did not stop within 15 seconds.' }
            Start-Sleep -Milliseconds 200
        }
        $stopped = $true
        foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $target $file) -Force }
        [void]$pool.Start()
        $response = Invoke-WebRequest 'http://localhost/Account/Login' -UseBasicParsing -TimeoutSec 45
        if ($response.StatusCode -ne 200) { throw 'The updated site did not return HTTP 200.' }
        Write-Output "Deployment succeeded. Backup: $backup. Login page: HTTP 200."
    }
    catch {
        $failure = $_
        if ($stopped) {
            if ($pool.State -ne 'Stopped') { [void]$pool.Stop() }
            $deadline = (Get-Date).AddSeconds(15)
            while ($pool.State -ne 'Stopped' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
            if ($pool.State -eq 'Stopped') {
                foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $backup $file) -Destination (Join-Path $target $file) -Force }
                [void]$pool.Start()
            }
        }
        throw $failure
    }
}
finally { $manager.Dispose() }
