using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using MaterialLibraryCrudApp.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Exporting the visible results to CSV, JSON, and Excel.</summary>
public sealed partial class DatabaseViewModel
{

    private void ExportResultsCsv()
    {
        var path = _dialogService.AskSavePath("Export results as CSV", FileFilters.Csv, "results.csv");
        if (path is null) return;
        var view = SqlResults ?? TableRows;
        if (view is null) return;
        File.WriteAllLines(path, ExportRows(view, ","));
        StatusMessage = $"Exported results to {path}.";
        RecordAudit("Export CSV", path);
    }

    private void ExportResultsJson()
    {
        var path = _dialogService.AskSavePath("Export results as JSON", FileFilters.Json, "results.json");
        var view = SqlResults ?? TableRows;
        if (path is null || view is null) return;
        var rows = view.Cast<DataRowView>().Select(row => view.Table!.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => row[c.ColumnName] == DBNull.Value ? null : row[c.ColumnName])).ToList();
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        StatusMessage = $"Exported results to {path}.";
        RecordAudit("Export JSON", path);
    }

    private void ExportResultsExcel()
    {
        var path = _dialogService.AskSavePath("Export results for Excel", FileFilters.ExcelXml, "results.xlsx");
        var view = SqlResults ?? TableRows;
        if (path is null || view is null) return;
        var columns = view.Table!.Columns.Cast<DataColumn>().ToList();
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        AddZipEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        AddZipEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        AddZipEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Results\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        AddZipEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        var sheet = new System.Text.StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        var rowNumber = 1;
        sheet.Append($"<row r=\"{rowNumber++}\">");
        for (var i = 0; i < columns.Count; i++) sheet.Append(CellXml(i, rowNumber - 1, columns[i].ColumnName, false));
        sheet.Append("</row>");
        foreach (DataRowView row in view)
        {
            sheet.Append($"<row r=\"{rowNumber}\">");
            for (var i = 0; i < columns.Count; i++)
            {
                var value = row[columns[i].ColumnName];
                var numeric = value is byte or short or int or long or float or double or decimal;
                sheet.Append(CellXml(i, rowNumber, value == DBNull.Value ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, numeric));
            }
            sheet.Append("</row>");
            rowNumber++;
        }
        sheet.Append("</sheetData></worksheet>");
        AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        RecordAudit("Export Excel", path);
        StatusMessage = $"Exported native XLSX workbook to {path}.";
    }

    private static void AddZipEntry(System.IO.Compression.ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), System.Text.Encoding.UTF8);
        writer.Write(content);
    }

    private static string CellXml(int column, int row, string value, bool numeric)
    {
        var reference = string.Empty;
        var n = column + 1;
        while (n > 0) { n--; reference = (char)('A' + n % 26) + reference; n /= 26; }
        var escaped = System.Security.SecurityElement.Escape(value) ?? string.Empty;
        return numeric ? $"<c r=\"{reference}{row}\"><v>{escaped}</v></c>" : $"<c r=\"{reference}{row}\" t=\"inlineStr\"><is><t>{escaped}</t></is></c>";
    }

    private static IEnumerable<string> ExportRows(DataView view, string separator)
    {
        var columns = view.Table!.Columns.Cast<DataColumn>().ToList();
        yield return string.Join(separator, columns.Select(c => EscapeCsv(c.ColumnName, separator)));
        foreach (DataRowView row in view)
            yield return string.Join(separator, columns.Select(c => EscapeCsv(row[c.ColumnName], separator)));
    }

    private static string EscapeCsv(object value, string separator)
    {
        var text = value == DBNull.Value ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Contains(separator, StringComparison.Ordinal) || text.Contains('"') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}
