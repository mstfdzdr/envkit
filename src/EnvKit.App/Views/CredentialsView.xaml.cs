using System.Windows;
using System.Windows.Controls;
using EnvKit.App.ViewModels;
using Microsoft.Win32;

namespace EnvKit.App.Views;

public partial class CredentialsView : UserControl
{
    public CredentialsView() => InitializeComponent();

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export credential inventory (no secrets)",
            FileName = "credentials.json",
            Filter = "JSON (*.json)|*.json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ((CredentialsViewModel)DataContext).ExportTo(dialog.FileName);
        }
    }
}
