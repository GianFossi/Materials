# Author: Claude
# Last revision: 2026-11-01
# Purpose: Regenerate the shipped reference database materials_2025.db from database/sources (CSV exports) and the physical-property XML in database/data.
#
# Usage:
#   pwsh tools/Build-Reference.ps1 -Output "$env:TEMP/materials_2025.db"        # build to a scratch file and compare
#   pwsh tools/Build-Reference.ps1 -Force                                       # replace database/data/materials_2025.db
# After a replacement update docs/asme-2025-import-verification.json from the summary this script prints and rerun the tests.
param(
    [string] $Sources,
    [string] $DataFolder,
    [string] $Output,
    [string] $Edition = "2025",
    [string] $Summary,
    [switch] $Force
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
if (-not $Sources) { $Sources = Join-Path $root "database/sources" }
if (-not $DataFolder) { $DataFolder = Join-Path $root "database/data" }
if (-not $Output) { $Output = Join-Path $DataFolder "materials_2025.db" }
if (-not $Summary) { $Summary = Join-Path ([IO.Path]::GetTempPath()) "materials_2025.summary.json" }
foreach ($path in @($Sources, $DataFolder)) { if (-not (Test-Path -LiteralPath $path)) { throw "Folder not found: $path" } }

$bin = Join-Path $PSScriptRoot "ReferenceBuild/bin"
$generated = Join-Path ([IO.Path]::GetTempPath()) ("generated-" + [Guid]::NewGuid().ToString("N") + ".db")

# 1. ASME import from the CSV exports (the updater refuses to overwrite an existing file, hence the new temporary name).
dotnet run --project (Join-Path $root "tools/MaterialLibrary.DatabaseUpdater") -c Release -- --sourcedir $Sources --edition $Edition --newdatabasefile $generated --report ([IO.Path]::ChangeExtension($generated, ".txt"))
if ($LASTEXITCODE -ne 0) { throw "DatabaseUpdater failed." }

# 2-4. XML import without manual review, data and provenance copied into a clean database, checks.
if (Test-Path -LiteralPath $bin) { Remove-Item -LiteralPath $bin -Recurse -Force }
dotnet build (Join-Path $root "src/MaterialLibrary.Crud/MaterialLibrary.Crud.fsproj") -c Release -f net10.0 -o $bin --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$arguments = @("--generated", $generated, "--data", $DataFolder, "--output", $Output, "--summary", $Summary)
if ($Force) { $arguments += "--force" }
dotnet fsi (Join-Path $PSScriptRoot "ReferenceBuild/Build-Reference.fsx") @arguments
$code = $LASTEXITCODE
Remove-Item -LiteralPath $generated, ([IO.Path]::ChangeExtension($generated, ".txt")) -Force -ErrorAction SilentlyContinue
if ($code -eq 0) { Write-Host "Summary: $Summary"; Get-Content -LiteralPath $Summary }
exit $code
