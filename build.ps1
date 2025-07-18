
# build.ps1 - Build script for Atomos
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Framework = "net9.0",
    [string]$OutputDir = "./publish/windows"
)

Write-Host "=== Atomos Build Script ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration"
Write-Host "Runtime: $Runtime"
Write-Host "Framework: $Framework"
Write-Host "Output Directory: $OutputDir"
Write-Host "Working from: $PSScriptRoot"

# Check dotnet
$dotnetVersion = dotnet --version
Write-Host "Using .NET version: $dotnetVersion"

# Create output directory
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "Created output directory"
}

# Define projects (now in the same directory as the script)
$projects = @(
    "Atomos.Watchdog",
    "Atomos.UI",
    "Atomos.ConsoleTooling",
    "Atomos.BackgroundWorker"
)

Write-Host "`nVerifying projects..."
foreach ($project in $projects) {
    if (Test-Path $project) {
        Write-Host "Found: $project"
    } else {
        Write-Host "Missing: $project"
    }
}

Write-Host "`nStarting builds..."
$successCount = 0

foreach ($project in $projects) {
    Write-Host "`nPublishing $project..."

    dotnet publish $project -c $Configuration -p:PublishSingleFile=true --self-contained=true -p:DebugType=None -p:DebugSymbols=false -r $Runtime -o $OutputDir -f $Framework

    if ($LASTEXITCODE -eq 0) {
        Write-Host "SUCCESS: $project"
        $successCount++
    } else {
        Write-Host "FAILED: $project"
    }
}

Write-Host "`n=== Summary ==="
Write-Host "Successful: $successCount of $($projects.Count)"

if ($successCount -eq $projects.Count) {
    Write-Host "All projects built successfully!"

    if (Test-Path $OutputDir) {
        Write-Host "`nOutput files:"
        Get-ChildItem $OutputDir -File | ForEach-Object {
            $sizeMB = [math]::Round($_.Length / 1MB, 2)
            Write-Host "$($_.Name) - $sizeMB MB"
        }
    }
    exit 0
} else {
    Write-Host "Some builds failed!"
    exit 1
}