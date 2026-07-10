using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using NavObjectViewer.Core.Models;

namespace NavObjectViewer.ViewModels;

/// <summary>Grid row wrapping a <see cref="NavObject"/> plus its export-selection state.</summary>
public partial class ObjectRowViewModel : ObservableObject
{
    public ObjectRowViewModel(NavObject model) => Model = model;

    public NavObject Model { get; }

    [ObservableProperty]
    private bool _isChecked;

    public string TypeName => Model.TypeName;
    public int Id => Model.Id;
    public string Name => Model.Name;
    public string VersionList => Model.VersionList;
    public bool Modified => Model.Modified;
    public string Date => Model.Date;
    public bool IsAnomalous => Model.IsAnomalous;
    public string Flag => Model.IsAnomalous ? "⚠" : string.Empty;
    public string SourceFileName => Path.GetFileName(Model.SourceFile);
}
