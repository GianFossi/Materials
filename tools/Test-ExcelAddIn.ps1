param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("x86", "x64")]
    [string] $Bitness,

    [Parameter(Mandatory = $true)]
    [string] $WorkbookPath,

    [Parameter(Mandatory = $true)]
    [string] $AddInPath,

    [string] $DatabasePath
)

$ErrorActionPreference = "Stop"

function Resolve-RequiredPath([string] $Path, [string] $Description) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        throw "$Description not found: $Path"
    }

    return $resolved.Path
}

function Release-ComObject([object] $ComObject) {
    if ($null -ne $ComObject) {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($ComObject)
    }
}

$workbook = Resolve-RequiredPath $WorkbookPath "Workbook"
$addin = Resolve-RequiredPath $AddInPath "Excel-DNA add-in"
$database = if ([string]::IsNullOrWhiteSpace($DatabasePath)) { $null } else { Resolve-RequiredPath $DatabasePath "ASME database" }
$workbookCopy = Join-Path ([System.IO.Path]::GetTempPath()) ("MaterialLibrary.ExcelSmoke." + [Guid]::NewGuid().ToString("N") + ".xlsx")
Copy-Item -LiteralPath $workbook -Destination $workbookCopy -Force

$excel = $null
$books = $null
$book = $null
$sheets = $null
$registeredAddIn = $false

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false

    Write-Host "Excel version: $($excel.Version)"
    Write-Host "Requested add-in bitness: $Bitness"
    Write-Host "Workbook: $workbook"
    Write-Host "Temporary workbook: $workbookCopy"
    Write-Host "Add-in: $addin"
    if ($null -ne $database) {
        Write-Host "ASME database: $database"
    }

    $registeredAddIn = [bool]$excel.RegisterXLL($addin)
    if (-not $registeredAddIn) {
        throw "Excel refused to register the XLL add-in: $addin"
    }

    $books = $excel.Workbooks
    $book = $books.Open($workbookCopy, 0, $false)

    if ($null -ne $database) {
        $sheetsForRewrite = $book.Worksheets

        for ($sheetIndex = 1; $sheetIndex -le $sheetsForRewrite.Count; $sheetIndex++) {
            $sheet = $sheetsForRewrite.Item($sheetIndex)
            $used = $sheet.UsedRange

            for ($row = 1; $row -le $used.Rows.Count; $row++) {
                for ($column = 1; $column -le $used.Columns.Count; $column++) {
                    $cell = $used.Cells.Item($row, $column)
                    $formula = $cell.Formula

                    if ($formula -is [string] -and $formula -match "asme_materials\.db|ASME_Materials\.db") {
                        $cell.Formula = [regex]::Replace(
                            $formula,
                            '"[^"]*(?:asme_materials|ASME_Materials)\.db"',
                            '"' + $database + '"')
                    }

                    Release-ComObject $cell
                }
            }

            Release-ComObject $used
            Release-ComObject $sheet
        }

        Release-ComObject $sheetsForRewrite
    }

    $excel.CalculateFullRebuild()

    $sheets = $book.Worksheets
    $formulaCount = 0
    $errorCount = 0
    $errors = New-Object System.Collections.Generic.List[string]

    for ($sheetIndex = 1; $sheetIndex -le $sheets.Count; $sheetIndex++) {
        $sheet = $sheets.Item($sheetIndex)
        $used = $sheet.UsedRange

        for ($row = 1; $row -le $used.Rows.Count; $row++) {
            for ($column = 1; $column -le $used.Columns.Count; $column++) {
                $cell = $used.Cells.Item($row, $column)
                $formula = $cell.Formula

                if ($formula -is [string] -and $formula.StartsWith("=")) {
                    $formulaCount++
                    $text = [string]$cell.Text

                    if ($text -match "^#(VALUE!|NAME\?|REF!|DIV/0!|NUM!|N/A|NULL!)") {
                        $errorCount++
                        $address = $cell.Address($false, $false)
                        $errors.Add("$($sheet.Name)!$address = $text :: $formula")
                    }
                }

                Release-ComObject $cell
            }
        }

        Release-ComObject $used
        Release-ComObject $sheet
    }

    Write-Host "Formula cells checked: $formulaCount"

    if ($formulaCount -eq 0) {
        throw "No formula cells found in $workbook. The smoke test did not exercise the add-in."
    }

    if ($errorCount -gt 0) {
        Write-Host "Excel formula errors:"
        $errors | ForEach-Object { Write-Host "  $_" }
        throw "$errorCount formula cell(s) returned an Excel error."
    }

    Write-Host "Excel add-in smoke test passed for $Bitness."
}
finally {
    if ($null -ne $book) {
        $book.Close($false)
    }

    if ($null -ne $excel) {
        $excel.Quit()
    }

    Release-ComObject $sheets
    Release-ComObject $book
    Release-ComObject $books
    Release-ComObject $excel

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()

    if (Test-Path -LiteralPath $workbookCopy) {
        Remove-Item -LiteralPath $workbookCopy -Force
    }
}
