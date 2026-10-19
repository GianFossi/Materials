using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace MaterialLibraryCrudApp.Services;

/// <summary>Persistent user preferences for the CRUD application.</summary>
public sealed class ApplicationOptions
{
    private static readonly string[] DefaultProductForms =
    [
        "Plate", "Pipe", "Tube", "Forging", "Bar", "Casting", "Fittings",
        "Seamless Pipe", "Welded Pipe", "Seamless Tube", "Welded Tube", "Bolting"
    ];

    /// <summary>Creates application options with the built-in defaults.</summary>
    public ApplicationOptions(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? GetDefaultSettingsPath();
        ProductForms = [.. DefaultProductForms];
        DefaultJointEfficiency = 0.70;
    }

    /// <summary>Path of the XML file used to persist these options.</summary>
    public string SettingsFilePath { get; private set; } = GetDefaultSettingsPath();

    /// <summary>Product forms offered by the material and ASME editors.</summary>
    public List<string> ProductForms { get; set; }

    /// <summary>Default efficiency for the longitudinal or pressure-critical welded joint, as a fraction.</summary>
    public double DefaultJointEfficiency { get; set; }

    /// <summary>Default folder containing XML files for physical properties and other imported data.</summary>
    public string FlexXmlFolder { get; set; } = string.Empty;

    /// <summary>Default SQLite database file to open.</summary>
    public string DatabaseFilePath { get; set; } = string.Empty;

    /// <summary>Loads preferences, creating an XML settings file with defaults on first run.</summary>
    public static ApplicationOptions Load(string? settingsFilePath = null)
    {
        var options = new ApplicationOptions(settingsFilePath);
        var path = options.SettingsFilePath;

        if (!File.Exists(path))
        {
            options.Save();
            return options;
        }

        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidDataException($"Settings file has no root element: {path}");
        var forms = root.Element("ProductForms")?.Elements("ProductForm")
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (forms is null || forms.Count == 0)
        {
            throw new InvalidDataException($"Settings file must contain at least one product form: {path}");
        }

        if (!double.TryParse(root.Element("DefaultJointEfficiency")?.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var efficiency)
            || !double.IsFinite(efficiency) || efficiency <= 0 || efficiency > 1)
        {
            throw new InvalidDataException($"Default joint efficiency must be greater than 0 and no greater than 1: {path}");
        }

        options.ProductForms = forms;
        options.DefaultJointEfficiency = efficiency;
        options.FlexXmlFolder = root.Element("FlexXmlFolder")?.Value.Trim() ?? string.Empty;
        options.DatabaseFilePath = root.Element("DatabaseFilePath")?.Value.Trim() ?? string.Empty;
        return options;
    }

    /// <summary>Writes the current preferences to their XML file.</summary>
    public void Save()
    {
        if (ProductForms.Count == 0 || ProductForms.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Add at least one non-empty product form before saving options.");
        }
        if (!double.IsFinite(DefaultJointEfficiency) || DefaultJointEfficiency <= 0 || DefaultJointEfficiency > 1)
        {
            throw new InvalidOperationException("Default joint efficiency must be greater than 0 and no greater than 1.");
        }

        var directory = Path.GetDirectoryName(SettingsFilePath)
            ?? throw new InvalidOperationException("Could not determine the settings folder.");
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsFilePath + ".tmp";
        var document = new XDocument(
            new XElement("MaterialLibraryCrudOptions",
                new XElement("ProductForms", ProductForms
                    .Select(form => form.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(form => new XElement("ProductForm", form))),
                new XElement("DefaultJointEfficiency", DefaultJointEfficiency.ToString("0.####", CultureInfo.InvariantCulture)),
                new XElement("FlexXmlFolder", FlexXmlFolder.Trim()),
                new XElement("DatabaseFilePath", DatabaseFilePath.Trim())));
        document.Save(temporaryPath);
        File.Move(temporaryPath, SettingsFilePath, overwrite: true);
    }

    private static string GetDefaultSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MaterialLibrary", "crud-options.xml");
}
