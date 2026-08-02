using System;
using System.IO;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialLibraryCrudApp.Views;
using MaterialLibraryCrudApp.ViewModels;
using MaterialLibraryCrudApp.Services;
using MaterialLibrary.Domain;
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
        public bool ConfirmDiscardChanges(string context) => true;
        public Material? EditMaterial(Material? existing) => null;
        public Material? EditMaterialTables(Material material) => null;
        public IReadOnlyList<Material>? ManageDatabase(IReadOnlyList<Material> currentMaterials) => null;
    }
}
