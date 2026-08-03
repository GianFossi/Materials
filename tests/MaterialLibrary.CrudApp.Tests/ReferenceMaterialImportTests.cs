using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MaterialLibrary.CrudApp.Tests;

/// <summary>
/// Covers importing one of the shipped ASME reference materials, and finding it by any identifier.
/// </summary>
/// <remarks>
/// <para>
/// Selecting a reference row used to fail with "exists in the reference tables but has not been
/// saved by this application", because reads went only through the document store. These tests pin
/// the fallback that assembles such a material from the reference tables instead, and the search
/// that locates a row by ID, specification, grade, class/condition/tempering, UNS, or full name.
/// </para>
/// <para>
/// They run against the real 2129-material fixture rather than a hand-built stub, because the
/// hydration path reads several pivoted tables and a simplified schema would not exercise it.
/// The fixture is copied first, so the file itself is never modified.
/// </para>
/// </remarks>
public sealed class ReferenceMaterialImportTests : IDisposable
{
    /// <summary>Fixture material carrying a populated specification, grade, class, and UNS.</summary>
    private const long SampleId = 77;

    private const string SampleSpecification = "SA-350";
    private const string SampleGrade = "LF1";
    private const string SampleClass = "1";
    private const string SampleUns = "K03009";
    private const string SampleName = "SA-350 LF1 1 K03009";

    private readonly string _directory;
    private readonly string _database;

