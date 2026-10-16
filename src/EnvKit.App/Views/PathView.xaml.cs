using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EnvKit.App.ViewModels;
using Microsoft.Win32;

namespace EnvKit.App.Views;

public partial class PathView : UserControl
{
    private Point _dragStart;
    private PathEntryItem? _dragged;

    public PathView() => InitializeComponent();

    private PathViewModel ViewModel => (PathViewModel)DataContext;

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Localization.Loc.Get("Path_BrowseTitle") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ViewModel.NewEntry = dialog.FolderName;
        }
    }

    private void OnEntryMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(EntryList);
        _dragged = ItemAt(e.OriginalSource);
    }

    private void OnEntryMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragged is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var offset = e.GetPosition(EntryList) - _dragStart;
        if (Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragged;
        _dragged = null;
        DragDrop.DoDragDrop(EntryList, item, DragDropEffects.Move);
    }

    private void OnEntryDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PathEntryItem)) is PathEntryItem source)
        {
            var target = ItemAt(e.OriginalSource);
            ViewModel.Move(source.Index, target?.Index ?? ViewModel.Entries.Count - 1);
        }
    }

    private static PathEntryItem? ItemAt(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListBoxItem { DataContext: PathEntryItem item })
            {
                return item;
            }
        }

        return null;
    }
}
