using CommunityToolkit.Mvvm.ComponentModel;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.ViewModels;

/// <summary>A checkable object-type filter (Table, Page, …) shown in the filter bar.</summary>
public partial class TypeToggle : ObservableObject
{
    private readonly Action _onChanged;

    public TypeToggle(NavObjectType type, Action onChanged)
    {
        Type = type;
        _onChanged = onChanged;
        _isEnabled = true;
    }

    public NavObjectType Type { get; }
    public string Name => Type.ToString();

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private int _count;

    partial void OnIsEnabledChanged(bool value) => _onChanged();

    public string Label => $"{Name} ({Count})";
}
