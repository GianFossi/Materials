using System;
using System.IO;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using Xunit;

namespace MaterialLibrary.CrudApp.Tests;

public sealed class ApplicationOptionsTests
{
    [Fact]
    public void OptionsXmlRoundTripsProductFormsEfficiencyAndDefaultPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MaterialLibrary.OptionsTests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "options.xml");
        try
        {
            var options = ApplicationOptions.Load(settingsPath);
            Assert.True(File.Exists(settingsPath));
            Assert.Equal(0.70, options.DefaultJointEfficiency);

            options.ProductForms = ["Plate", "Welded Pipe"];
            options.DefaultJointEfficiency = 0.65;
            options.FlexXmlFolder = Path.Combine(directory, "xml");
            options.DatabaseFilePath = Path.Combine(directory, "materials.db");
            options.Save();

            var loaded = ApplicationOptions.Load(settingsPath);
            Assert.Equal(new[] { "Plate", "Welded Pipe" }, loaded.ProductForms);
            Assert.Equal(0.65, loaded.DefaultJointEfficiency);
            Assert.Equal(options.FlexXmlFolder, loaded.FlexXmlFolder);
            Assert.Equal(options.DatabaseFilePath, loaded.DatabaseFilePath);

            var editor = new MaterialEditViewModel(null, loaded);
            Assert.Equal(loaded.ProductForms, editor.ProductFormChoices);
            editor.ProductForm = "Legacy product form";
            Assert.Contains("Legacy product form", editor.ProductFormChoices);
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
            if (File.Exists(settingsPath + ".tmp"))
            {
                File.Delete(settingsPath + ".tmp");
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}
