using System;
using System.IO;
using System.Threading;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using MaterialLibraryCrudApp.Views;
using MaterialLibraryCrudApp.ViewModels;
using MaterialLibraryCrudApp.Services;
using MaterialLibrary.Domain;
using MaterialLibrary.Domain.Database.Lookup;
using Xunit;

namespace MaterialLibraryCrudApp.Tests;

public sealed class WpfSmokeTests
{
    [Fact]
    public void MainWindowLoadsExpectedWorkflowControls()
    {
        RunOnSta(() =>
        {
            var application = Application.Current ?? new MaterialLibraryCrudApp.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources["ToolbarButtonStyle"] = new Style(typeof(Button));
            application.Resources["DialogButtonStyle"] = new Style(typeof(Button));
            application.Resources["FieldLabelStyle"] = new Style(typeof(TextBlock));
            application.Resources["FieldInputStyle"] = new Style(typeof(TextBox));

            var window = new MainWindow();
            window.Measure(new System.Windows.Size(1200, 700));
            window.Arrange(new System.Windows.Rect(0, 0, 1200, 700));
            window.UpdateLayout();
            Assert.NotNull(FindButton(window, "Compare Materials"));
            Assert.NotNull(FindButton(window, "Database..."));
            window.Close();
        });
    }

    [Fact]
    public void DatabaseWindowLoadsOperationalWorkflowControls()
    {
        RunOnSta(() =>
        {
            var viewModel = new DatabaseViewModel(new NoOpDialogs(), Array.Empty<Material>());
            var window = new DatabaseWindow(viewModel);
            window.Measure(new System.Windows.Size(1000, 700));
            window.Arrange(new System.Windows.Rect(0, 0, 1000, 700));
            window.UpdateLayout();
            Assert.NotNull(FindButton(window, "Plot"));
            Assert.NotNull(FindButton(window, "Export PNG"));
            Assert.NotNull(FindButton(window, "Undo Last Transaction"));
            Assert.NotNull(FindButton(window, "Cancel"));
            window.Close();
        });
    }