    /// <summary>Copies the shared fixture into an isolated directory.</summary>
    public ReferenceMaterialImportTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "refimp-" + Guid.NewGuid().ToString("N")[..8]);
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

    /// <summary>Opens the manager on the copied fixture and waits for the list to load.</summary>
    /// <param name="dialogs">Dialog stub driving the flow.</param>
    /// <returns>An opened view model.</returns>
    private DatabaseViewModel OpenManager(StubDialogs dialogs)
    {
        dialogs.OpenPath = _database;
        var viewModel = new DatabaseViewModel(dialogs, Array.Empty<Material>());
        viewModel.OpenDatabaseCommand.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while ((!viewModel.IsOpen || viewModel.Materials.Count == 0) && DateTime.UtcNow < deadline)
        {
            System.Threading.Thread.Sleep(25);
        }

        Assert.True(viewModel.IsOpen, $"database did not open: {viewModel.StatusMessage}");
        return viewModel;
    }

    [Fact]
    public void ReferenceMaterialsAreListedWithTheirFullIdentity()
    {
        var viewModel = OpenManager(new StubDialogs());

        Assert.Equal(2129, viewModel.Materials.Count);

        var sample = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        Assert.Equal(SampleSpecification, sample.Specification);
        Assert.Equal(SampleGrade, sample.Grade);
        Assert.Equal(SampleClass, sample.ClassConditionTemper);
        Assert.Equal(SampleUns, sample.Uns);
        // Reference rows have no stored name, so it is composed the way the domain composes it.
        Assert.Equal(SampleName, sample.Name);
        Assert.Equal("ASME reference", sample.Origin);
    }

    [Theory]
    [InlineData("77")]                      // database ID
    [InlineData("K03009")]                  // UNS
    [InlineData("SA-350 LF1 1 K03009")]     // complete name
    [InlineData("sa-350 lf1 k03009")]       // case-insensitive, multiple terms
    public void SearchFindsTheMaterialByAnyIdentifier(string term)
    {
        var viewModel = OpenManager(new StubDialogs());

        viewModel.MaterialSearch = term;

        Assert.Contains(viewModel.Materials, m => m.DatabaseId == SampleId);
    }

    [Fact]
    public void SearchNarrowsBySpecificationAndGrade()
    {
        var viewModel = OpenManager(new StubDialogs());

        viewModel.MaterialSearch = SampleSpecification;
        var bySpecification = viewModel.Materials.Count;
        Assert.True(bySpecification > 0);
        Assert.True(bySpecification < 2129, "specification alone should not match everything");

        // Adding a term must narrow the result, never widen it.
        viewModel.MaterialSearch = $"{SampleSpecification} {SampleGrade}";
        Assert.True(viewModel.Materials.Count <= bySpecification);
        Assert.Contains(viewModel.Materials, m => m.DatabaseId == SampleId);
    }

    [Fact]
    public void ClearingTheSearchRestoresTheFullList()
    {
        var viewModel = OpenManager(new StubDialogs());

        viewModel.MaterialSearch = "no-such-material-anywhere";
        Assert.Empty(viewModel.Materials);

        viewModel.MaterialSearch = string.Empty;
        Assert.Equal(2129, viewModel.Materials.Count);
    }

    [Fact]
    public void SelectingAReferenceRowAndImportingYieldsAUsableMaterial()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ImportSelectedCommand.Execute(null);

        Assert.Empty(dialogs.Errors);
        Assert.Single(viewModel.ImportedMaterials);

        var material = viewModel.ImportedMaterials[0];
        Assert.Equal(SampleName, material.Name);
        Assert.Equal(SampleSpecification, material.Specification);

        // Assembled from the pivoted reference tables rather than from a stored document.
        Assert.True(
            material.StrengthProperties.TensileProperties.Length > 0,
            "the hydrated material carried no tensile rows");

        // The status names the source, because a hydrated reference material is not the full object.
        Assert.Contains("ASME reference tables", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void HydratedMaterialCanBeSavedBackAndThenReadsFromItsDocument()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ImportSelectedCommand.Execute(null);
        var hydrated = viewModel.ImportedMaterials[0];

        var working = viewModel.WorkingPathDisplay;
        Assert.False(MaterialDatabaseCrud.hasStoredDocument(working, hydrated.Id).ResultValue);

        // Saving it gives the material a document, so subsequent reads become lossless.
        var upsert = MaterialDatabaseCrud.upsertMaterial(working, hydrated);
        Assert.True(upsert.IsOk, upsert.IsOk ? string.Empty : upsert.ErrorValue.ToString());
        Assert.True(MaterialDatabaseCrud.hasStoredDocument(working, hydrated.Id).ResultValue);

        var reread = MaterialDatabaseCrud.readMaterial(working, hydrated.Id);
        Assert.True(reread.IsOk);
        Assert.Equal(
            MaterialSerialization.toJsonString(hydrated),
            MaterialSerialization.toJsonString(reread.ResultValue));
    }

    [Fact]
    public void TheFixtureFileIsNeverModified()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "asme_materials.working.db");
        var before = new FileInfo(fixture).Length;

        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);
        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ImportSelectedCommand.Execute(null);

        SqliteConnection.ClearAllPools();
        Assert.Equal(before, new FileInfo(fixture).Length);
    }

    [Fact]
    public void HydratedMaterialCarriesTheFullPhysicalPropertySet()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ImportSelectedCommand.Execute(null);

        var physical = viewModel.ImportedMaterials[0].PhysicalProperties;

        // Read from the group-keyed and material-keyed pivoted tables, not just the identity row.
        Assert.True(physical.ThermalExpansionTable.Length > 0, "no thermal-expansion rows");
        Assert.True(physical.ElasticModulusTable.Length > 0, "no elastic-modulus rows");
        Assert.True(physical.DensityTable.Length > 0, "no density rows");
        Assert.NotNull(physical.SpecificHeatTable);
        Assert.NotNull(physical.ThermalConductivityTable);
    }

    [Fact]
    public void HydratedPhysicalPropertiesAreConvertedToTheProjectUnits()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ImportSelectedCommand.Execute(null);

        var physical = viewModel.ImportedMaterials[0].PhysicalProperties;

        // The database stores um/m/degC; the domain's fixed unit is 1/degC.
        var expansion = physical.ThermalExpansionTable.ToList()[0].ExpansionCoefficient;
        Assert.InRange(expansion, 5e-6, 3e-5);

        // The database stores GPa; the domain's fixed unit is MPa.
        var modulus = physical.ElasticModulusTable.ToList()[0].ElasticModulus;
        Assert.InRange(modulus, 100_000, 250_000);

        // Poisson ratio is carried across from the Materials row.
        Assert.NotNull(physical.ElasticModulusTable.ToList()[0].PoissonRatio);

        // Specific heat and conductivity already match the domain units.
        Assert.InRange(physical.SpecificHeatTable.Value.ToList()[0].SpecificHeat, 100, 2000);
        Assert.InRange(physical.ThermalConductivityTable.Value.ToList()[0].Item2, 1, 500);
        Assert.InRange(physical.DensityTable.ToList()[0].Density, 1000, 25000);
    }

    [Fact]
    public void HydratedMaterialCarriesItsWeldingClassification()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ImportSelectedCommand.Execute(null);

        // Read from DataTableASME, which the previous hydration ignored entirely.
        Assert.NotNull(viewModel.ImportedMaterials[0].WeldingInfo);
    }

    [Fact]
    public void ShowRawRowsFollowsTheSelectedMaterialAndSwitchesTab()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        Assert.False(viewModel.HasMaterialFilter);
        Assert.Equal(0, viewModel.SelectedTabIndex);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ShowRawRowsForSelectedCommand.Execute(null);

        Assert.True(viewModel.HasMaterialFilter);
        Assert.Equal(SampleId, viewModel.MaterialIdFilter);
        // Index 1 is the Raw Tables tab.
        Assert.Equal(1, viewModel.SelectedTabIndex);
        Assert.Contains(SampleId.ToString(), viewModel.MaterialFilterDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingTheMaterialFilterRestoresAllRows()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ShowRawRowsForSelectedCommand.Execute(null);
        Assert.True(viewModel.HasMaterialFilter);

        viewModel.ClearMaterialFilterCommand.Execute(null);

        Assert.False(viewModel.HasMaterialFilter);
        Assert.Null(viewModel.MaterialIdFilter);
        Assert.Contains("all rows", viewModel.MaterialFilterDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaterialFilterRestrictsRawTableRowsToThatMaterial()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        // A table that carries a MaterialID link.
        viewModel.SelectedTable = viewModel.Tables.Single(t => t.Name == "YieldStrengthTable");
        WaitForTable(viewModel);
        var unfiltered = viewModel.TableRowCount;
        Assert.True(unfiltered > 1, "fixture should hold many yield-strength rows");

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ShowRawRowsForSelectedCommand.Execute(null);
        WaitForTable(viewModel);

        Assert.True(viewModel.TableRowCount < unfiltered, "the material filter did not narrow the table");
        Assert.True(viewModel.TableRowCount > 0, "the material filter removed everything");
    }

    /// <summary>Waits for the raw-table load, which runs detached from the caller.</summary>
    /// <param name="viewModel">View model being driven.</param>
    private static void WaitForTable(DatabaseViewModel viewModel)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (viewModel.TableRows is null && DateTime.UtcNow < deadline)
        {
            System.Threading.Thread.Sleep(25);
        }

        // Give the detached refresh a moment to publish the row count as well.
        System.Threading.Thread.Sleep(250);
    }

    [Fact]
    public void HydratedMaterialCarriesThermalDiffusivityInSiUnits()
    {
        var dialogs = new StubDialogs();
        var viewModel = OpenManager(dialogs);

        viewModel.SelectedMaterial = viewModel.Materials.Single(m => m.DatabaseId == SampleId);
        viewModel.ImportSelectedCommand.Execute(null);

        var diffusivity = viewModel.ImportedMaterials[0].PhysicalProperties.ThermalDiffusivityTable;

        Assert.NotNull(diffusivity);
        Assert.True(diffusivity.Value.Length > 0, "no thermal-diffusivity rows");

        // The database publishes mm^2/s; the domain uses m^2/s, matching how thermal expansion is
        // converted. Steel sits around 1e-5 m^2/s, so a missing conversion would land far outside.
        Assert.InRange(diffusivity.Value.ToList()[0].Item2, 1e-6, 1e-4);
    }

    /// <summary>Dialog stub answering from fixed settings and recording errors.</summary>
    private sealed class StubDialogs : IDialogService
    {
        /// <summary>Path returned by the open-file dialog.</summary>
        public string? OpenPath { get; set; }

        /// <summary>Errors the view model reported.</summary>
        public List<string> Errors { get; } = [];

        /// <inheritdoc />
        public string? AskOpenPath(string title, string filter) => OpenPath;

        /// <inheritdoc />
        public string? AskSavePath(string title, string filter, string? suggestedPath) => null;

        /// <inheritdoc />
        public void ShowError(string message) => Errors.Add(message);

        /// <inheritdoc />
        public void ShowInformation(string message) { }

        /// <inheritdoc />
        public bool ConfirmDelete(string materialId) => true;

        /// <inheritdoc />
        public bool ConfirmDestructiveSql(string sql) => true;

        /// <inheritdoc />
        public bool ConfirmOverwriteReference(string path) => false;

        /// <inheritdoc />
        public bool ConfirmDiscardChanges(string context) => true;

        /// <inheritdoc />
        public Material? EditMaterial(Material? existing) => null;

        /// <inheritdoc />
        public Material? EditMaterialTables(Material material) => material;

        /// <inheritdoc />
        public IReadOnlyList<Material>? ManageDatabase(IReadOnlyList<Material> currentMaterials) => null;
    }
}
