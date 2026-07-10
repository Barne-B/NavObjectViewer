using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;

namespace NavObjectViewer.ViewModels;

public enum SearchScopeKind { All, Filtered, CustomRange }

public partial class MainViewModel : ObservableObject
{
    private readonly ObjectIndexer _indexer = new();
    private readonly FullTextSearch _search = new();
    private readonly HashSet<NavObjectType> _enabledTypes = [];

    private List<ObjectRowViewModel> _allRows = [];
    private int? _idMin;
    private int? _idMax;

    public MainViewModel()
    {
        TypeToggles = new ObservableCollection<TypeToggle>(
            new[]
            {
                NavObjectType.Table, NavObjectType.Page, NavObjectType.Report, NavObjectType.Codeunit,
                NavObjectType.XMLport, NavObjectType.Query, NavObjectType.MenuSuite,
                NavObjectType.Dataport, NavObjectType.Form,
            }.Select(t => new TypeToggle(t, RefreshView)));

        foreach (var t in TypeToggles)
            _enabledTypes.Add(t.Type);

        SearchScopes = new ObservableCollection<SearchScopeKind>(Enum.GetValues<SearchScopeKind>());
    }

    // ---- Collections -------------------------------------------------------
    public ObservableCollection<TypeToggle> TypeToggles { get; }
    public ObservableCollection<SearchScopeKind> SearchScopes { get; }

    /// <summary>Grouped search results: object nodes with lazily-loaded line children.</summary>
    public ObservableCollection<SearchNodeViewModel> SearchNodes { get; } = [];

    /// <summary>Raised after a search finishes populating results, so the view can reset the results scroll.</summary>
    public event Action? SearchCompleted;

    [ObservableProperty]
    private ICollectionView? _rowsView;

    // Content-based grid filter: when set, the grid shows only these objects.
    private HashSet<NavObject>? _contentFilterIds;
    private List<ObjectHit> _lastHits = [];
    private string _lastQuery = string.Empty;
    private bool _lastIgnoreCase = true;
    private bool _lastUseRegex;

    // ---- Filter inputs -----------------------------------------------------
    [ObservableProperty] private string _filterName = string.Empty;
    [ObservableProperty] private string _filterVersion = string.Empty;
    [ObservableProperty] private string _filterIdMin = string.Empty;
    [ObservableProperty] private string _filterIdMax = string.Empty;
    [ObservableProperty] private bool _showModifiedOnly;
    [ObservableProperty] private bool _showAnomalousOnly;

    partial void OnFilterNameChanged(string value) => RefreshView();
    partial void OnFilterVersionChanged(string value) => RefreshView();
    partial void OnShowModifiedOnlyChanged(bool value) => RefreshView();
    partial void OnShowAnomalousOnlyChanged(bool value) => RefreshView();

    partial void OnFilterIdMinChanged(string value)
    {
        _idMin = int.TryParse(value, out int v) ? v : null;
        RefreshView();
    }

    partial void OnFilterIdMaxChanged(string value)
    {
        _idMax = int.TryParse(value, out int v) ? v : null;
        RefreshView();
    }

    // ---- Viewer ------------------------------------------------------------
    [ObservableProperty] private ObjectRowViewModel? _selectedRow;
    [ObservableProperty] private string _viewerText = string.Empty;
    [ObservableProperty] private string _viewerHeader = "No object selected";
    [ObservableProperty] private int _viewerScrollLine;

    partial void OnSelectedRowChanged(ObjectRowViewModel? value)
    {
        if (value is null)
        {
            ViewerText = string.Empty;
            ViewerHeader = "No object selected";
            return;
        }

        try
        {
            ViewerText = ObjectReader.ReadText(value.Model);
            ViewerHeader = $"{value.Model} — {value.Model.ByteLength:N0} bytes — {value.SourceFileName}"
                + (value.IsAnomalous ? "  ⚠ bracketed header" : string.Empty);
        }
        catch (Exception ex)
        {
            ViewerText = $"// Failed to read object: {ex.Message}";
        }
    }

    // ---- Status ------------------------------------------------------------
    [ObservableProperty] private string _statusText = "Open a C/AL export file or a folder to begin.";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _visibleCount;
    [ObservableProperty] private int _checkedCount;
    [ObservableProperty] private string _sourceLabel = string.Empty;

    // ---- Search inputs -----------------------------------------------------
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _searchIgnoreCase = true;
    [ObservableProperty] private bool _searchUseRegex;
    [ObservableProperty] private SearchScopeKind _searchScope = SearchScopeKind.Filtered;
    [ObservableProperty] private string _searchStatus = string.Empty;

