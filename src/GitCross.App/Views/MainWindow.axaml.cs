using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GitCross.App.ViewModels;

namespace GitCross.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Opened += async (_, _) =>
        {
            // Allow `GitCross.App <path>`; otherwise try the current directory.
            var args = Environment.GetCommandLineArgs();
            await _vm.OpenAsync(args.Length > 1 ? args[1] : Environment.CurrentDirectory);
        };
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open repository", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            await _vm.OpenAsync(path);
        }
    }

    private async void OnRecentChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string path)
        {
            await _vm.OpenAsync(path);
        }
    }
}
