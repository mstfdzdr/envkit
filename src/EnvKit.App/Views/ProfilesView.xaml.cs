using System.Windows;
using System.Windows.Controls;
using EnvKit.App.ViewModels;

namespace EnvKit.App.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView() => InitializeComponent();

    private void OnCopyRunCommand(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(((ProfilesViewModel)DataContext).RunCommandText);
}
