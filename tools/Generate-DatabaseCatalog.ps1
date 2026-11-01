# Author: Claude
# Last revision: 2026-11-01
# Purpose: Rebuild the project code, then regenerate chapter 2 (table catalog) of docs/asme-materials-db.md from the real database schema.
#
# Usage:
#   pwsh tools/Generate-DatabaseCatalog.ps1            # rewrite the chapter
#   pwsh tools/Generate-DatabaseCatalog.ps1 -Check     # write nothing; exit code 1 when the chapter is out of date (CI)
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [switch] $Check
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot "DatabaseCatalog/Generate-Catalog.fsx"
$bin = Join-Path $PSScriptRoot "DatabaseCatalog/bin"
$reference = Join-Path $root "database/data/materials_2025.db"
$doc = Join-Path $root "docs/asme-materials-db.md"

foreach ($path in @($script, $reference, $doc)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "File not found: $path" }
}

# The generator calls the project's own code (ensureSchema, SqlAudit, XML importer), so build it fresh first.
if (Test-Path -LiteralPath $bin) { Remove-Item -LiteralPath $bin -Recurse -Force }
dotnet build (Join-Path $root "src/MaterialLibrary.Crud/MaterialLibrary.Crud.fsproj") -c $Configuration -f net10.0 -o $bin --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$arguments = @("--reference", $reference, "--doc", $doc)
if ($Check) { $arguments += "--check" }
dotnet fsi $script @arguments
exit $LASTEXITCODE
