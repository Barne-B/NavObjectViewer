using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using NavObjectViewer.ViewModels;

namespace NavObjectViewer.Views;

public partial class MainWindow : Window
{
    // Beyond these sizes, C/AL highlighting + no-wrap layout gets pathological
    // (e.g. a Report's RDLDATA is one multi-MB base64 line), so we render plainly.
    private const int LargeTextThreshold = 1_000_000;
    private const int LongLineThreshold = 5_000;

    private readonly MainViewModel _vm;
    private IHighlightingDefinition? _calHighlighting;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        LoadHighlighting();
        Loaded += OnLoaded;
    }

    // Command-line auto-open: NavObjectViewer.exe "<path>" [--select-first] [--select-largest] [--search <term>]
    // Used for headless reproduction and as an "open with" convenience.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        string[] cli = Environment.GetCommandLineArgs();
        if (cli.Length < 2)
            return;

        string path = cli[1];
        try
        {
            if (Directory.Exists(path))
                await _vm.LoadPathAsync(path, isFolder: true);
            else if (File.Exists(path))
                await _vm.LoadPathAsync(path, isFolder: false);

            if (cli.Contains("--select-first") && _vm.RowsView is not null)
                _vm.SelectedRow = _vm.RowsView.Cast<ObjectRowViewModel>().FirstOrDefault();

            // Verification hook: open the largest object (usually a Report with big RDLDATA).
            if (cli.Contains("--select-largest") && _vm.RowsView is not null)
                _vm.SelectedRow = _vm.RowsView.Cast<ObjectRowViewModel>()
                    .OrderByDescending(r => r.Model.ByteLength).FirstOrDefault();

            int si = Array.IndexOf(cli, "--search");
            if (si >= 0 && si + 1 < cli.Length)
            {
                _vm.SearchScope = SearchScopeKind.All;
                _vm.SearchQuery = cli[si + 1];
                await _vm.SearchCommand.ExecuteAsync(null);
                if (_vm.SearchNodes.Count > 0)
                    _vm.SearchNodes[0].IsExpanded = true; // force lazy child load + tree render
            }
        }
        catch (Exception ex)
        {
            Logging.Log("OnLoaded auto-open", ex);
        }
    }

    private void LoadHighlighting()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("NavObjectViewer.Highlighting.CAL.xshd");
            if (stream is null)
                return;

            using XmlReader reader = XmlReader.Create(stream);
            _calHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            Editor.SyntaxHighlighting = _calHighlighting;
        }
        catch
        {
            // Highlighting is cosmetic; ignore load failures.
        }
    }

    // Push object text into the editor, degrading gracefully for very large or
    // very-long-line objects so rendering never hangs or throws unhandled.
    private void SetViewerText(string text)
    {
        try
        {
            bool heavy = text.Length > LargeTextThreshold || HasLongLine(text, LongLineThreshold);
            Editor.SyntaxHighlighting = heavy ? null : _calHighlighting;
            Editor.WordWrap = heavy;
            Editor.Text = text;
            Editor.ScrollToHome();
        }
        catch (Exception ex)
        {
            Logging.Log("SetViewerText", ex);
            try
            {
                Editor.SyntaxHighlighting = null;
                Editor.WordWrap = true;
                Editor.Text = $"// This object could not be displayed ({ex.GetType().Name}: {ex.Message}).";
            }
            catch { /* give up on the viewer, but never crash */ }
        }
    }

    private static bool HasLongLine(string text, int threshold)
    {
        int run = 0;
        foreach (char c in text)
        {
            if (c == '\n')
                run = 0;
            else if (++run > threshold)
                return true;
        }
        return false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ViewerText):
                SetViewerText(_vm.ViewerText);
                break;

            case nameof(MainViewModel.ViewerScrollLine):
                int line = _vm.ViewerScrollLine;
                if (line > 0 && line <= Editor.Document.LineCount)
                {
                    Editor.ScrollToLine(line);
                    Editor.TextArea.Caret.Line = line;
                    Editor.TextArea.Caret.Column = 1;
                }
                break;

            case nameof(MainViewModel.SelectedRow) when _vm.SelectedRow is not null:
                ObjectsGrid.ScrollIntoView(_vm.SelectedRow);
                break;
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var objects = _vm.CheckedObjects;
        if (objects.Count == 0)
        {
            MessageBox.Show(this, "No objects are selected. Tick the checkboxes of the objects you want to export.",
                "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ExportDialog(objects) { Owner = this };
        dialog.ShowDialog();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.SearchCommand.CanExecute(null))
            _vm.SearchCommand.Execute(null);
    }

    private void ResultsTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Object node → open it (Line = 0, no scroll); line node → jump to that line.
        if (ResultsTree.SelectedItem is SearchNodeViewModel node && node.Object is not null)
            _vm.JumpToLine(node.Object, node.Line);
    }
}
