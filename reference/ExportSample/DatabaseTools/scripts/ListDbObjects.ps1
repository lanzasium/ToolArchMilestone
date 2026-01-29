$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$rootDir = (Resolve-Path (Join-Path $scriptDir "..")).Path
$binDir = Join-Path $rootDir "bin"
$dataDir = Join-Path $rootDir "data"
$outputDir = Join-Path $rootDir "output"

$assemblyPath = Join-Path $binDir "System.Data.SQLite.dll"
$interopPath = Join-Path $binDir "SQLite.Interop.dll"
$dbPath = Join-Path $dataDir "ToolArchMilestone.db"
$outputPath = Join-Path $outputDir "db_objects.txt"

if (-not (Test-Path $assemblyPath)) {
    throw "Assembly System.Data.SQLite.dll non trovato in $binDir"
}

if (-not (Test-Path $interopPath)) {
    throw "SQLite.Interop.dll non trovato in $binDir"
}

if (-not (Test-Path $dbPath)) {
    throw "Database non trovato in $dataDir. Atteso $dbPath"
}

[System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null

$connectionString = "Data Source=$dbPath;Version=3;"
$conn = New-Object System.Data.SQLite.SQLiteConnection($connectionString)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT type, name FROM sqlite_master WHERE type IN ('table','view') ORDER BY type, name"
$reader = $cmd.ExecuteReader()
$lines = @()
while ($reader.Read()) {
    $type = $reader["type"].ToString()
    $name = $reader["name"].ToString()
    $lines += ("{0}:{1}" -f $type, $name)
}
$reader.Close()
$conn.Close()

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Set-Content -Path $outputPath -Value $lines
Write-Output "Elenco salvato in $outputPath"
