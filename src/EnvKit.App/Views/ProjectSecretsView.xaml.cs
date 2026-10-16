using System.Windows;
using System.Windows.Controls;
using EnvKit.App.ViewModels;
using Microsoft.Win32;

namespace EnvKit.App.Views;

public partial class ProjectSecretsView : UserControl
{
    public ProjectSecretsView() => InitializeComponent();

    private ProjectSecretsViewModel ViewModel => (ProjectSecretsViewModel)DataContext;

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder to scan for .env files and user secrets" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ViewModel.Folder = dialog.FolderName;
            ViewModel.ScanCommand.Execute(null);
        }
    }
}
