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
public sealed class DatabaseViewModel : ObservableObject
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

    public ObservableCollection<DatabaseRowViewModel> Materials { get; } = [];
    public ObservableCollection<DatabaseTableViewModel> Tables { get; } = [];
    public ObservableCollection<string> TableColumns { get; } = [];
    public ObservableCollection<DatabaseColumnViewModel> TableSchema { get; } = [];
    public ObservableCollection<DatabaseForeignKeyViewModel> TableForeignKeys { get; } = [];
    public ObservableCollection<string> SqlHistory { get; } = [];
    public ReadOnlyObservableCollection<string> AuditLog { get; }
    public IReadOnlyList<TransactionJournalEntry> TransactionHistory => _transactionHistory;
    public ObservableCollection<string> SavedQueries { get; } = [];
    public IReadOnlyList<Material> ImportedMaterials => _imported;
    public bool IsOpen => _workingPath is not null;
    /// <summary>True while a source/reference database is open; source files are never written directly.</summary>
    public bool IsReferenceReadOnly => _sourcePath is not null;
    public string ReferenceModeDisplay => IsReferenceReadOnly
        ? "Reference database: read-only (all edits target the working copy)"
        : "Working database mode";
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }
    public bool HasUnsavedTableChanges
    {
        get => _hasUnsavedTableChanges;
        private set => SetProperty(ref _hasUnsavedTableChanges, value);
    }
    public string WorkingPathDisplay => _workingPath ?? "(no database open)";

    public DataView? TableRows
    {
        get => _tableRows;
        private set => SetProperty(ref _tableRows, value);
    }

    public string TableSearch
    {
        get => _tableSearch;
        set { if (SetProperty(ref _tableSearch, value)) RefreshTables(); }
    }

    public string RowFilter
    {
        get => _rowFilter;
        set { if (SetProperty(ref _rowFilter, value)) ApplyTableView(); }
    }

    public string SortColumn
    {
        get => _sortColumn;
        set { if (SetProperty(ref _sortColumn, value)) ApplyTableView(); }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set { if (SetProperty(ref _sortDescending, value)) ApplyTableView(); }
    }

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
                _ = RefreshSelectedTableAsync();
                RefreshTableCommands();
            }
        }
    }

    public int TablePageSize
    {
        get => _tablePageSize;
        set
        {
            var normalized = Math.Clamp(value, 25, 5000);
            if (SetProperty(ref _tablePageSize, normalized))
            {
                _tableOffset = 0;
                _ = RefreshSelectedTableAsync();
            }
        }
    }

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

    public bool CanEditSelectedTable => IsOpen
        && SelectedTable?.Kind == "table"
        && _currentTable is not null
        && (_currentTable.Columns.Contains("__rowid") || _currentPrimaryKeyColumns.Count > 0);

    public string CreateTableSql
    {
        get => _createTableSql;
        set
        {
            if (SetProperty(ref _createTableSql, value)) CreateTableCommand.RaiseCanExecuteChanged();
        }
    }

    public string SqlCommandText
    {
        get => _sqlCommandText;
        set
        {
            if (SetProperty(ref _sqlCommandText, value)) ExecuteSqlCommand.RaiseCanExecuteChanged();
        }
    }
    public string SavedQueryName
    {
        get => _savedQueryName;
        set { if (SetProperty(ref _savedQueryName, value)) LoadQueryCommand.RaiseCanExecuteChanged(); }
    }
    public string RenameTableName { get => _renameTableName; set { if (SetProperty(ref _renameTableName, value)) RenameTableCommand.RaiseCanExecuteChanged(); } }
    public string AddColumnSql { get => _addColumnSql; set { if (SetProperty(ref _addColumnSql, value)) AddColumnCommand.RaiseCanExecuteChanged(); } }
    public string CreateIndexSql { get => _createIndexSql; set { if (SetProperty(ref _createIndexSql, value)) CreateIndexCommand.RaiseCanExecuteChanged(); } }

    public DataView? SqlResults
    {
        get => _sqlResults;
        private set => SetProperty(ref _sqlResults, value);
    }

    public string PlotXColumn
    {
        get => _plotXColumn;
        set
        {
            if (SetProperty(ref _plotXColumn, value)) PlotTableCommand.RaiseCanExecuteChanged();
        }
    }

    public string PlotYColumn
    {
        get => _plotYColumn;
        set
        {
            if (SetProperty(ref _plotYColumn, value)) PlotTableCommand.RaiseCanExecuteChanged();
        }
    }

    public PointCollection PlotPoints
    {
        get => _plotPoints;
        private set => SetProperty(ref _plotPoints, value);
    }
    public ObservableCollection<PlotSeriesViewModel> PlotSeries => _plotSeries;
    public string PlotYColumns
    {
        get => _plotYColumns;
        set { if (SetProperty(ref _plotYColumns, value)) PlotTableCommand.RaiseCanExecuteChanged(); }
    }

    public string PlotMessage
    {
        get => _plotMessage;
        private set => SetProperty(ref _plotMessage, value);
    }
    public string PlotTitle { get => _plotTitle; set => SetProperty(ref _plotTitle, value); }
    public string PlotXAxisLabel { get => _plotXAxisLabel; set => SetProperty(ref _plotXAxisLabel, value); }
    public string PlotYAxisLabel { get => _plotYAxisLabel; set => SetProperty(ref _plotYAxisLabel, value); }
    public string PlotXAxisMinimum { get; private set; } = "";
    public string PlotXAxisMaximum { get; private set; } = "";
    public string PlotYAxisMinimum { get; private set; } = "";
    public string PlotYAxisMaximum { get; private set; } = "";
    public string PlotXAxisQuarter1 { get; private set; } = "";
    public string PlotXAxisQuarter2 { get; private set; } = "";
    public string PlotXAxisQuarter3 { get; private set; } = "";
    public string PlotYAxisQuarter1 { get; private set; } = "";
    public string PlotYAxisQuarter2 { get; private set; } = "";
    public string PlotYAxisQuarter3 { get; private set; } = "";
    public double PlotZoom { get => _plotZoom; set => SetProperty(ref _plotZoom, Math.Clamp(value, 0.5, 3.0)); }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AsyncRelayCommand OpenDatabaseCommand { get; }
    public AsyncRelayCommand ReopenLastDatabaseCommand { get; }
    public RelayCommand ImportSelectedCommand { get; }
    public RelayCommand ExportLibraryCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand SaveWorkingCopyAsCommand { get; }
    public AsyncRelayCommand BackupWorkingCopyCommand { get; }
    public RelayCommand CancelOperationCommand { get; }
    public AsyncRelayCommand IntegrityCheckCommand { get; }
    public RelayCommand ClearTableToolsCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RefreshTableCommand { get; }
    public RelayCommand PreviousTablePageCommand { get; }
    public RelayCommand NextTablePageCommand { get; }
    public AsyncRelayCommand SaveTableChangesCommand { get; }
    public AsyncRelayCommand CreateTableCommand { get; }
    public AsyncRelayCommand ExecuteSqlCommand { get; }
    public RelayCommand SaveQueryCommand { get; }
    public RelayCommand LoadQueryCommand { get; }
    public RelayCommand ExportResultsCsvCommand { get; }
    public RelayCommand ExportResultsJsonCommand { get; }
    public RelayCommand ExportResultsExcelCommand { get; }
    public AsyncRelayCommand RenameTableCommand { get; }
    public AsyncRelayCommand AddColumnCommand { get; }
    public AsyncRelayCommand CreateIndexCommand { get; }
    public RelayCommand UndoTableCommand { get; }
    public RelayCommand RedoTableCommand { get; }
    public RelayCommand DiscardTableChangesCommand { get; }
    public AsyncRelayCommand UndoLastTransactionCommand { get; }
    public AsyncRelayCommand RedoLastTransactionCommand { get; }

    public bool CanClose() => !HasUnsavedTableChanges || _dialogService.ConfirmDiscardChanges("closing the database window");
    public RelayCommand PlotTableCommand { get; }

    private async Task OpenDatabaseAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
        var path = _dialogService.AskOpenPath("Open material database", FileFilters.Database);
        if (path is null) return;
        await OpenDatabasePathAsync(path, cancellation.Token);
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private async Task OpenDatabasePathAsync(string path, CancellationToken cancellationToken)
    {
        var working = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(path)) ?? Path.GetTempPath(),
            Path.GetFileNameWithoutExtension(path) + ".working" + Path.GetExtension(path));

        var copyResult = await Task.Run(() => MaterialDatabaseCrud.createWorkingCopy(path, working), cancellationToken);
        if (!copyResult.TryUnwrap(out var workingPath, out var copyError))
        {
            ShowError(copyError);
            return;
        }

        var schemaResult = await Task.Run(() => MaterialDatabaseCrud.ensureSchema(workingPath), cancellationToken);
        if (!schemaResult.TryUnwrap(out var createdTables, out var schemaError))
        {
            ShowError(schemaError);
            return;
        }

        _sourcePath = path;
        _lastSourcePath = path;
        _workingPath = workingPath;
        SaveSessionStore();
        CaptureWorkingFingerprint();
        RaisePropertyChanged(nameof(IsOpen));
        RaisePropertyChanged(nameof(IsReferenceReadOnly));
        RaisePropertyChanged(nameof(ReferenceModeDisplay));
        RaisePropertyChanged(nameof(WorkingPathDisplay));
        RefreshCommandStates();
        await RefreshAllAsync();

        var created = createdTables.ToReadOnlyList();
        StatusMessage = created.Count == 0
            ? $"Opened working copy of {Path.GetFileName(path)}; schema already complete."
            : $"Opened working copy of {Path.GetFileName(path)}; created {created.Count} missing table(s): {string.Join(", ", created)}.";
    }

    private async Task ReopenLastDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastSourcePath) || !File.Exists(_lastSourcePath))
        {
            StatusMessage = "The last database is unavailable.";
            return;
        }
        if (IsBusy) return;
        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try { await OpenDatabasePathAsync(_lastSourcePath, cancellation.Token); }
        catch (Exception ex) { ShowRawError(ex); }
        finally { _operationCancellation = null; IsBusy = false; }
    }

    private async Task BackupWorkingCopyAsync()
    {
        if (_workingPath is null || IsBusy) return;
        if (WorkingCopyChanged()) { ShowRawError(new IOException("The working database changed externally; reload it before writing.")); return; }
        var target = _dialogService.AskSavePath("Back up working database", FileFilters.Database, _workingPath + ".backup");
        if (target is null) return;

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
            var source = _workingPath;
            var result = await Task.Run(() => MaterialDatabaseCrud.createWorkingCopy(source, target), cancellation.Token);
            if (result.TryUnwrap(out var saved, out var error))
                StatusMessage = $"Backed up working database to {saved}.";
            else
                ShowError(error);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Database operation cancelled.";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
        StatusMessage = "Cancelling database operation...";
    }

    private async Task RefreshAllAsync()
    {
        await RefreshMaterialsAsync();
        RefreshTables();
    }

    private async Task RefreshMaterialsAsync()
    {
        Materials.Clear();
        if (_workingPath is null) return;

        try
        {
            var path = _workingPath;
            var result = await Task.Run(() => MaterialDatabaseCrud.listMaterials(path));
            if (!result.TryUnwrap(out var summaries, out var error))
            {
                ShowError(error);
                return;
            }

            foreach (var summary in summaries.ToReadOnlyList())
            {
                Materials.Add(new DatabaseRowViewModel(summary));
            }

            SelectedMaterial = null;
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private void RefreshMaterials() => _ = RefreshMaterialsAsync();

    private void RefreshTables()
    {
        Tables.Clear();
        TableRows = null;
        _currentTable = null;
        TableColumns.Clear();
        TableSchema.Clear();
        TableForeignKeys.Clear();
        _currentPrimaryKeyColumns.Clear();
        TableRowCount = 0;
        if (_workingPath is null)
        {
            SelectedTable = null;
            return;
        }

        try
        {
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name, type
                FROM sqlite_master
                WHERE type IN ('table', 'view')
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY type, name
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var table = new DatabaseTableViewModel(reader.GetString(0), reader.GetString(1));
                if (string.IsNullOrWhiteSpace(TableSearch)
                    || table.Name.Contains(TableSearch, StringComparison.OrdinalIgnoreCase))
                    Tables.Add(table);
            }

            SelectedTable = Tables.FirstOrDefault(t => string.Equals(t.Name, "Materials", StringComparison.OrdinalIgnoreCase))
                ?? Tables.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task RefreshSelectedTableAsync()
    {
        if (_workingPath is null || SelectedTable is null) return;
        var path = _workingPath;
        var tableName = SelectedTable.Name;
        var kind = SelectedTable.Kind;
        var pageSize = TablePageSize;
        var offset = TableOffset;
        try
        {
            var result = await Task.Run(() =>
            {
                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                var sourceColumns = GetColumnNames(connection, tableName);
                var count = CountRows(connection, tableName, sourceColumns, _rowFilter);
                var table = new DataTable(tableName);
                using var command = connection.CreateCommand();
                var where = BuildSqlSearch(sourceColumns, _rowFilter);
                var order = string.IsNullOrWhiteSpace(_sortColumn) || !sourceColumns.Contains(_sortColumn)
                    ? string.Empty
                    : $" ORDER BY {QuoteIdentifier(_sortColumn)}{(_sortDescending ? " DESC" : " ASC")}";
                command.CommandText = kind == "table"
                    ? $"SELECT rowid AS __rowid, * FROM {QuoteIdentifier(tableName)}" + where + order + " LIMIT $limit OFFSET $offset"
                    : $"SELECT * FROM {QuoteIdentifier(tableName)}" + where + order + " LIMIT $limit OFFSET $offset";
                if (!string.IsNullOrWhiteSpace(_rowFilter)) command.Parameters.AddWithValue("$search", "%" + _rowFilter + "%");
                command.Parameters.AddWithValue("$limit", pageSize);
                command.Parameters.AddWithValue("$offset", offset);
                try
                {
                    using var reader = command.ExecuteReader();
                    table.Load(reader);
                }
                catch (SqliteException) when (kind == "table")
                {
                    table.Clear();
                    using var fallback = connection.CreateCommand();
                    fallback.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)}" + where + order + " LIMIT $limit OFFSET $offset";
                    if (!string.IsNullOrWhiteSpace(_rowFilter)) fallback.Parameters.AddWithValue("$search", "%" + _rowFilter + "%");
                    fallback.Parameters.AddWithValue("$limit", pageSize);
                    fallback.Parameters.AddWithValue("$offset", offset);
                    using var reader = fallback.ExecuteReader();
                    table.Load(reader);
                }
                return (table, count);
            });
            if (_workingPath != path || SelectedTable?.Name != tableName) return;
            TableRowCount = result.count;
            result.table.AcceptChanges();
            _currentTable = result.table;
            TableRows = result.table.DefaultView;
            RefreshColumnList(result.table);
            RefreshTableCommands();
            StatusMessage = $"Browsing {tableName}: {TablePageDisplay}";
        }
        catch (Exception ex) { ShowRawError(ex); }
    }

    private void RefreshSelectedTable()
    {
        if (_workingPath is null || SelectedTable is null)
        {
            TableRows = null;
            _currentTable = null;
            TableRowCount = 0;
            RaisePropertyChanged(nameof(CanEditSelectedTable));
            return;
        }

        try
        {
            using var connection = OpenRawConnection();
            var sourceColumns = GetColumnNames(connection, SelectedTable.Name);
            TableRowCount = CountRows(connection, SelectedTable.Name, sourceColumns, _rowFilter);

            var table = new DataTable(SelectedTable.Name);
            using var command = connection.CreateCommand();
            var where = BuildSqlSearch(sourceColumns, _rowFilter);
            var order = string.IsNullOrWhiteSpace(_sortColumn) || !sourceColumns.Contains(_sortColumn)
                ? string.Empty
                : $" ORDER BY {QuoteIdentifier(_sortColumn)}{(_sortDescending ? " DESC" : " ASC")}";
            command.CommandText = (SelectedTable.Kind == "table"
                ? $"SELECT rowid AS __rowid, * FROM {QuoteIdentifier(SelectedTable.Name)}"
                : $"SELECT * FROM {QuoteIdentifier(SelectedTable.Name)}")
                + where + order + " LIMIT $limit OFFSET $offset";
            if (!string.IsNullOrWhiteSpace(_rowFilter)) command.Parameters.AddWithValue("$search", "%" + _rowFilter + "%");
            command.Parameters.AddWithValue("$limit", TablePageSize);
            command.Parameters.AddWithValue("$offset", TableOffset);

            try
            {
                using var reader = command.ExecuteReader();
                table.Load(reader);
            }
            catch (SqliteException) when (SelectedTable.Kind == "table")
            {
                // WITHOUT ROWID tables are browseable, but their rows cannot be updated through
                // the generic editor because there is no stable rowid address.
                table.Clear();
                using var fallback = connection.CreateCommand();
                fallback.CommandText = $"SELECT * FROM {QuoteIdentifier(SelectedTable.Name)}" + where + order + " LIMIT $limit OFFSET $offset";
                if (!string.IsNullOrWhiteSpace(_rowFilter)) fallback.Parameters.AddWithValue("$search", "%" + _rowFilter + "%");
                fallback.Parameters.AddWithValue("$limit", TablePageSize);
                fallback.Parameters.AddWithValue("$offset", TableOffset);
                using var reader = fallback.ExecuteReader();
                table.Load(reader);
            }
            table.AcceptChanges();
            _tableUndoSnapshot = table.Copy();
            _tableRedoSnapshot = null;
            table.RowChanged += (_, args) =>
            {
                if (args.Action is DataRowAction.Add or DataRowAction.Change or DataRowAction.Delete)
                    HasUnsavedTableChanges = true;
            };
            table.RowDeleted += (_, _) => HasUnsavedTableChanges = true;
            _currentTable = table;
            HasUnsavedTableChanges = false;
            RefreshEditCommands();
            RaisePropertyChanged(nameof(CanEditSelectedTable));
            RefreshColumnList(table);
            TableRows = table.DefaultView;
            LoadSchema(connection, SelectedTable.Name);
            RaisePropertyChanged(nameof(CanEditSelectedTable));
            ApplyTableView();
            StatusMessage = $"Browsing {SelectedTable.Name}: {TablePageDisplay}";
            RefreshTableCommands();
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private void PreviousTablePage()
    {
        TableOffset = Math.Max(0, TableOffset - TablePageSize);
        _ = RefreshSelectedTableAsync();
    }

    private void NextTablePage()
    {
        TableOffset = Math.Min(Math.Max(0, TableRowCount - 1), TableOffset + TablePageSize);
        _ = RefreshSelectedTableAsync();
    }

    private void SaveTableChanges()
    {
        if (_workingPath is null || SelectedTable is null || _currentTable is null || !CanEditSelectedTable) return;

        try
        {
            using var connection = OpenRawConnection();
            using var transaction = connection.BeginTransaction();
            var changed = 0;

            foreach (DataRow row in _currentTable.Rows)
            {
                changed += row.RowState switch
                {
                    DataRowState.Added => InsertRawRow(connection, transaction, SelectedTable.Name, row),
                    DataRowState.Modified => UpdateRawRow(connection, transaction, SelectedTable.Name, row),
                    DataRowState.Deleted => DeleteRawRow(connection, transaction, SelectedTable.Name, row),
                    _ => 0
                };
            }

            transaction.Commit();
            HasUnsavedTableChanges = false;
            _tableUndoSnapshot = _currentTable.Copy();
            _tableRedoSnapshot = null;
            RefreshEditCommands();
            _ = RefreshSelectedTableAsync();
            RefreshMaterials();
            StatusMessage = $"Saved {changed} raw table change(s) to the working copy.";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task SaveTableChangesAsync()
    {
        if (_workingPath is null || SelectedTable is null || _currentTable is null || !CanEditSelectedTable) return;
        var changes = SnapshotChanges(_currentTable);
        if (changes.Count == 0) return;
        if (_currentPrimaryKeyColumns.Count > 0 && HasDuplicatePrimaryKeys(_currentTable, _currentPrimaryKeyColumns))
        {
            StatusMessage = "Cannot save: duplicate primary-key values exist in the edited page.";
            return;
        }
        var path = _workingPath;
        var tableName = SelectedTable.Name;
        var primaryKeys = _currentPrimaryKeyColumns.ToList();
        IsBusy = true;
        try
        {
            var commit = await Task.Run(() => ApplyRawChanges(path, tableName, primaryKeys, changes));
            var changed = commit.Changed;
            _transactionJournal.Append(new TransactionJournalEntry(
                DateTimeOffset.UtcNow,
                path,
                tableName,
                commit.Changes.Select(change => new TransactionRowChange(
                    change.State.ToString(),
                    ToJsonValues(change.Original),
                    ToJsonValues(change.Current),
                    change.RowId)).ToList()));
            _transactionJournal.ClearRedo();
            _transactionHistory = _transactionJournal.Load();
            RaisePropertyChanged(nameof(TransactionHistory));
            CaptureWorkingFingerprint();
            HasUnsavedTableChanges = false;
            await RefreshSelectedTableAsync();
            await RefreshMaterialsAsync();
            StatusMessage = $"Saved {changed} raw table change(s) to the working copy.";
            RecordAudit("Raw table save", $"{tableName} ({changed} change(s))");
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; }
    }

    private static List<RawRowChange> SnapshotChanges(DataTable table)
    {
        var changes = new List<RawRowChange>();
        foreach (DataRow row in table.Rows)
        {
            if (row.RowState is not (DataRowState.Added or DataRowState.Modified or DataRowState.Deleted)) continue;
            var columns = table.Columns.Cast<DataColumn>().Where(c => c.ColumnName != "__rowid").ToList();
            var current = row.RowState == DataRowState.Deleted
                ? new Dictionary<string, object?>()
                : columns.ToDictionary(c => c.ColumnName, c => (object?)NormalizeDbValue(row[c, DataRowVersion.Current]));
            var original = columns.ToDictionary(c => c.ColumnName, c => (object?)NormalizeDbValue(row[c, DataRowVersion.Original]));
            var rowId = row.Table.Columns.Contains("__rowid") && row.RowState != DataRowState.Added ? Convert.ToInt64(row["__rowid", DataRowVersion.Original], CultureInfo.InvariantCulture) : (long?)null;
            changes.Add(new RawRowChange(row.RowState, current, original, rowId));
        }
        return changes;
    }

    private static IReadOnlyDictionary<string, JsonElement> ToJsonValues(IReadOnlyDictionary<string, object?> values)
    {
        return values.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value, pair.Value?.GetType() ?? typeof(object)));
    }

    private static bool HasDuplicatePrimaryKeys(DataTable table, IReadOnlyList<string> keys)
    {
        return table.Rows.Cast<DataRow>()
            .Where(row => row.RowState != DataRowState.Deleted)
            .Select(row => string.Join("\u001F", keys.Select(key => Convert.ToString(row[key], CultureInfo.InvariantCulture) ?? string.Empty)))
            .GroupBy(value => value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);
    }

    private static RawCommitResult ApplyRawChanges(string path, string tableName, IReadOnlyList<string> primaryKeys, IReadOnlyList<RawRowChange> changes)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var changed = 0;
        var committed = new List<RawRowChange>();
        foreach (var change in changes)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (change.State == DataRowState.Added)
            {
                var columns = change.Current.Keys.ToList();
                command.CommandText = $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", columns.Select(QuoteIdentifier))}) VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i))})";
                for (var i = 0; i < columns.Count; i++) command.Parameters.AddWithValue("$p" + i, change.Current[columns[i]] ?? DBNull.Value);
            }
            else
            {
                var usePk = primaryKeys.Count > 0 && change.RowId is null;
                var keys = usePk ? primaryKeys : new List<string>();
                command.CommandText = change.State == DataRowState.Deleted
                    ? $"DELETE FROM {QuoteIdentifier(tableName)} WHERE " + (usePk ? string.Join(" AND ", keys.Select((k, i) => $"{QuoteIdentifier(k)} = $k{i}")) : "rowid = $rowid")
                    : $"UPDATE {QuoteIdentifier(tableName)} SET " + string.Join(", ", change.Current.Keys.Select((k, i) => $"{QuoteIdentifier(k)} = $p{i}")) + " WHERE " + (usePk ? string.Join(" AND ", keys.Select((k, i) => $"{QuoteIdentifier(k)} = $k{i}")) : "rowid = $rowid");
                if (change.State == DataRowState.Modified) foreach (var pair in change.Current.Select((p, i) => (p, i))) command.Parameters.AddWithValue("$p" + pair.i, pair.p.Value ?? DBNull.Value);
                if (usePk) foreach (var pair in keys.Select((k, i) => (k, i))) command.Parameters.AddWithValue("$k" + pair.i, change.Original[pair.k] ?? DBNull.Value);
                else command.Parameters.AddWithValue("$rowid", change.RowId!.Value);
            }
            changed += command.ExecuteNonQuery();
            if (change.State == DataRowState.Added && change.RowId is null)
            {
                using var identity = connection.CreateCommand();
                identity.Transaction = transaction;
                identity.CommandText = "SELECT last_insert_rowid()";
                var rowId = Convert.ToInt64(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
                committed.Add(change with { RowId = rowId });
            }
            else
            {
                committed.Add(change);
            }
        }
        using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.Transaction = transaction;
            foreignKeys.CommandText = "PRAGMA foreign_key_check";
            using var violations = foreignKeys.ExecuteReader();
            if (violations.Read())
                throw new InvalidOperationException("Cannot commit raw-table changes: the transaction would violate a foreign-key constraint.");
        }
        transaction.Commit();
        return new RawCommitResult(changed, committed);
    }

    private void CreateTable()
    {
        if (_workingPath is null) return;

        try
        {
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = CreateTableSql;
            var changes = command.ExecuteNonQuery();
            RefreshTables();
            StatusMessage = $"Create table command completed; SQLite reported {changes} changed row(s).";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task CreateTableAsync()
    {
        if (_workingPath is null || IsBusy) return;
        if (WorkingCopyChanged()) { ShowRawError(new IOException("The working database changed externally; reload it before writing.")); return; }
        IsBusy = true;
        try
        {
            var path = _workingPath;
            var sql = CreateTableSql;
            var changes = await Task.Run(() =>
            {
                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                return command.ExecuteNonQuery();
            });
            RefreshTables();
            StatusMessage = $"Create table command completed; SQLite reported {changes} changed row(s).";
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; }
    }

    private void ExecuteSql()
    {
        if (_workingPath is null) return;

        try
        {
            if (IsDestructiveSql(SqlCommandText))
            {
                if (!_dialogService.ConfirmDestructiveSql(SqlCommandText)) return;
                if (!CreateAutomaticBackup(out var backupError))
                {
                    ShowRawError(new IOException(backupError));
                    return;
                }
            }
            SqlHistory.Insert(0, SqlCommandText.Trim());
            while (SqlHistory.Count > 50) SqlHistory.RemoveAt(SqlHistory.Count - 1);
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = SqlCommandText;

            if (ReturnsRows(SqlCommandText))
            {
                var table = new DataTable("SqlResults");
                using var reader = command.ExecuteReader();
                table.Load(reader);
                SqlResults = table.DefaultView;
                StatusMessage = $"SQL returned {table.Rows.Count:N0} row(s).";
            }
            else
            {
                var changes = command.ExecuteNonQuery();
                SqlResults = null;
                RefreshTables();
                RefreshMaterials();
                StatusMessage = $"SQL completed; SQLite reported {changes} changed row(s).";
            }
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task ExecuteSqlAsync()
    {
        if (_workingPath is null || IsBusy) return;
        var sql = SqlCommandText.Trim();
        if (WorkingCopyChanged())
        {
            ShowRawError(new IOException("The working database changed externally; reload it before executing SQL."));
            return;
        }
        if (IsDestructiveSql(sql))
        {
            if (!_dialogService.ConfirmDestructiveSql(sql)) return;
            if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        }

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
            var path = _workingPath;
            var result = await Task.Run(() => ExecuteSqlCore(path, sql, cancellation.Token), cancellation.Token);
            if (result.Rows is not null)
            {
                SqlResults = result.Rows.DefaultView;
                StatusMessage = $"SQL returned {result.Rows.Rows.Count:N0} row(s).";
            }
            else
            {
                SqlResults = null;
                await RefreshAllAsync();
                CaptureWorkingFingerprint();
            StatusMessage = $"SQL completed; SQLite reported {result.Changes} changed row(s).";
            RecordAudit("SQL", sql);
            }
            SqlHistory.Insert(0, sql);
            while (SqlHistory.Count > 50) SqlHistory.RemoveAt(SqlHistory.Count - 1);
            SaveQueryStore();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "SQL operation cancelled.";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private static SqlExecutionResult ExecuteSqlCore(string path, string sql, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var cancellationRegistration = cancellationToken.Register(() => command.Cancel());
        if (ReturnsRows(sql))
        {
            var table = new DataTable("SqlResults");
            using var reader = command.ExecuteReader();
            table.Load(reader);
            return new SqlExecutionResult(table, 0);
        }
        return new SqlExecutionResult(null, command.ExecuteNonQuery());
    }

    private void RenameTable()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        ExecuteSchemaCommand($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}");
    }

    private async Task RenameTableAsync()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        await RunSchemaMutationAsync($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}");
    }

    private void AddColumn()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        ExecuteSchemaCommand($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}");
    }

    private async Task AddColumnAsync()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        await RunSchemaMutationAsync($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}");
    }

    private void CreateIndex()
    {
        if (_workingPath is null || !_dialogService.ConfirmDestructiveSql(CreateIndexSql)) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        ExecuteSchemaCommand(CreateIndexSql);
    }

    private async Task CreateIndexAsync()
    {
        if (_workingPath is null || !_dialogService.ConfirmDestructiveSql(CreateIndexSql)) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        await RunSchemaMutationAsync(CreateIndexSql);
    }

    private void ExecuteSchemaCommand(string sql)
    {
        try
        {
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            RefreshTables();
            CaptureWorkingFingerprint();
            StatusMessage = "Schema change completed on the working copy.";
            RecordAudit("Schema", sql);
        }
        catch (Exception ex) { ShowRawError(ex); }
    }

    private async Task RunSchemaMutationAsync(string sql)
    {
        if (_workingPath is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            await Task.Run(() =>
            {
                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            });
            RefreshTables();
            StatusMessage = "Schema change completed on the working copy.";
            RecordAudit("Schema", SchemaUndo.TryGetInverse(sql, out var inverse) ? $"{sql} | inverse: {inverse}" : $"{sql} | inverse: unsupported");
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; }
    }

    private bool CreateAutomaticBackup(out string error)
    {
        error = string.Empty;
        if (_workingPath is null) return true;
        var directory = Path.GetDirectoryName(_workingPath) ?? Path.GetTempPath();
        var name = Path.GetFileNameWithoutExtension(_workingPath);
        var backup = Path.Combine(directory, $"{name}.pre-sql-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");
        var result = MaterialDatabaseCrud.createWorkingCopy(_workingPath, backup);
        if (result.TryUnwrap(out _, out var materialError))
        {
            try
            {
                var backups = Directory.GetFiles(directory, $"{name}.pre-sql-*.db")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Skip(10);
                foreach (var oldBackup in backups) File.Delete(oldBackup);
            }
            catch { /* Cleanup is best effort; the new backup remains authoritative. */ }
            return true;
        }
        error = MaterialErrorFormat.Format(materialError);
        return false;
    }

    private void SaveQuery()
    {
        if (string.IsNullOrWhiteSpace(SavedQueryName)) return;
        if (!SavedQueries.Contains(SavedQueryName)) SavedQueries.Add(SavedQueryName);
        _savedQueryTexts[SavedQueryName] = SqlCommandText;
        SaveQueryStore();
        StatusMessage = $"Saved query '{SavedQueryName}'.";
    }

    private void RecordAudit(string operation, string target)
    {
        _auditLog.Insert(0, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {operation} | {target}");
        while (_auditLog.Count > 100) _auditLog.RemoveAt(_auditLog.Count - 1);
        SaveAuditStore();
    }

    private void CaptureWorkingFingerprint()
    {
        if (_workingPath is null || !File.Exists(_workingPath)) return;
        var info = new FileInfo(_workingPath);
        _workingLastWriteUtc = info.LastWriteTimeUtc;
        _workingLength = info.Length;
    }

    private bool WorkingCopyChanged()
    {
        if (_workingPath is null || !File.Exists(_workingPath)) return false;
        var info = new FileInfo(_workingPath);
        return info.LastWriteTimeUtc != _workingLastWriteUtc || info.Length != _workingLength;
    }

    private void LoadAuditStore()
    {
        try
        {
            if (!File.Exists(_auditStorePath)) return;
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_auditStorePath));
            if (entries is null) return;
            foreach (var entry in entries.Take(100)) _auditLog.Add(entry);
        }
        catch { }
    }

    private void SaveAuditStore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_auditStorePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_auditStorePath, System.Text.Json.JsonSerializer.Serialize(_auditLog.ToList()));
        }
        catch { }
    }

    private void LoadSessionStore()
    {
        try
        {
            if (!File.Exists(_sessionStorePath)) return;
            _lastSourcePath = System.Text.Json.JsonSerializer.Deserialize<SessionStore>(File.ReadAllText(_sessionStorePath))?.SourcePath;
            ReopenLastDatabaseCommand.RaiseCanExecuteChanged();
        }
        catch { }
    }

    private void SaveSessionStore()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_sessionStorePath)!);
            File.WriteAllText(_sessionStorePath, System.Text.Json.JsonSerializer.Serialize(new SessionStore(_lastSourcePath)));
            ReopenLastDatabaseCommand.RaiseCanExecuteChanged();
        }
        catch { }
    }

    private sealed record SessionStore(string? SourcePath);

    private void LoadQuery()
    {
        if (_savedQueryTexts.TryGetValue(SavedQueryName, out var query)) SqlCommandText = query;
    }

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

    private static bool IsDestructiveSql(string sql)
    {
        var text = sql.TrimStart();
        return new[] { "DELETE", "DROP", "ALTER", "UPDATE", "INSERT", "REPLACE", "VACUUM" }
            .Any(keyword => text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadQueryStore()
    {
        try
        {
            if (!File.Exists(_queryStorePath)) return;
            var store = System.Text.Json.JsonSerializer.Deserialize<QueryStore>(File.ReadAllText(_queryStorePath));
            if (store is null) return;
            foreach (var item in store.History.Take(50)) SqlHistory.Add(item);
            foreach (var item in store.Saved) { SavedQueries.Add(item.Name); _savedQueryTexts[item.Name] = item.Sql; }
        }
        catch { /* Corrupt preferences must not prevent the database window from opening. */ }
    }

    private void SaveQueryStore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_queryStorePath)!;
            Directory.CreateDirectory(directory);
            var store = new QueryStore(SqlHistory.ToList(), _savedQueryTexts.Select(pair => new SavedQuery(pair.Key, pair.Value)).ToList());
            File.WriteAllText(_queryStorePath, System.Text.Json.JsonSerializer.Serialize(store, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Preferences are optional and must never break database work. */ }
    }

    private sealed record QueryStore(List<string> History, List<SavedQuery> Saved);
    private sealed record SavedQuery(string Name, string Sql);

    private void PlotTable()
    {
        if (_currentTable is null) return;

        var yColumns = (string.IsNullOrWhiteSpace(PlotYColumns) ? PlotYColumn : PlotYColumns)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _plotSeries.Clear();
        var values = new List<(double X, double Y)>();
        foreach (DataRow row in _currentTable.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (!TryGetDouble(row[PlotXColumn], out var x)) continue;
            foreach (var yColumn in yColumns)
                if (row.Table.Columns.Contains(yColumn) && TryGetDouble(row[yColumn], out var y))
                    values.Add((x, y));
        }

        if (values.Count == 0)
        {
            PlotPoints = [];
            PlotXAxisMinimum = "";
            PlotXAxisMaximum = "";
            PlotYAxisMinimum = "";
            PlotYAxisMaximum = "";
            RaisePropertyChanged(nameof(PlotSeries));
            RaisePropertyChanged(nameof(PlotXAxisMinimum));
            RaisePropertyChanged(nameof(PlotXAxisMaximum));
            RaisePropertyChanged(nameof(PlotYAxisMinimum));
            RaisePropertyChanged(nameof(PlotYAxisMaximum));
            PlotMessage = "No numeric rows found in the loaded page for those columns.";
            return;
        }

        var minX = values.Min(v => v.X);
        var maxX = values.Max(v => v.X);
        var minY = values.Min(v => v.Y);
        var maxY = values.Max(v => v.Y);
        var spanX = Math.Max(maxX - minX, double.Epsilon);
        var spanY = Math.Max(maxY - minY, double.Epsilon);
        PlotXAxisMinimum = minX.ToString("G5");
        PlotXAxisMaximum = maxX.ToString("G5");
        PlotYAxisMinimum = minY.ToString("G5");
        PlotYAxisMaximum = maxY.ToString("G5");
        PlotXAxisQuarter1 = (minX + spanX * 0.25).ToString("G5");
        PlotXAxisQuarter2 = (minX + spanX * 0.50).ToString("G5");
        PlotXAxisQuarter3 = (minX + spanX * 0.75).ToString("G5");
        PlotYAxisQuarter1 = (minY + spanY * 0.25).ToString("G5");
        PlotYAxisQuarter2 = (minY + spanY * 0.50).ToString("G5");
        PlotYAxisQuarter3 = (minY + spanY * 0.75).ToString("G5");
        RaisePropertyChanged(nameof(PlotXAxisMinimum));
        RaisePropertyChanged(nameof(PlotXAxisMaximum));
        RaisePropertyChanged(nameof(PlotYAxisMinimum));
        RaisePropertyChanged(nameof(PlotYAxisMaximum));
        RaisePropertyChanged(nameof(PlotXAxisQuarter1)); RaisePropertyChanged(nameof(PlotXAxisQuarter2)); RaisePropertyChanged(nameof(PlotXAxisQuarter3));
        RaisePropertyChanged(nameof(PlotYAxisQuarter1)); RaisePropertyChanged(nameof(PlotYAxisQuarter2)); RaisePropertyChanged(nameof(PlotYAxisQuarter3));
        const double width = 680.0;
        const double height = 240.0;
        const double pad = 18.0;

        for (var seriesIndex = 0; seriesIndex < yColumns.Count; seriesIndex++)
        {
            var yColumn = yColumns[seriesIndex];
            var points = new PointCollection();
            foreach (DataRow row in _currentTable.Rows)
            {
                if (!TryGetDouble(row[PlotXColumn], out var x) || !row.Table.Columns.Contains(yColumn) || !TryGetDouble(row[yColumn], out var y)) continue;
                points.Add(new Point(pad + ((x - minX) / spanX) * (width - pad * 2.0), height - pad - ((y - minY) / spanY) * (height - pad * 2.0)));
            }
            _plotSeries.Add(new PlotSeriesViewModel(yColumn, points, seriesIndex));
        }

        RaisePropertyChanged(nameof(PlotSeries));

        PlotPoints = _plotSeries.FirstOrDefault()?.Points ?? [];
        PlotMessage = $"{values.Count:N0} point(s), {PlotXAxisLabel}: {minX:G4}..{maxX:G4}, {PlotYAxisLabel}: {minY:G4}..{maxY:G4}.";
    }

    private void ImportSelected()
    {
        if (_workingPath is null || SelectedMaterial is null) return;

        if (!MaterialDatabaseCrud.readMaterial(_workingPath, SelectedMaterial.MaterialKey)
                .TryUnwrap(out var material, out var error))
        {
            ShowError(error);
            return;
        }

        _imported.RemoveAll(m => string.Equals(m.Id, material.Id, StringComparison.Ordinal));
        _imported.Add(material);
        StatusMessage = $"Imported {material.Id}; {_imported.Count} material(s) queued for the library.";
    }

    private void ExportLibrary()
    {
        if (_workingPath is null) return;

        if (!ForeignKeysAreValid(_workingPath, out var foreignKeyError))
        {
            ShowRawError(new InvalidOperationException(foreignKeyError));
            return;
        }

        if (!MaterialDatabaseCrud.upsertMaterials(_workingPath, _libraryMaterials.ToFSharpList())
                .TryUnwrap(out var changes, out var error))
        {
            ShowError(error);
            return;
        }

        _ = RefreshAllAsync();
        StatusMessage = $"Wrote {changes.ToReadOnlyList().Count} material(s) to the working copy.";
    }

    private static bool ForeignKeysAreValid(string path, out string error)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { error = string.Empty; return true; }
        error = $"Foreign-key validation failed in table '{reader.GetString(0)}', row {reader.GetValue(1)}.";
        return false;
    }

    private void DeleteSelected()
    {
        if (_workingPath is null || SelectedMaterial is null) return;
        var key = SelectedMaterial.MaterialKey;
        if (!_dialogService.ConfirmDelete(key)) return;

        if (!MaterialDatabaseCrud.deleteMaterial(_workingPath, key).TryUnwrap(out var change, out var error))
        {
            ShowError(error);
            return;
        }

        _ = RefreshAllAsync();
        StatusMessage = change.Message;
    }

    private void SaveWorkingCopyAs()
    {
        if (_workingPath is null) return;
        var target = _dialogService.AskSavePath("Save database as", FileFilters.Database, _sourcePath);
        if (target is null) return;

        if (!MaterialDatabaseCrud.createWorkingCopy(_workingPath, target).TryUnwrap(out var saved, out var error))
        {
            ShowError(error);
            return;
        }

        StatusMessage = $"Saved database to {saved}.";
    }

    private SqliteConnection OpenRawConnection()
    {
        var connection = new SqliteConnection($"Data Source={_workingPath}");
        connection.Open();
        return connection;
    }

    private static int CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int CountRows(SqliteConnection connection, string tableName, IReadOnlyList<string> columns, string filter)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}" + BuildSqlSearch(columns, filter);
        if (!string.IsNullOrWhiteSpace(filter)) command.Parameters.AddWithValue("$search", "%" + filter + "%");
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static List<string> GetColumnNames(SqliteConnection connection, string tableName)
    {
        var columns = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var reader = command.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static string BuildSqlSearch(IReadOnlyList<string> columns, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || columns.Count == 0) return string.Empty;
        return " WHERE " + string.Join(" OR ", columns.Select(c => $"CAST({QuoteIdentifier(c)} AS TEXT) LIKE $search"));
    }

    private static int InsertRawRow(SqliteConnection connection, SqliteTransaction transaction, string tableName, DataRow row)
    {
        var columns = row.Table.Columns.Cast<DataColumn>().Where(c => c.ColumnName != "__rowid").ToList();
        if (columns.Count == 0) return 0;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", columns.Select(c => QuoteIdentifier(c.ColumnName)))}) " +
            $"VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i.ToString(CultureInfo.InvariantCulture)))})";

        for (var i = 0; i < columns.Count; i++)
        {
            command.Parameters.AddWithValue("$p" + i.ToString(CultureInfo.InvariantCulture), NormalizeDbValue(row[columns[i]]));
        }

        return command.ExecuteNonQuery();
    }

    private int UpdateRawRow(SqliteConnection connection, SqliteTransaction transaction, string tableName, DataRow row)
    {
        var columns = row.Table.Columns.Cast<DataColumn>().Where(c => c.ColumnName != "__rowid").ToList();
        if (columns.Count == 0) return 0;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"UPDATE {QuoteIdentifier(tableName)} SET {string.Join(", ", columns.Select((c, i) => $"{QuoteIdentifier(c.ColumnName)} = $p{i.ToString(CultureInfo.InvariantCulture)}"))} " +
            ((_currentPrimaryKeyColumns.Count > 0 && !row.Table.Columns.Contains("__rowid"))
                ? "WHERE " + string.Join(" AND ", _currentPrimaryKeyColumns.Select((c, i) => $"{QuoteIdentifier(c)} = $key{i}"))
                : "WHERE rowid = $rowid");

        for (var i = 0; i < columns.Count; i++)
        {
            command.Parameters.AddWithValue("$p" + i.ToString(CultureInfo.InvariantCulture), NormalizeDbValue(row[columns[i], DataRowVersion.Current]));
        }

        if (_currentPrimaryKeyColumns.Count > 0 && !row.Table.Columns.Contains("__rowid"))
        {
            for (var i = 0; i < _currentPrimaryKeyColumns.Count; i++)
                command.Parameters.AddWithValue("$key" + i, NormalizeDbValue(row[_currentPrimaryKeyColumns[i], DataRowVersion.Original]));
        }
        else if (TryGetRowId(row, DataRowVersion.Original, out var rowId))
            command.Parameters.AddWithValue("$rowid", rowId);
        else return 0;
        return command.ExecuteNonQuery();
    }

    private int DeleteRawRow(SqliteConnection connection, SqliteTransaction transaction, string tableName, DataRow row)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (_currentPrimaryKeyColumns.Count > 0 && !row.Table.Columns.Contains("__rowid"))
        {
            command.CommandText = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE " + string.Join(" AND ", _currentPrimaryKeyColumns.Select((c, i) => $"{QuoteIdentifier(c)} = $key{i}"));
            for (var i = 0; i < _currentPrimaryKeyColumns.Count; i++)
                command.Parameters.AddWithValue("$key" + i, NormalizeDbValue(row[_currentPrimaryKeyColumns[i], DataRowVersion.Original]));
        }
        else
        {
            if (!TryGetRowId(row, DataRowVersion.Original, out var rowId)) return 0;
            command.CommandText = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE rowid = $rowid";
            command.Parameters.AddWithValue("$rowid", rowId);
        }
        return command.ExecuteNonQuery();
    }

    private void RefreshColumnList(DataTable table)
    {
        TableColumns.Clear();
        foreach (DataColumn column in table.Columns)
        {
            if (column.ColumnName != "__rowid") TableColumns.Add(column.ColumnName);
        }

        PlotXColumn = TableColumns.Contains(PlotXColumn) ? PlotXColumn : TableColumns.FirstOrDefault() ?? string.Empty;
        PlotYColumn = TableColumns.Contains(PlotYColumn) ? PlotYColumn : TableColumns.Skip(1).FirstOrDefault() ?? PlotXColumn;
        PlotTableCommand.RaiseCanExecuteChanged();
    }

    private void ApplyTableView()
    {
        if (_currentTable is null) return;
        var view = _currentTable.DefaultView;
        try
        {
            view.RowFilter = BuildRowFilter(_rowFilter, _currentTable.Columns);
            view.Sort = string.IsNullOrWhiteSpace(_sortColumn)
                ? string.Empty
                : QuoteViewIdentifier(_sortColumn) + (_sortDescending ? " DESC" : " ASC");
            TableRows = view;
        }
        catch (EvaluateException)
        {
            StatusMessage = "Filter expression is invalid.";
        }
    }

    private void LoadSchema(SqliteConnection connection, string tableName)
    {
        TableSchema.Clear();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetInt32(5) > 0) _currentPrimaryKeyColumns.Add(reader.GetString(1));
            TableSchema.Add(new DatabaseColumnViewModel(
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetInt32(3) == 0,
                reader.GetInt32(5) > 0,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)})";
        using var fkReader = foreignKeys.ExecuteReader();
        while (fkReader.Read())
        {
            TableForeignKeys.Add(new DatabaseForeignKeyViewModel(
                fkReader.GetString(2), fkReader.GetString(3), fkReader.GetString(4)));
        }
    }

    private void ClearTableTools()
    {
        TableSearch = string.Empty;
        RowFilter = string.Empty;
        SortColumn = string.Empty;
        SortDescending = false;
        _ = RefreshSelectedTableAsync();
    }

    private void UndoTable()
    {
        if (_currentTable is null || _tableUndoSnapshot is null) return;
        _tableRedoSnapshot = _currentTable.Copy();
        ReplaceCurrentTable(_tableUndoSnapshot.Copy());
        StatusMessage = "Undid raw-table changes for the current page.";
    }

    private void RedoTable()
    {
        if (_currentTable is null || _tableRedoSnapshot is null) return;
        _tableUndoSnapshot = _currentTable.Copy();
        ReplaceCurrentTable(_tableRedoSnapshot.Copy());
        _tableRedoSnapshot = null;
        StatusMessage = "Redid raw-table changes for the current page.";
    }

    private void DiscardTableChanges()
    {
        if (_tableUndoSnapshot is null) return;
        ReplaceCurrentTable(_tableUndoSnapshot.Copy());
        _tableRedoSnapshot = null;
        StatusMessage = "Discarded unsaved raw-table changes.";
    }

    private void ReplaceCurrentTable(DataTable table)
    {
        table.AcceptChanges();
        _currentTable = table;
        TableRows = table.DefaultView;
        HasUnsavedTableChanges = false;
        RefreshEditCommands();
    }

    private void RefreshEditCommands()
    {
        UndoTableCommand.RaiseCanExecuteChanged();
        RedoTableCommand.RaiseCanExecuteChanged();
        DiscardTableChangesCommand.RaiseCanExecuteChanged();
        UndoLastTransactionCommand.RaiseCanExecuteChanged();
        RedoLastTransactionCommand.RaiseCanExecuteChanged();
    }

    private async Task RedoLastTransactionAsync()
    {
        if (_workingPath is null || HasUnsavedTableChanges) return;
        var entry = _transactionJournal.LoadRedo().LastOrDefault(item => string.Equals(item.DatabasePath, _workingPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            await Task.Run(() => TransactionReverter.Apply(path, entry));
            _transactionJournal.RemoveRedo(entry);
            _transactionJournal.Append(entry);
            CaptureWorkingFingerprint();
            _transactionHistory = _transactionJournal.Load();
            RaisePropertyChanged(nameof(TransactionHistory));
            await RefreshSelectedTableAsync();
            await RefreshMaterialsAsync();
            StatusMessage = $"Redid the transaction on {entry.TableName}.";
            RecordAudit("Redo transaction", entry.TableName);
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; RefreshEditCommands(); }
    }

    private async Task UndoLastTransactionAsync()
    {
        if (_workingPath is null || HasUnsavedTableChanges) return;
        var entry = TransactionHistory.LastOrDefault(item => string.Equals(item.DatabasePath, _workingPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            await Task.Run(() => TransactionReverter.Revert(path, entry));
            CaptureWorkingFingerprint();
            _transactionJournal.PushRedo(entry);
            _transactionJournal.Remove(entry);
            _transactionHistory = _transactionJournal.Load();
            RaisePropertyChanged(nameof(TransactionHistory));
            await RefreshSelectedTableAsync();
            await RefreshMaterialsAsync();
            StatusMessage = $"Undid the transaction on {entry.TableName}.";
            RecordAudit("Undo transaction", entry.TableName);
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; RefreshEditCommands(); }
    }

    private async Task IntegrityCheckAsync()
    {
        if (_workingPath is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            var result = await Task.Run(() =>
            {
                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check";
                return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "unknown";
            });
            StatusMessage = result.Equals("ok", StringComparison.OrdinalIgnoreCase)
                ? "SQLite integrity check passed."
                : $"SQLite integrity check reported: {result}";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

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

    private void RefreshTableCommands()
    {
        RefreshTableCommand.RaiseCanExecuteChanged();
        PreviousTablePageCommand.RaiseCanExecuteChanged();
        NextTablePageCommand.RaiseCanExecuteChanged();
        SaveTableChangesCommand.RaiseCanExecuteChanged();
        PlotTableCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(TablePageDisplay));
    }

    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static bool ReturnsRows(string sql)
    {
        var trimmed = sql.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetRowId(DataRow row, DataRowVersion version, out long rowId)
    {
        rowId = 0;
        return row.Table.Columns.Contains("__rowid")
            && row["__rowid", version] is not DBNull
            && long.TryParse(Convert.ToString(row["__rowid", version], CultureInfo.InvariantCulture), out rowId);
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

public sealed class PlotSeriesViewModel(string name, PointCollection points, int index)
{
    public string Name { get; } = name;
    public PointCollection Points { get; } = points;
    public System.Windows.Media.Brush Stroke { get; } = new System.Windows.Media.SolidColorBrush(new[] { Colors.DodgerBlue, Colors.IndianRed, Colors.ForestGreen, Colors.DarkOrange, Colors.Purple }[index % 5]);
}

/// <summary>Read-only projection of a SQLite table or view.</summary>
internal sealed record SqlExecutionResult(DataTable? Rows, int Changes);
internal sealed record RawRowChange(DataRowState State, Dictionary<string, object?> Current, Dictionary<string, object?> Original, long? RowId);
internal sealed record RawCommitResult(int Changed, IReadOnlyList<RawRowChange> Changes);

public sealed class DatabaseTableViewModel(string name, string kind)
{
    public string Name { get; } = name;
    public string Kind { get; } = kind;
    public bool IsReadOnly => Kind != "table";
    public string DisplayName => $"{Name} ({Kind})";
}

public sealed record DatabaseColumnViewModel(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, string DefaultValue)
{
    public string PrimaryKeyDisplay => IsPrimaryKey ? "Yes" : "";
    public string NullableDisplay => IsNullable ? "Yes" : "No";
}

public sealed record DatabaseForeignKeyViewModel(string ReferencedTable, string FromColumn, string ToColumn);

/// <summary>Read-only projection of one database material summary for the grid.</summary>
public sealed class DatabaseRowViewModel
{
    public DatabaseRowViewModel(DatabaseMaterialSummary summary)
    {
        DatabaseId = summary.DatabaseId;
        MaterialKey = summary.MaterialKey;
        Name = summary.Name;
        Specification = summary.Specification;
        Grade = summary.Grade;
        HasDocument = summary.HasDocument;
    }

    public long DatabaseId { get; }
    public string MaterialKey { get; }
    public string Name { get; }
    public string Specification { get; }
    public string Grade { get; }
    public bool HasDocument { get; }
    public string Origin => HasDocument ? "Application" : "ASME reference";
}
