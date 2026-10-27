param([int]$Port = 5289)
$ErrorActionPreference = 'Stop'
$demoRoot = Split-Path -Parent $PSScriptRoot
$demoWebRoot = Join-Path $demoRoot 'HawalaExchange.Web'
$demoSettings = Get-Content -LiteralPath (Join-Path $demoWebRoot 'appsettings.Development.json') -Raw | ConvertFrom-Json
$demoConnection = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($demoSettings.ConnectionStrings.DefaultConnection)
$demoDatabaseName = 'HawalaExchangeQuettaDemo_20261005_f53b28da'
$demoConnection['Initial Catalog'] = $demoDatabaseName
# Open only the named demo database. Missing databases fail rather than being created by app startup.
$demoSql = [System.Data.SqlClient.SqlConnection]::new($demoConnection.ConnectionString)
try {
    $demoSql.Open()
    $demoCommand = $demoSql.CreateCommand()
    $demoCommand.CommandText = "SELECT COUNT(*) FROM Hawalas WHERE Notes LIKE 'DEMO set % - synthetic data'"
    if ($demoCommand.ExecuteScalar() -lt 1) { throw 'The selected database is not the expected synthetic demo.' }
} finally { $demoSql.Dispose() }
$demoVariables = @('ConnectionStrings__DefaultConnection', 'ASPNETCORE_ENVIRONMENT', 'DataProtection__KeyRingPath')
$demoPrevious = @{}
foreach ($demoVariable in $demoVariables) { $demoPrevious[$demoVariable] = [Environment]::GetEnvironmentVariable($demoVariable, 'Process') }
try {
    $env:ConnectionStrings__DefaultConnection = $demoConnection.ConnectionString
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DataProtection__KeyRingPath = Join-Path $env:LOCALAPPDATA 'HawalaExchangeCommissionDemo\Keys'
    Write-Host "DEMO ONLY: $demoDatabaseName"
    Write-Host "Open http://127.0.0.1:$Port/correspondents/1/forwarding-commission"
    # Loopback only, and a separate host/key ring to avoid sharing the normal localhost login.
    dotnet run --project (Join-Path $demoWebRoot 'HawalaExchange.Web.csproj') --no-launch-profile -- --urls "http://127.0.0.1:$Port" --contentRoot $demoWebRoot
    if ($LASTEXITCODE -ne 0) { throw 'Demo application exited with an error.' }
} finally {
    foreach ($demoVariable in $demoVariables) { [Environment]::SetEnvironmentVariable($demoVariable, $demoPrevious[$demoVariable], 'Process') }
}
