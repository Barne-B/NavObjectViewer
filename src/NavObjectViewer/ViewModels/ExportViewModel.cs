using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;

namespace NavObjectViewer.ViewModels;

public enum ExportMode { Combined, PerObject }

public partial class ExportViewModel : ObservableObject
{
    private readonly ObjectExporter _exporter = new();
    private readonly IReadOnlyList<NavObject> _objects;

    public ExportViewModel(IReadOnlyList<NavObject> objects)
    {
        _objects = objects;
        _objectCount = objects.Count;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCombined))]
    private ExportMode _mode = ExportMode.Combined;

    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private bool _groupByType = true;
    [ObservableProperty] private int _objectCount;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    public bool IsCombined => Mode == ExportMode.Combined;

    partial void OnModeChanged(ExportMode value) => OutputPath = string.Empty;

    [RelayCommand]
    private void Browse()
    {
        if (IsCombined)
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
            ExportResult result = await Task.Run(() => mode == ExportMode.Combined
                ? _exporter.ExportCombined(_objects, path, progress)
                : _exporter.ExportPerObject(_objects, path, group, progress));

            string summary = mode == ExportMode.Combined
                ? $"Wrote {result.ObjectCount:N0} objects to:\n{result.OutputPath}"
                : $"Wrote {result.FileCount:N0} files ({result.ObjectCount:N0} objects) under:\n{result.OutputPath}";

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
