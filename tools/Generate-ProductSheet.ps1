# Author: Claude
# Last revision: 2026-11-01
# Purpose: Rebuild the project code, then write the Markdown product data sheet of one material (all sizes, room/design/operating temperatures, all notes).
#
# Usage:
#   pwsh tools/Generate-ProductSheet.ps1 -Database <file.db> -MaterialId 177 -Design "300,350,400" -Operating "100,200,250" -Out sheet.md
#   pwsh tools/Generate-ProductSheet.ps1 -Database <file.db> -Specification SA-516 -Grade 70 -Room 20 -Out sheet.md
param(
    [Parameter(Mandatory = $true)]
    [string] $Database,

    [long] $MaterialId,
    [string] $Specification,
    [string] $Grade,
    [string] $Condition,
    [string] $Uns,

    [double] $Room = 20,
    [string] $Design = "",
    [string] $Operating = "",

    [string] $Out,

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot "ProductSheet/Generate-ProductSheet.fsx"
$bin = Join-Path $PSScriptRoot "ProductSheet/bin"
if (-not (Test-Path -LiteralPath $Database)) { throw "Database not found: $Database" }
# Comma separated lists stay strings: "pwsh -File" would otherwise read 350,400 as one number with thousands separators.
function ConvertTo-Temperatures([string] $List) {
    if ([string]::IsNullOrWhiteSpace($List)) { return @() }
    return @($List.Split([char[]]@(",", ";"), [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [double]::Parse($_.Trim(), [Globalization.CultureInfo]::InvariantCulture) })
}
$designValues = ConvertTo-Temperatures $Design
$operatingValues = ConvertTo-Temperatures $Operating
if ($designValues.Count -gt 3 -or $operatingValues.Count -gt 3) { throw "At most three design and three operating temperatures are allowed." }
if (-not $MaterialId -and -not $Specification) { throw "Give -MaterialId or -Specification." }

# The sheet is built by the project's own code (ProductSheet module), so rebuild it first.
if (Test-Path -LiteralPath $bin) { Remove-Item -LiteralPath $bin -Recurse -Force }
dotnet build (Join-Path $root "src/MaterialLibrary.Crud/MaterialLibrary.Crud.fsproj") -c $Configuration -f net10.0 -o $bin --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$arguments = @("--db", (Resolve-Path -LiteralPath $Database).Path, "--room", $Room.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($MaterialId) { $arguments += @("--material", $MaterialId) }
if ($Specification) { $arguments += @("--spec", $Specification) }
if ($Grade) { $arguments += @("--grade", $Grade) }
if ($Condition) { $arguments += @("--condition", $Condition) }
if ($Uns) { $arguments += @("--uns", $Uns) }
if ($designValues.Count -gt 0) { $arguments += @("--design", (($designValues | ForEach-Object { $_.ToString([Globalization.CultureInfo]::InvariantCulture) }) -join ",")) }
if ($operatingValues.Count -gt 0) { $arguments += @("--operating", (($operatingValues | ForEach-Object { $_.ToString([Globalization.CultureInfo]::InvariantCulture) }) -join ",")) }
if ($Out) { $arguments += @("--out", $Out) }

dotnet fsi $script @arguments
exit $LASTEXITCODE
