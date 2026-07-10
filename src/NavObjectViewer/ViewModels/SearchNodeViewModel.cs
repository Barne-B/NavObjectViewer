using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.ViewModels;

/// <summary>
/// A node in the grouped search tree. Top-level nodes summarize an object ("COD80040
/// … (12 matches)"); their children are the individual matching lines, loaded lazily
/// the first time the node is expanded so a huge search never materializes every line.
/// </summary>
public partial class SearchNodeViewModel : ObservableObject
{
    private Func<SearchNodeViewModel, IEnumerable<SearchNodeViewModel>>? _loadChildren;
    private bool _loaded;

    /// <summary>Placeholder that makes the expander arrow appear before children are loaded.</summary>
    private static SearchNodeViewModel Placeholder { get; } = new("…");

    private SearchNodeViewModel(string header) => Header = header;

    /// <summary>Create an object (parent) node with a lazy child loader.</summary>
    public static SearchNodeViewModel ForObject(NavObject obj, int count, string previewLine,
        Func<SearchNodeViewModel, IEnumerable<SearchNodeViewModel>> loadChildren)
    {
        string preview = string.IsNullOrWhiteSpace(previewLine) ? string.Empty : $"  —  {previewLine}";
        var node = new SearchNodeViewModel($"{obj.Type.Prefix()}{obj.Id}  {obj.Name}   ({count} match{(count == 1 ? "" : "es")}){preview}")
        {
            Object = obj,
            IsObject = true,
        };
        node._loadChildren = loadChildren;
        node.Children.Add(Placeholder); // so the node is expandable
        return node;
    }

    /// <summary>Create a leaf node for one matching line.</summary>
    public static SearchNodeViewModel ForLine(NavObject obj, int line, string text)
        => new($"{line,6}: {text.Trim()}") { Object = obj, Line = line };

    public string Header { get; }
    public NavObject? Object { get; private init; }
    public int Line { get; private init; }
    public bool IsObject { get; private init; }

    public ObservableCollection<SearchNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _loaded || _loadChildren is null)
            return;

        _loaded = true;
        Children.Clear();
        foreach (SearchNodeViewModel child in _loadChildren(this))
            Children.Add(child);
    }
}
