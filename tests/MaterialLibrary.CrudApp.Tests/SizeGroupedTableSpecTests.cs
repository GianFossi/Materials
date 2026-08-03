using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaterialLibrary.Domain;
using MaterialLibrary.Domain.Database.Lookup;
using MaterialLibraryCrudApp.Interop;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MaterialLibrary.CrudApp.Tests;

/// <summary>
/// Covers the two grids that show Sy/Su and the allowable stresses grouped by size band.
/// </summary>
/// <remarks>
/// <para>
/// The allowable-stress grid used to read <c>StrengthProperties.AllowableStresses</c>, which the
/// reference importer never populates, so it was always empty for an imported material. These tests
/// pin that the grids now read the size-banded datasets, and that a flattened grid regroups back
/// into the same datasets when committed.
/// </para>
/// <para>
/// They run against the real fixture, because the grouping only exists for materials whose ASME
/// tables publish more than one band.
/// </para>
/// </remarks>
public sealed class SizeGroupedTableSpecTests : IDisposable
{
    /// <summary>SA-325 bolting: two diameter bands in both the strength and allowable tables.</summary>
    private const long BandedMaterialId = 260;

    /// <summary>SA-334 7: carries note G5, so Division 1 splits into a normal and a high case.</summary>
    private const long HighAllowableMaterialId = 736;

    private readonly string _directory;
    private readonly string _database;

    /// <summary>Copies the shared fixture into an isolated directory.</summary>
    public SizeGroupedTableSpecTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "sizegrp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_directory);
        _database = Path.Combine(_directory, "asme_materials.db");

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "asme_materials.working.db"), _database);
    }

    /// <summary>Removes the temporary directory.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp directory must not fail the test run.
        }
    }

    /// <summary>Hydrates one reference material straight from the fixture.</summary>
    /// <param name="databaseId">Value of <c>Materials.ID</c>.</param>
    /// <returns>The hydrated material.</returns>
    private Material Load(long databaseId)
    {
        var result = AsmeMaterialRepository.findById(_database, databaseId);
        Assert.True(result.IsOk, result.IsOk ? string.Empty : result.ErrorValue.ToString());
        return result.ResultValue;
    }

    /// <summary>Finds a registered table spec by its title.</summary>
    /// <param name="title">Exact grid title.</param>
    /// <returns>The matching spec.</returns>
    private static MaterialTableSpec Spec(string title) =>
        MaterialTableSpecs.All.Single(spec => spec.Title == title);

    [Fact]
    public void AllowableStressGridIsPopulatedForAnImportedReferenceMaterial()
    {
        var material = Load(BandedMaterialId);
        var spec = Spec("Allowable stresses by size group (Div 1 / Div 2)");
        var rows = spec.Read(material);

        Assert.NotEmpty(rows);

        // Every row repeats its own division, case, and band, so a single line stands alone.
        Assert.All(rows, row => Assert.Equal(spec.Columns.Count, row.Length));
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row[0])));
        Assert.All(rows, row => Assert.Equal("Normal", row[1]));

        // Two diameter bands, each with its own curve.
        var bands = rows.Select(row => $"{row[2]}-{row[4]}").Distinct().ToList();
        Assert.Equal(2, bands.Count);
    }

    [Fact]
    public void AllowableStressGridSeparatesTheNormalAndHighDivisionOneCases()
    {
        var material = Load(HighAllowableMaterialId);
        var rows = Spec("Allowable stresses by size group (Div 1 / Div 2)").Read(material);

        var cases = rows
            .Where(row => row[0] == "VIII-1")
            .Select(row => row[1])
            .Distinct()
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.Equal<IReadOnlyList<string?>>(["High", "Normal"], cases);
    }

    [Fact]
    public void StrengthGridShowsSyAndSuPerSizeBand()
    {
        var material = Load(BandedMaterialId);
        var rows = Spec("Minimum strengths Sy / Su by size group").Read(material);

        Assert.NotEmpty(rows);
        Assert.Equal<IReadOnlyList<string?>>(
            ["Su", "Sy"],
            rows.Select(row => row[0]).Distinct().OrderBy(text => text, StringComparer.Ordinal).ToList());

        // The band bounds travel with each row, and the fixture's bands are closed on both sides.
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row[1])));
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row[3])));
        Assert.All(rows, row => Assert.Equal("yes", row[2]));
        Assert.All(rows, row => Assert.Equal("yes", row[4]));
    }

    [Fact]
    public void CommittingTheFlattenedGridRebuildsTheSameGroups()
    {
        var material = Load(BandedMaterialId);

        foreach (var title in new[]
                 {
                     "Minimum strengths Sy / Su by size group",
                     "Allowable stresses by size group (Div 1 / Div 2)",
                 })
        {
            var spec = Spec(title);
            var rows = spec.Read(material);
            var rewritten = spec.Write(material, rows);

            // Reading back what was just written must reproduce the grid cell for cell; anything
            // else means a group was split, merged, or lost on the way through.
            var reread = spec.Read(rewritten);

            Assert.Equal(rows.Count, reread.Count);

            for (var i = 0; i < rows.Count; i++)
            {
                Assert.Equal<IReadOnlyList<string?>>(rows[i], reread[i]);
            }
        }
    }

    [Fact]
    public void EditingASizeBoundMovesOnlyThatGroup()
    {
        var material = Load(BandedMaterialId);
        var spec = Spec("Minimum strengths Sy / Su by size group");
        var rows = spec.Read(material).Select(row => (string?[])row.Clone()).ToList();

        // Widen the first group's upper bound and mark it exclusive, on every row of that group.
        var firstKey = $"{rows[0][0]}|{rows[0][1]}|{rows[0][3]}";

        foreach (var row in rows.Where(row => $"{row[0]}|{row[1]}|{row[3]}" == firstKey))
        {
            row[3] = "99";
            row[4] = "no";
        }

        var reread = spec.Read(spec.Write(material, rows));

        Assert.Contains(reread, row => row[3] == "99" && row[4] == "no");

        // The untouched groups keep their own bounds rather than being folded into the edited one.
        Assert.Contains(reread, row => row[3] != "99");
        Assert.Equal(rows.Count, reread.Count);
    }
}
