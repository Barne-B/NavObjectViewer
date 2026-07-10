using System.Windows;
using NavObjectViewer.Core.Models;
using NavObjectViewer.ViewModels;

namespace NavObjectViewer.Views;

public partial class ExportDialog : Window
{
    public ExportDialog(IReadOnlyList<NavObject> objects)
    {
        InitializeComponent();
        DataContext = new ExportViewModel(objects);
        SourceInitialized += (_, _) => NativeTheme.ApplyTitleBarTheme(this, ThemeManager.Current == ThemeManager.Mode.Dark);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
