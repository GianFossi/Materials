using System.Collections.ObjectModel;
using System.Globalization;
using MaterialLibraryCrudApp.Services;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Edits persistent application preferences.</summary>
public sealed class OptionsViewModel : ObservableObject
{
    private readonly ApplicationOptions _options;
    private string _newProductForm = string.Empty;
    private string _defaultJointEfficiency;
    private string _flexXmlFolder;
    private string _databaseFilePath;
    private string _status = "Changes are applied only after you select Save.";
    private string? _selectedProductForm;

    /// <summary>Creates the options editing buffer.</summary>
    public OptionsViewModel(ApplicationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ProductForms = new ObservableCollection<string>(options.ProductForms);
        _defaultJointEfficiency = options.DefaultJointEfficiency.ToString("0.##", CultureInfo.InvariantCulture);
        _flexXmlFolder = options.FlexXmlFolder;
        _databaseFilePath = options.DatabaseFilePath;
        AddProductFormCommand = new RelayCommand(AddProductForm);
        RemoveProductFormCommand = new RelayCommand(RemoveProductForm, () => SelectedProductForm is not null);
    }

    /// <summary>Product-form labels offered by the material and ASME editors.</summary>
    public ObservableCollection<string> ProductForms { get; }

    /// <summary>New product-form label to add to the list.</summary>
    public string NewProductForm
    {
        get => _newProductForm;
        set => SetProperty(ref _newProductForm, value);
    }

    /// <summary>Selected product form to remove.</summary>
    public string? SelectedProductForm
    {
        get => _selectedProductForm;
        set
        {
            if (SetProperty(ref _selectedProductForm, value))
            {
                RemoveProductFormCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Default joint efficiency as a fraction from greater than 0 through 1.</summary>
    public string DefaultJointEfficiency
    {
        get => _defaultJointEfficiency;
        set => SetProperty(ref _defaultJointEfficiency, value);
    }

    /// <summary>Default folder containing flex/property XML files.</summary>
    public string FlexXmlFolder
    {
        get => _flexXmlFolder;
        set => SetProperty(ref _flexXmlFolder, value);
    }

    /// <summary>Default SQLite database file path.</summary>
    public string DatabaseFilePath
    {
        get => _databaseFilePath;
        set => SetProperty(ref _databaseFilePath, value);
    }

    /// <summary>Options-file location and save feedback.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Adds the typed product form to the editable list.</summary>
    public RelayCommand AddProductFormCommand { get; }

    /// <summary>Removes the selected product form.</summary>
    public RelayCommand RemoveProductFormCommand { get; }

    /// <summary>Validates and persists the current options.</summary>
    public bool TrySave()
    {
        if (!double.TryParse(DefaultJointEfficiency, NumberStyles.Float, CultureInfo.InvariantCulture, out var efficiency)
            || !double.IsFinite(efficiency) || efficiency <= 0 || efficiency > 1)
        {
            Status = "Enter a joint efficiency greater than 0 and no greater than 1 (for example, 0.70).";
            return false;
        }

        var normalizedForms = ProductForms.Select(form => form.Trim())
            .Where(form => form.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedForms.Count == 0)
        {
            Status = "Keep at least one product form in the list.";
            return false;
        }

        try
        {
            _options.ProductForms = normalizedForms;
            _options.DefaultJointEfficiency = efficiency;
            _options.FlexXmlFolder = FlexXmlFolder.Trim();
            _options.DatabaseFilePath = DatabaseFilePath.Trim();
            _options.Save();
            ProductForms.Clear();
            foreach (var form in normalizedForms)
            {
                ProductForms.Add(form);
            }
            Status = $"Options saved to {_options.SettingsFilePath}";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Could not save options: {ex.Message}";
            return false;
        }
    }

    private void AddProductForm()
    {
        var form = NewProductForm.Trim();
        if (form.Length == 0 || ProductForms.Contains(form, StringComparer.OrdinalIgnoreCase))
        {
            Status = form.Length == 0 ? "Enter a product-form name." : "That product form is already in the list.";
            return;
        }

        ProductForms.Add(form);
        NewProductForm = string.Empty;
        Status = $"Added '{form}'. Save to keep this change.";
    }

    private void RemoveProductForm()
    {
        if (SelectedProductForm is null)
        {
            return;
        }
        ProductForms.Remove(SelectedProductForm);
        SelectedProductForm = null;
        Status = "Removed product form. Save to keep this change.";
    }
}
