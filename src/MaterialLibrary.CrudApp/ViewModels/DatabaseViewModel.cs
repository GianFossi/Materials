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

/// <summary>Drives the database manager and raw SQLite workspace.</summary>
public sealed partial class DatabaseViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly IReadOnlyList<Material> _libraryMaterials;
    private readonly List<Material> _imported = [];

    private string? _workingPath;
    private string? _sourcePath;
    private string _statusMessage = "Open a material database to begin.";
    private DatabaseRowViewModel? _selectedMaterial;
    private DatabaseTableViewModel? _selectedTable;
    private DataTable? _currentTable;
    private DataView? _tableRows;
    private int _tablePageSize = 200;
    private int _tableOffset;
    private int _tableRowCount;
    private string _createTableSql = "CREATE TABLE NewTable (\r\n    Id INTEGER PRIMARY KEY,\r\n    Name TEXT\r\n);";
    private string _sqlCommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') ORDER BY type, name;";
    private DataView? _sqlResults;
    private string _plotXColumn = string.Empty;
    private string _plotYColumn = string.Empty;
    private string _plotYColumns = string.Empty;
    private readonly ObservableCollection<PlotSeriesViewModel> _plotSeries = [];
    private PointCollection _plotPoints = [];
    private string _plotMessage = "Select numeric X and Y columns from the loaded table page.";
    private string _plotTitle = "Table plot";
    private string _plotXAxisLabel = "X";
    private string _plotYAxisLabel = "Y";
    private double _plotZoom = 1.0;
    private bool _isBusy;
    private string _tableSearch = string.Empty;
    private string _rowFilter = string.Empty;
    private string _sortColumn = string.Empty;
    private bool _sortDescending;
    private CancellationTokenSource? _operationCancellation;
    private string _savedQueryName = string.Empty;
    private string _renameTableName = string.Empty;
    private string _addColumnSql = string.Empty;
    private string _createIndexSql = string.Empty;
    private readonly Dictionary<string, string> _savedQueryTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<string> _auditLog = [];
    private readonly string _auditStorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialLibrary", "crud-audit.json");
    private DateTime _workingLastWriteUtc;
    private long _workingLength;
    private readonly string _queryStorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialLibrary", "crud-queries.json");
    private bool _hasUnsavedTableChanges;
    private DataTable? _tableUndoSnapshot;
    private DataTable? _tableRedoSnapshot;
    private readonly List<string> _currentPrimaryKeyColumns = [];
    private readonly string _sessionStorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialLibrary", "crud-session.json");
    private string? _lastSourcePath;
    private readonly TransactionJournal _transactionJournal = new();
    private IReadOnlyList<TransactionJournalEntry> _transactionHistory = [];

    /// <summary>Creates the database manager.</summary>
    /// <param name="dialogService">Provider of file pickers and message boxes.</param>
    /// <param name="libraryMaterials">Materials of the in-memory library, available for export.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is <c>null</c>.</exception>
    public DatabaseViewModel(IDialogService dialogService, IReadOnlyList<Material> libraryMaterials)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _libraryMaterials = libraryMaterials ?? throw new ArgumentNullException(nameof(libraryMaterials));
        AuditLog = new ReadOnlyObservableCollection<string>(_auditLog);
        LoadAuditStore();

        OpenDatabaseCommand = new AsyncRelayCommand(OpenDatabaseAsync);
        ReopenLastDatabaseCommand = new AsyncRelayCommand(ReopenLastDatabaseAsync, () => !string.IsNullOrWhiteSpace(_lastSourcePath) && !IsBusy);
        ImportSelectedCommand = new RelayCommand(ImportSelected, () => SelectedMaterial is not null);
        ExportLibraryCommand = new RelayCommand(ExportLibrary, () => IsOpen && _libraryMaterials.Count > 0);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => SelectedMaterial is not null);
        SaveWorkingCopyAsCommand = new RelayCommand(SaveWorkingCopyAs, () => IsOpen);
        BackupWorkingCopyCommand = new AsyncRelayCommand(BackupWorkingCopyAsync, () => IsOpen && !IsBusy);
        CancelOperationCommand = new RelayCommand(CancelOperation, () => IsBusy);
        IntegrityCheckCommand = new AsyncRelayCommand(IntegrityCheckAsync, () => IsOpen && !IsBusy);
        ClearTableToolsCommand = new RelayCommand(ClearTableTools);
        SaveQueryCommand = new RelayCommand(SaveQuery, () => !string.IsNullOrWhiteSpace(SqlCommandText));
        LoadQueryCommand = new RelayCommand(LoadQuery, () => SavedQueries.Contains(SavedQueryName));
        ExportResultsCsvCommand = new RelayCommand(ExportResultsCsv, () => SqlResults is not null || TableRows is not null);
        ExportResultsJsonCommand = new RelayCommand(ExportResultsJson, () => SqlResults is not null || TableRows is not null);
        ExportResultsExcelCommand = new RelayCommand(ExportResultsExcel, () => SqlResults is not null || TableRows is not null);
        RenameTableCommand = new AsyncRelayCommand(RenameTableAsync, () => IsOpen && SelectedTable is not null && !string.IsNullOrWhiteSpace(RenameTableName) && !IsBusy);
        AddColumnCommand = new AsyncRelayCommand(AddColumnAsync, () => IsOpen && SelectedTable is not null && !string.IsNullOrWhiteSpace(AddColumnSql) && !IsBusy);
        CreateIndexCommand = new AsyncRelayCommand(CreateIndexAsync, () => IsOpen && !string.IsNullOrWhiteSpace(CreateIndexSql) && !IsBusy);
        UndoTableCommand = new RelayCommand(UndoTable, () => _tableUndoSnapshot is not null && HasUnsavedTableChanges);
        RedoTableCommand = new RelayCommand(RedoTable, () => _tableRedoSnapshot is not null);
        DiscardTableChangesCommand = new RelayCommand(DiscardTableChanges, () => HasUnsavedTableChanges);
        UndoLastTransactionCommand = new AsyncRelayCommand(UndoLastTransactionAsync, () => IsOpen && !IsBusy && TransactionHistory.Any(entry => string.Equals(entry.DatabasePath, _workingPath, StringComparison.OrdinalIgnoreCase)));
        RedoLastTransactionCommand = new AsyncRelayCommand(RedoLastTransactionAsync, () => IsOpen && !IsBusy && _transactionJournal.LoadRedo().Any(entry => string.Equals(entry.DatabasePath, _workingPath, StringComparison.OrdinalIgnoreCase)));
        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => IsOpen);
        RefreshTableCommand = new AsyncRelayCommand(RefreshSelectedTableAsync, () => IsOpen && SelectedTable is not null && !IsBusy);
        PreviousTablePageCommand = new RelayCommand(PreviousTablePage, () => IsOpen && SelectedTable is not null && TableOffset > 0);
        NextTablePageCommand = new RelayCommand(NextTablePage, () => IsOpen && SelectedTable is not null && TableOffset + TablePageSize < TableRowCount);
        SaveTableChangesCommand = new AsyncRelayCommand(SaveTableChangesAsync, () => CanEditSelectedTable && !IsBusy);
        CreateTableCommand = new AsyncRelayCommand(CreateTableAsync, () => IsOpen && !string.IsNullOrWhiteSpace(CreateTableSql) && !IsBusy);
        ExecuteSqlCommand = new AsyncRelayCommand(ExecuteSqlAsync, () => IsOpen && !string.IsNullOrWhiteSpace(SqlCommandText) && !IsBusy);
        PlotTableCommand = new RelayCommand(PlotTable, () => TableRows is not null && !string.IsNullOrWhiteSpace(PlotXColumn) && !string.IsNullOrWhiteSpace(PlotYColumn));
        LoadQueryStore();
        LoadSessionStore();
        _transactionHistory = _transactionJournal.Load();
    }

    /// <summary>Materials present in the open database.</summary>
    public ObservableCollection<DatabaseRowViewModel> Materials { get; } = [];
    /// <summary>Tables and views available in the open database.</summary>
    public ObservableCollection<DatabaseTableViewModel> Tables { get; } = [];
    /// <summary>Column names of the selected table, used by the filter and plot pickers.</summary>
    public ObservableCollection<string> TableColumns { get; } = [];
    /// <summary>Column definitions of the selected table.</summary>
    public ObservableCollection<DatabaseColumnViewModel> TableSchema { get; } = [];
    /// <summary>Foreign keys declared on the selected table.</summary>
    public ObservableCollection<DatabaseForeignKeyViewModel> TableForeignKeys { get; } = [];
    /// <summary>Statements executed in this session, most recent first.</summary>
    public ObservableCollection<string> SqlHistory { get; } = [];
    /// <summary>Running record of the operations performed in this session.</summary>
    public ReadOnlyObservableCollection<string> AuditLog { get; }
    /// <summary>Committed raw-table transactions that can still be undone.</summary>
    public IReadOnlyList<TransactionJournalEntry> TransactionHistory => _transactionHistory;
    /// <summary>Named queries saved for reuse.</summary>
    public ObservableCollection<string> SavedQueries { get; } = [];
    /// <summary>Materials the user chose to import into the library.</summary>
    public IReadOnlyList<Material> ImportedMaterials => _imported;
    /// <summary>Whether a database is currently open.</summary>
    public bool IsOpen => _workingPath is not null;
    /// <summary>True while a source/reference database is open; source files are never written directly.</summary>
    public bool IsReferenceReadOnly => _sourcePath is not null;
    /// <summary>Banner text describing which file is being edited and which is protected.</summary>
    public string ReferenceModeDisplay => IsReferenceReadOnly
        ? "Reference database: read-only (all edits target the working copy)"
        : "Working database mode";
    /// <summary>Whether a long-running database operation is in progress.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }
    /// <summary>Whether the raw grid holds edits that have not been committed.</summary>
    public bool HasUnsavedTableChanges
    {
        get => _hasUnsavedTableChanges;
        private set => SetProperty(ref _hasUnsavedTableChanges, value);
    }
    /// <summary>Path of the working copy, or a placeholder when nothing is open.</summary>
    public string WorkingPathDisplay => _workingPath ?? "(no database open)";

    /// <summary>Rows of the selected table, bound directly to the editable grid.</summary>
    public DataView? TableRows
    {
        get => _tableRows;
        private set => SetProperty(ref _tableRows, value);
    }

    /// <summary>Filter applied to the table list.</summary>
    public string TableSearch
    {
        get => _tableSearch;
        set { if (SetProperty(ref _tableSearch, value)) RefreshTables(); }
    }

    /// <summary>Text matched against every column of the selected table.</summary>
    /// <remarks>Passed as a bound parameter, never concatenated into the SQL.</remarks>
    public string RowFilter
    {
        get => _rowFilter;
        set { if (SetProperty(ref _rowFilter, value)) ApplyTableView(); }
    }

    /// <summary>Column the rows are ordered by.</summary>
    /// <remarks>
    /// Validated against the table's real column list before it reaches the query, because a column
    /// name cannot be parameterised and must therefore be whitelisted.
    /// </remarks>
    public string SortColumn
    {
        get => _sortColumn;
        set { if (SetProperty(ref _sortColumn, value)) ApplyTableView(); }
    }

    /// <summary>Whether the sort is descending.</summary>
    public bool SortDescending
    {
        get => _sortDescending;
        set { if (SetProperty(ref _sortDescending, value)) ApplyTableView(); }
    }

    /// <summary>Material row selected in the materials grid.</summary>
    public DatabaseRowViewModel? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (SetProperty(ref _selectedMaterial, value))
            {
                ImportSelectedCommand.RaiseCanExecuteChanged();
                DeleteSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Table whose rows and schema are shown in the raw-table tab.</summary>
    public DatabaseTableViewModel? SelectedTable
    {
        get => _selectedTable;
        set
        {
            if (HasUnsavedTableChanges && !ReferenceEquals(_selectedTable, value))
            {
                if (!_dialogService.ConfirmDiscardChanges("changing tables"))
                {
                    RaisePropertyChanged(nameof(SelectedTable));
                    return;
                }
                DiscardTableChanges();
            }
            if (SetProperty(ref _selectedTable, value))
            {
                _tableOffset = 0;
                RaisePropertyChanged(nameof(CanEditSelectedTable));
                RunDetached(RefreshSelectedTableAsync, "Loading table");
                RefreshTableCommands();
            }
        }
    }

    /// <summary>Rows per page; clamped to a sane range when set.</summary>
    public int TablePageSize
    {
        get => _tablePageSize;
        set
        {
            var normalized = Math.Clamp(value, 25, 5000);
            if (SetProperty(ref _tablePageSize, normalized))
            {
                _tableOffset = 0;
                RunDetached(RefreshSelectedTableAsync, "Changing page size");
            }
        }
    }

    /// <summary>Zero-based index of the first row on the current page.</summary>
    public int TableOffset
    {
        get => _tableOffset;
        private set
        {
            if (SetProperty(ref _tableOffset, value))
            {
                RaisePropertyChanged(nameof(TablePageDisplay));
                RefreshTableCommands();
            }
        }
    }

    /// <summary>Total rows in the selected table, used to drive paging.</summary>
    public int TableRowCount
    {
        get => _tableRowCount;
        private set
        {
            if (SetProperty(ref _tableRowCount, value))
            {
                RaisePropertyChanged(nameof(TablePageDisplay));
                RefreshTableCommands();
            }
        }
    }

    /// <summary>Human-readable description of the page currently shown.</summary>
    public string TablePageDisplay
    {
        get
        {
            if (SelectedTable is null) return "No table selected.";
            if (TableRowCount == 0) return "0 rows.";
            var first = TableOffset + 1;
            var last = Math.Min(TableOffset + TablePageSize, TableRowCount);
            return $"Rows {first:N0}-{last:N0} of {TableRowCount:N0}.";
        }
    }

    /// <summary>Whether the selected object accepts edits.</summary>
    /// <remarks>Views are read-only, so only real tables can be modified.</remarks>
    public bool CanEditSelectedTable => IsOpen
        && SelectedTable?.Kind == "table"
        && _currentTable is not null
        && (_currentTable.Columns.Contains("__rowid") || _currentPrimaryKeyColumns.Count > 0);

    /// <summary>CREATE TABLE statement typed in the schema tab.</summary>
    public string CreateTableSql
    {
        get => _createTableSql;
        set
        {
            if (SetProperty(ref _createTableSql, value)) CreateTableCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Statement in the SQL editor.</summary>
    public string SqlCommandText
    {
        get => _sqlCommandText;
        set
        {
            if (SetProperty(ref _sqlCommandText, value)) ExecuteSqlCommand.RaiseCanExecuteChanged();
        }
    }
    /// <summary>Name used when saving or loading a query.</summary>
    public string SavedQueryName
    {
        get => _savedQueryName;
        set { if (SetProperty(ref _savedQueryName, value)) LoadQueryCommand.RaiseCanExecuteChanged(); }
    }
    /// <summary>New name used by the rename-table command.</summary>
    public string RenameTableName { get => _renameTableName; set { if (SetProperty(ref _renameTableName, value)) RenameTableCommand.RaiseCanExecuteChanged(); } }
    /// <summary>Column definition appended by the add-column command.</summary>
    public string AddColumnSql { get => _addColumnSql; set { if (SetProperty(ref _addColumnSql, value)) AddColumnCommand.RaiseCanExecuteChanged(); } }
    /// <summary>CREATE INDEX statement typed in the schema tab.</summary>
    public string CreateIndexSql { get => _createIndexSql; set { if (SetProperty(ref _createIndexSql, value)) CreateIndexCommand.RaiseCanExecuteChanged(); } }

    /// <summary>Result set of the last statement that returned rows.</summary>
    public DataView? SqlResults
    {
        get => _sqlResults;
        private set => SetProperty(ref _sqlResults, value);
    }

    /// <summary>Column plotted on the X axis.</summary>
    public string PlotXColumn
    {
        get => _plotXColumn;
        set
        {
            if (SetProperty(ref _plotXColumn, value)) PlotTableCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Column plotted on the Y axis.</summary>
    public string PlotYColumn
    {
        get => _plotYColumn;
        set
        {
            if (SetProperty(ref _plotYColumn, value)) PlotTableCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Points of the primary plotted series, in canvas coordinates.</summary>
    public PointCollection PlotPoints
    {
        get => _plotPoints;
        private set => SetProperty(ref _plotPoints, value);
    }
    /// <summary>All plotted series, one per selected Y column.</summary>
    public ObservableCollection<PlotSeriesViewModel> PlotSeries => _plotSeries;
    /// <summary>Comma-separated list of columns plotted on the Y axis.</summary>
    public string PlotYColumns
    {
        get => _plotYColumns;
        set { if (SetProperty(ref _plotYColumns, value)) PlotTableCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Explanation shown when the plot cannot be drawn.</summary>
    public string PlotMessage
    {
        get => _plotMessage;
        private set => SetProperty(ref _plotMessage, value);
    }
    /// <summary>Title shown above the plot.</summary>
    public string PlotTitle { get => _plotTitle; set => SetProperty(ref _plotTitle, value); }
    /// <summary>Caption of the X axis.</summary>
    public string PlotXAxisLabel { get => _plotXAxisLabel; set => SetProperty(ref _plotXAxisLabel, value); }
    /// <summary>Caption of the Y axis.</summary>
    public string PlotYAxisLabel { get => _plotYAxisLabel; set => SetProperty(ref _plotYAxisLabel, value); }
    /// <summary>Lowest X value in the plotted range.</summary>
    public string PlotXAxisMinimum { get; private set; } = "";
    /// <summary>Highest X value in the plotted range.</summary>
    public string PlotXAxisMaximum { get; private set; } = "";
    /// <summary>Lowest Y value in the plotted range.</summary>
    public string PlotYAxisMinimum { get; private set; } = "";
    /// <summary>Highest Y value in the plotted range.</summary>
    public string PlotYAxisMaximum { get; private set; } = "";
    /// <summary>X-axis tick label at one quarter of the range.</summary>
    public string PlotXAxisQuarter1 { get; private set; } = "";
    /// <summary>X-axis tick label at the midpoint of the range.</summary>
    public string PlotXAxisQuarter2 { get; private set; } = "";
    /// <summary>X-axis tick label at three quarters of the range.</summary>
    public string PlotXAxisQuarter3 { get; private set; } = "";
    /// <summary>Y-axis tick label at one quarter of the range.</summary>
    public string PlotYAxisQuarter1 { get; private set; } = "";
    /// <summary>Y-axis tick label at the midpoint of the range.</summary>
    public string PlotYAxisQuarter2 { get; private set; } = "";
    /// <summary>Y-axis tick label at three quarters of the range.</summary>
    public string PlotYAxisQuarter3 { get; private set; } = "";
    /// <summary>Zoom factor applied to the plot surface.</summary>
    public double PlotZoom { get => _plotZoom; set => SetProperty(ref _plotZoom, Math.Clamp(value, 0.5, 3.0)); }

    /// <summary>Message shown in the status bar.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Picks a database, copies it to a working file, and provisions any missing tables.</summary>
    public AsyncRelayCommand OpenDatabaseCommand { get; }
    /// <summary>Reopens the database used in the previous session.</summary>
    public AsyncRelayCommand ReopenLastDatabaseCommand { get; }
    /// <summary>Reads the selected database material and queues it for the library.</summary>
    public RelayCommand ImportSelectedCommand { get; }
    /// <summary>Writes every material of the in-memory library into the working copy.</summary>
    public RelayCommand ExportLibraryCommand { get; }
    /// <summary>Deletes the selected material and all of its linked rows.</summary>
    public RelayCommand DeleteSelectedCommand { get; }
    /// <summary>Copies the working file to a permanent location chosen by the user.</summary>
    public RelayCommand SaveWorkingCopyAsCommand { get; }
    /// <summary>Writes a timestamped backup of the working copy.</summary>
    public AsyncRelayCommand BackupWorkingCopyCommand { get; }
    /// <summary>Requests cancellation of the running database operation.</summary>
    public RelayCommand CancelOperationCommand { get; }
    /// <summary>Runs the SQLite integrity and foreign-key checks against the working copy.</summary>
    public AsyncRelayCommand IntegrityCheckCommand { get; }
    /// <summary>Clears the filter, sort, and search applied to the table.</summary>
    public RelayCommand ClearTableToolsCommand { get; }
    /// <summary>Reloads the material list and table list from the working copy.</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>Reloads the selected table from the working copy.</summary>
    public AsyncRelayCommand RefreshTableCommand { get; }
    /// <summary>Moves to the previous page of rows.</summary>
    public RelayCommand PreviousTablePageCommand { get; }
    /// <summary>Moves to the next page of rows.</summary>
    public RelayCommand NextTablePageCommand { get; }
    /// <summary>Commits the pending raw-grid edits inside one transaction.</summary>
    public AsyncRelayCommand SaveTableChangesCommand { get; }
    /// <summary>Executes the CREATE TABLE statement from the schema tab.</summary>
    public AsyncRelayCommand CreateTableCommand { get; }
    /// <summary>Runs the statement in the SQL editor against the working copy.</summary>
    public AsyncRelayCommand ExecuteSqlCommand { get; }
    /// <summary>Saves the current statement under the chosen name.</summary>
    public RelayCommand SaveQueryCommand { get; }
    /// <summary>Loads a previously saved statement into the editor.</summary>
    public RelayCommand LoadQueryCommand { get; }
    /// <summary>Exports the visible results to a CSV file.</summary>
    public RelayCommand ExportResultsCsvCommand { get; }
    /// <summary>Exports the visible results to a JSON file.</summary>
    public RelayCommand ExportResultsJsonCommand { get; }
    /// <summary>Exports the visible results to an Excel workbook.</summary>
    public RelayCommand ExportResultsExcelCommand { get; }
    /// <summary>Renames the selected table.</summary>
    public AsyncRelayCommand RenameTableCommand { get; }
    /// <summary>Adds a column to the selected table.</summary>
    public AsyncRelayCommand AddColumnCommand { get; }
    /// <summary>Executes the CREATE INDEX statement from the schema tab.</summary>
    public AsyncRelayCommand CreateIndexCommand { get; }
    /// <summary>Restores the raw grid to its state before the last edit.</summary>
    public RelayCommand UndoTableCommand { get; }
    /// <summary>Reapplies the edit undone by the undo command.</summary>
    public RelayCommand RedoTableCommand { get; }
    /// <summary>Abandons the pending raw-grid edits.</summary>
    public RelayCommand DiscardTableChangesCommand { get; }
    /// <summary>Reverts the most recent committed raw-table transaction.</summary>
    public AsyncRelayCommand UndoLastTransactionCommand { get; }
    /// <summary>Reapplies the most recently undone transaction.</summary>
    public AsyncRelayCommand RedoLastTransactionCommand { get; }
    /// <summary>Rebuilds the plot from the selected X and Y columns of the current table.</summary>
    public RelayCommand PlotTableCommand { get; }

    private sealed record SessionStore(string? SourcePath);

    private sealed record QueryStore(List<string> History, List<SavedQuery> Saved);
    private sealed record SavedQuery(string Name, string Sql);

    private static string BuildRowFilter(string text, DataColumnCollection columns)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var escaped = text.Replace("'", "''");
        var terms = columns.Cast<DataColumn>()
            .Where(c => c.ColumnName != "__rowid" && c.DataType == typeof(string))
            .Select(c => $"CONVERT([{c.ColumnName.Replace("]", "]]", StringComparison.Ordinal)}], 'System.String') LIKE '%{escaped}%'")
            .ToList();
        return terms.Count == 0 ? string.Empty : string.Join(" OR ", terms);
    }

    private static string QuoteViewIdentifier(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private void RefreshCommandStates()
    {
        ExportLibraryCommand.RaiseCanExecuteChanged();
        SaveWorkingCopyAsCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        CreateTableCommand.RaiseCanExecuteChanged();
        ExecuteSqlCommand.RaiseCanExecuteChanged();
        RefreshTableCommands();
    }

    private static object NormalizeDbValue(object? value)
    {
        if (value is null || value == DBNull.Value) return DBNull.Value;
        return value is string text && string.IsNullOrEmpty(text) ? DBNull.Value : value;
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        if (value is null || value == DBNull.Value)
        {
            result = 0;
            return false;
        }

        return value switch
        {
            double d => SetFinite(d, out result),
            float f => SetFinite(f, out result),
            decimal m => SetFinite((double)m, out result),
            int i => SetFinite(i, out result),
            long l => SetFinite(l, out result),
            short s => SetFinite(s, out result),
            byte b => SetFinite(b, out result),
            _ => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result)
        };
    }

    private static bool SetFinite(double value, out double result)
    {
        result = value;
        return double.IsFinite(value);
    }

    private void ShowError(MaterialError error)
    {
        var message = MaterialErrorFormat.Format(error);
        _dialogService.ShowError(message);
        StatusMessage = message;
    }

    private void ShowRawError(Exception error)
    {
        var message = $"Database workspace error: {error.Message}";
        _dialogService.ShowError(message);
        StatusMessage = message;
    }
}
