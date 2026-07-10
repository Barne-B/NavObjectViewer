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
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