    [Fact]
    public void SuppliedFixtureOpensThroughDatabaseWorkflow()
    {
        RunOnSta(() =>
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "asme_materials.working.db");
            var dialogs = new NoOpDialogs { OpenPath = fixture };
            var viewModel = new DatabaseViewModel(dialogs, Array.Empty<Material>());
            viewModel.OpenDatabaseCommand.Execute(null);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) =>
            {
                if (viewModel.IsOpen || !viewModel.IsBusy) frame.Continue = false;
            }, Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
            Assert.True(viewModel.IsOpen, viewModel.StatusMessage);
            Assert.NotEmpty(viewModel.Tables);
        });
    }

    [Fact]
    public void AsmeEditorFindButtonHandlesRunSource()
    {
        RunOnSta(() =>
        {
            var button = new Button();
            var textBlock = new TextBlock();
            var run = new Run("Save");
            textBlock.Inlines.Add(run);
            button.Content = textBlock;
            var method = typeof(Asme2025EditorWindow).GetMethod("FindButton", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var found = method!.Invoke(null, new object[] { run }) as Button;
            Assert.Same(button, found);
        });
    }

    [Fact]
    public void ParseSourceNotesSplitsFormattedNotesIntoRows()
    {
        var text = "1A / G5 (Specific): Due to the relatively low yield strength...\n\nSecond paragraph.\n\n1A / T1 (General): Joint efficiency factor of 0.85.";
        var rows = Asme2025EditorViewModel.ParseSourceNotes(text);
        Assert.Equal(2, rows.Count);
        Assert.Equal(("1A", "G5"), (rows[0].SourceTable, rows[0].Code));
        Assert.Equal("Due to the relatively low yield strength...\n\nSecond paragraph.", rows[0].Text);
        Assert.Equal(("1A", "T1", "Joint efficiency factor of 0.85."), (rows[1].SourceTable, rows[1].Code, rows[1].Text));
        Assert.Empty(Asme2025EditorViewModel.ParseSourceNotes(""));
        Assert.Equal("Source", Asme2025EditorViewModel.ParseSourceNotes("XML: file.xml\nfree text").Single().SourceTable);
    }

    [Fact]
    public void PropertyCurveChoiceShowsUnboundedAndOpenSizeLimitsClearly()
    {
        var unbounded = new PropertyCurveChoice(new Asme2025Curve
        {
            Id = 103,
            Code = "S1",
            ProductForm = "SMLS. PIPE",
            S1StressLevel = "High",
            SizeAxis = "thickness-or-unspecified",
            SourceTable = "1A",
            Points = Array.Empty<Asme2025Point>()
        });
        Assert.Equal("Curve #103 · Seamless (SMLS. PIPE) · High Stress (G5)", unbounded.Title);
        Assert.Equal("Size: no bounds specified · Axis: thickness / unspecified", unbounded.SizeRangeLabel);
        Assert.Equal("Table 1A · 0 points", unbounded.SourceLabel);

        var seamless = new PropertyCurveChoice(new Asme2025Curve
        {
            Id = 104,
            ProductForm = "SMLS. PIPE",
            SizeMinimum = 10,
            SizeMaximum = 25,
            MinimumIncluded = false,
            MaximumIncluded = true,
            SizeAxis = "thickness-or-unspecified",
            SourceTable = "1B",
            Points = Array.Empty<Asme2025Point>()
        });
        var welded = new PropertyCurveChoice(new Asme2025Curve
        {
            Id = 105,
            ProductForm = "WLD. PIPE",
            SizeMinimum = 10,
            SizeMaximum = 25,
            MinimumIncluded = false,
            MaximumIncluded = true,
            SizeAxis = "thickness-or-unspecified",
            SourceTable = "1B",
            Points = Array.Empty<Asme2025Point>()
        });

        Assert.Equal("Size: > 10 mm · ≤ 25 mm · Axis: thickness / unspecified", seamless.SizeRangeLabel);
        Assert.Equal(seamless.SizeRangeLabel, welded.SizeRangeLabel);
        Assert.Equal("Curve #104 · Seamless (SMLS. PIPE)", seamless.Title);
        Assert.Equal("Curve #105 · Welded (WLD. PIPE)", welded.Title);

        var limited = new PropertyCurveChoice(new Asme2025Curve
        {
            Id = 106,
            Code = "S1",
            SourceTable = "1A",
            CodeLimits = new Asme2025CodeLimits { PNumber = "1", GroupNumber = "2", MaxTempSectionVIII1 = "538" },
            Points = Array.Empty<Asme2025Point>()
        });
        Assert.Equal("Table 1A · 0 points · P-No. 1 Gr. 2 · VIII-1 max 538 °C", limited.SourceLabel);
        var notPermitted = new PropertyCurveChoice(new Asme2025Curve
        {
            Id = 107,
            Code = "S2",
            SourceTable = "2A",
            CodeLimits = new Asme2025CodeLimits { MaxTempSectionVIII2 = "NP" },
            Points = Array.Empty<Asme2025Point>()
        });
        Assert.Equal("Table 2A · 0 points · VIII-2: not permitted", notPermitted.SourceLabel);
    }

    private static Button? FindButton(DependencyObject root, string content)
    {
        if (root is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal)) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var found = FindButton(child, content);
            if (found is not null) return found;
        }
        if (root is not System.Windows.Media.Visual && root is not System.Windows.Media.Media3D.Visual3D) return null;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindButton(System.Windows.Media.VisualTreeHelper.GetChild(root, i), content);
            if (found is not null) return found;
        }
        return null;
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class NoOpDialogs : IDialogService
    {
        public string? OpenPath { get; init; }
        public string? AskOpenPath(string title, string filter) => OpenPath;
        public string? AskSavePath(string title, string filter, string? suggestedPath) => null;
        public void ShowError(string message) { }
        public void ShowInformation(string message) { }
        public bool ConfirmDelete(string materialId) => false;
        public bool ConfirmDestructiveSql(string sql) => false;
        public bool ConfirmOverwriteReference(string path) => false;
        public bool ConfirmDiscardChanges(string context) => true;
        public Material? EditMaterial(Material? existing) => null;
        public Material? EditMaterialTables(Material material) => null;
        public IReadOnlyList<Material>? ManageDatabase(IReadOnlyList<Material> currentMaterials) => null;
    }
}
