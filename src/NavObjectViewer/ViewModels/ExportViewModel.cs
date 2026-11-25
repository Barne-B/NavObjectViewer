using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;

namespace NavObjectViewer.ViewModels;

public enum ExportMode { Combined, PerObject, Fob }

public partial class ExportViewModel : ObservableObject
{
    private readonly ObjectExporter _exporter = new();
    private readonly FobWriter _fobWriter = new();
    private readonly IReadOnlyList<NavObject> _objects;

    public ExportViewModel(IReadOnlyList<NavObject> objects)
    {
        _objects = objects;
        _objectCount = objects.Count;
        _fobObjectCount = objects.Count(o => o.IsFob);
    }

    /// <summary>Selected objects that come from a .fob (only these can be written to a new .fob).</summary>
    public int FobObjectCount => _fobObjectCount;
    private readonly int _fobObjectCount;

    public bool CanExportFob => _fobObjectCount > 0;

    /// <summary>Hint shown when the selection contains .fob objects, or the .fob mode is chosen.</summary>
    public string FobHint => Mode switch
    {
        ExportMode.Fob when _fobObjectCount < ObjectCount =>
            $"Only the {_fobObjectCount:N0} object(s) read from a .fob can be written; {ObjectCount - _fobObjectCount:N0} text-export object(s) will be skipped (compiling text needs C/SIDE).",
        ExportMode.Fob => "Compiled objects are copied byte-for-byte from the source .fob file(s).",
        _ when _fobObjectCount > 0 =>
            $"{_fobObjectCount:N0} object(s) come from a .fob: they are written as decoded code (triggers and functions only) and cannot be re-imported into C/SIDE.",
        _ => string.Empty,
    };

    public bool HasFobHint => FobHint.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCombined))]
    [NotifyPropertyChangedFor(nameof(IsPerObject))]
    [NotifyPropertyChangedFor(nameof(FobHint))]
    [NotifyPropertyChangedFor(nameof(HasFobHint))]
    private ExportMode _mode = ExportMode.Combined;

    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private bool _groupByType = true;
    [ObservableProperty] private int _objectCount;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    public bool IsCombined => Mode == ExportMode.Combined;
    public bool IsPerObject => Mode == ExportMode.PerObject;

    partial void OnModeChanged(ExportMode value) => OutputPath = string.Empty;

    [RelayCommand]
    private void Browse()
    {
        if (Mode == ExportMode.Fob)
        {
            var fobDlg = new SaveFileDialog
            {
                Title = "Save as new .fob",
                Filter = "Binary objects (*.fob)|*.fob",
                FileName = "NAV-Export-Selection.fob",
            };
            if (fobDlg.ShowDialog() == true)
                OutputPath = fobDlg.FileName;
        }
        else if (IsCombined)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save combined export",
                Filter = "Text export (*.txt)|*.txt",
                FileName = "NAV-Export-Selection.txt",
            };
            if (dlg.ShowDialog() == true)
                OutputPath = dlg.FileName;
        }
        else
        {
            var dlg = new OpenFolderDialog { Title = "Choose an output folder" };
            if (dlg.ShowDialog() == true)
                OutputPath = dlg.FolderName;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            ResultText = "Choose an output location first.";
            return;
        }
        if (_objects.Count == 0)
        {
            ResultText = "No objects selected.";
            return;
        }

        IsBusy = true;
        Progress = 0;
        ResultText = "Exporting…";
        var progress = new Progress<double>(p => Progress = p);
        ExportMode mode = Mode;
        string path = OutputPath;
        bool group = GroupByType;

        try
        {
            ExportResult result = await Task.Run(() => mode switch
            {
                ExportMode.Combined => _exporter.ExportCombined(_objects, path, progress),
                ExportMode.Fob => _fobWriter.Write(_objects, path, progress),
                _ => _exporter.ExportPerObject(_objects, path, group, progress),
            });

            string summary = mode == ExportMode.PerObject
                ? $"Wrote {result.FileCount:N0} files ({result.ObjectCount:N0} objects) under:\n{result.OutputPath}"
                : $"Wrote {result.ObjectCount:N0} objects to:\n{result.OutputPath}";

            if (result.Warnings.Count > 0)
                summary += $"\n\n{result.Warnings.Count} warning(s):\n - " + string.Join("\n - ", result.Warnings.Take(20));

            ResultText = summary;
        }
        catch (Exception ex)
        {
            ResultText = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }
}