    // ---- Content filter (search-driven grid filter) ------------------------
    [ObservableProperty] private bool _contentFilterActive;
    [ObservableProperty] private string _contentFilterLabel = string.Empty;

    // ---- Open commands -----------------------------------------------------
    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open C/AL object export",
            Filter = "Text exports (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;

        await LoadAsync(dlg.FileName, isFolder: false);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var dlg = new OpenFolderDialog { Title = "Open a folder of C/AL exports" };
        if (dlg.ShowDialog() != true)
            return;

        await LoadAsync(dlg.FolderName, isFolder: true);
    }

    /// <summary>Load a file/folder by path (used by command-line auto-open and tests).</summary>
    public Task LoadPathAsync(string path, bool isFolder) => LoadAsync(path, isFolder);

    private async Task LoadAsync(string path, bool isFolder)
    {
        IsBusy = true;
        Progress = 0;
        StatusText = $"Indexing {path}…";
        var progress = new Progress<double>(p => Progress = p);

        try
        {
            List<NavObject> objects = await Task.Run(() =>
            {
                if (isFolder)
                    return _indexer.IndexDirectory(path, recursive: true, progress);

                List<NavObject>? cached = IndexCache.TryLoad(path);
                if (cached is not null)
                    return cached;

                List<NavObject> fresh = _indexer.IndexFile(path, progress);
                IndexCache.Save(path, fresh);
                return fresh;
            });

            PopulateRows(objects);
            SourceLabel = isFolder ? $"Folder: {path}" : $"File: {Path.GetFileName(path)}";
            StatusText = $"Indexed {objects.Count:N0} objects.";
        }
        catch (Exception ex)
        {
            Logging.Log("LoadAsync", ex);
            StatusText = $"Failed to open: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    private void PopulateRows(List<NavObject> objects)
    {
        _allRows = new List<ObjectRowViewModel>(objects.Count);
        foreach (NavObject o in objects)
        {
            var row = new ObjectRowViewModel(o);
            row.PropertyChanged += OnRowPropertyChanged;
            _allRows.Add(row);
        }

        var view = new CollectionViewSource { Source = _allRows }.View;
        view.Filter = FilterPredicate;
        view.SortDescriptions.Add(new SortDescription(nameof(ObjectRowViewModel.TypeName), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(ObjectRowViewModel.Id), ListSortDirection.Ascending));
        RowsView = view;

        foreach (TypeToggle t in TypeToggles)
            t.Count = objects.Count(o => o.Type == t.Type);

        TotalCount = _allRows.Count;
        CheckedCount = 0;
        SelectedRow = null;
        SearchNodes.Clear();
        _lastHits = [];
        _contentFilterIds = null;
        ContentFilterActive = false;
        ContentFilterLabel = string.Empty;
        RefreshView();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectRowViewModel.IsChecked))
            CheckedCount = _allRows.Count(r => r.IsChecked);
    }

    // ---- Filtering ---------------------------------------------------------
    private bool FilterPredicate(object obj)
    {
        var r = (ObjectRowViewModel)obj;

        if (_contentFilterIds is not null && !_contentFilterIds.Contains(r.Model))
            return false;
        if (!_enabledTypes.Contains(r.Model.Type))
            return false;
        if (ShowModifiedOnly && !r.Modified)
            return false;
        if (ShowAnomalousOnly && !r.IsAnomalous)
            return false;
        if (_idMin.HasValue && r.Id < _idMin.Value)
            return false;
        if (_idMax.HasValue && r.Id > _idMax.Value)
            return false;
        if (!string.IsNullOrWhiteSpace(FilterVersion)
            && r.VersionList.IndexOf(FilterVersion, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (!WildcardMatcher.IsMatch(r.Name, FilterName))
            return false;

        return true;
    }

    private void RefreshView()
    {
        _enabledTypes.Clear();
        foreach (TypeToggle t in TypeToggles)
            if (t.IsEnabled)
                _enabledTypes.Add(t.Type);

        RowsView?.Refresh();
        VisibleCount = RowsView?.Cast<object>().Count() ?? 0;
    }

    private IEnumerable<ObjectRowViewModel> VisibleRows =>
        RowsView?.Cast<ObjectRowViewModel>() ?? Enumerable.Empty<ObjectRowViewModel>();

    // ---- Filter commands ---------------------------------------------------
    [RelayCommand]
    private void ClearFilters()
    {
        FilterName = string.Empty;
        FilterVersion = string.Empty;
        FilterIdMin = string.Empty;
        FilterIdMax = string.Empty;
        ShowModifiedOnly = false;
        ShowAnomalousOnly = false;
        foreach (TypeToggle t in TypeToggles)
            t.IsEnabled = true;
        ClearContentFilter();
    }

    [RelayCommand]
    private void PresetCustomRange()
    {
        FilterIdMin = "50000";
        FilterIdMax = string.Empty;
    }

    [RelayCommand]
    private void PresetModified() => ShowModifiedOnly = true;

    // ---- Selection commands ------------------------------------------------
    [RelayCommand]
    private void CheckVisible()
    {
        foreach (ObjectRowViewModel r in VisibleRows.ToList())
            r.IsChecked = true;
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (ObjectRowViewModel r in _allRows)
            r.IsChecked = false;
    }

    [RelayCommand]
    private void InvertVisible()
    {
        foreach (ObjectRowViewModel r in VisibleRows.ToList())
            r.IsChecked = !r.IsChecked;
    }

    public IReadOnlyList<NavObject> CheckedObjects =>
        _allRows.Where(r => r.IsChecked).Select(r => r.Model).ToList();

    // ---- Search ------------------------------------------------------------
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(SearchQuery) || _allRows.Count == 0)
            return;

        IEnumerable<NavObject> scope = SearchScope switch
        {
            SearchScopeKind.All => _allRows.Select(r => r.Model),
            SearchScopeKind.CustomRange => _allRows.Where(r => r.Id >= 50000).Select(r => r.Model),
            _ => VisibleRows.Select(r => r.Model),
        };
        List<NavObject> scopeList = scope.ToList();

        IsBusy = true;
        SearchStatus = "Searching…";
        var progress = new Progress<double>(p => Progress = p);
        _lastQuery = SearchQuery;
        _lastIgnoreCase = SearchIgnoreCase;
        _lastUseRegex = SearchUseRegex;

        try
        {
            List<ObjectHit> hits = await Task.Run(() =>
                _search.SearchObjects(scopeList, _lastQuery, _lastIgnoreCase, _lastUseRegex, progress));

            _lastHits = hits;
            SearchNodes.Clear();
            foreach (ObjectHit hit in hits)
                SearchNodes.Add(SearchNodeViewModel.ForObject(hit.Object, hit.Count, hit.FirstLineText, LoadMatchNodes));

            long totalMatches = hits.Sum(h => (long)h.Count);
            SearchStatus = $"{totalMatches:N0} matches in {hits.Count:N0} object(s) — scanned {scopeList.Count:N0} object(s)";
            SearchCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            SearchStatus = $"Search error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    // Lazy loader invoked when an object node is first expanded.
    private IEnumerable<SearchNodeViewModel> LoadMatchNodes(SearchNodeViewModel node)
    {
        if (node.Object is null)
            return [];

        return _search.MatchesInObject(node.Object, _lastQuery, _lastIgnoreCase, _lastUseRegex)
            .Select(m => SearchNodeViewModel.ForLine(m.Object, m.Line, m.Text));
    }

    /// <summary>Jump the viewer to a specific object and line (from a search result).</summary>
    public void JumpToLine(NavObject obj, int line)
    {
        ObjectRowViewModel? row = _allRows.FirstOrDefault(r => ReferenceEquals(r.Model, obj))
            ?? _allRows.FirstOrDefault(r => r.Model.Type == obj.Type
                && r.Model.Id == obj.Id
                && r.Model.SourceFile == obj.SourceFile);
        if (row is null)
            return;

        SelectedRow = row;
        if (line > 0)
            ViewerScrollLine = line;
    }

    // ---- Content filter commands ------------------------------------------
    [RelayCommand]
    private void FilterGridToMatches()
    {
        if (_lastHits.Count == 0)
        {
            SearchStatus = "Run a search first, then filter the list to its matches.";
            return;
        }

        _contentFilterIds = new HashSet<NavObject>(_lastHits.Select(h => h.Object));
        ContentFilterActive = true;
        ContentFilterLabel = $"Content: \"{_lastQuery}\" ({_contentFilterIds.Count:N0} object(s))";
        RefreshView();
    }

    [RelayCommand]
    private void ClearContentFilter()
    {
        _contentFilterIds = null;
        ContentFilterActive = false;
        ContentFilterLabel = string.Empty;
        RefreshView();
    }
}
