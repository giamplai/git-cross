using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GitCross.App.ViewModels;

namespace GitCross.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow() : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _vm.ApplySavedTheme();
        DataContext = _vm;

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new FocusSearchCommand(SearchBox) });

        Opened += async (_, _) =>
        {
            // `GitCross.App <path>` wins; otherwise reopen the last repository.
            var args = Environment.GetCommandLineArgs();
            var path = args.Length > 1 ? args[1] : _vm.InitialRepoPath;
            if (path is not null && Directory.Exists(path))
            {
                await _vm.OpenAsync(path);
            }
        };

        // Pick up changes made outside the app (terminal, editor) when the user comes back.
        Activated += async (_, _) =>
        {
            if (_vm.HasRepo && !_vm.IsBusy)
            {
                await _vm.RefreshAsync();
            }
        };
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync("Open repository") is { } path)
        {
            await _vm.OpenAsync(path);
        }
    }

    private async void OnInitClick(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync("Choose a folder to initialize") is { } path)
        {
            await _vm.InitAsync(path);
        }
    }

    private async void OnCloneClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new CloneWindow(_vm.LastCloneDirectory);
        var result = await dialog.ShowDialog<string?>(this);
        if (result is not null)
        {
            _vm.LastCloneDirectory = Path.GetDirectoryName(result);
            await _vm.OpenAsync(result);
        }
    }

    private void OnSidebarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.SelectedSidebar?.Ref is { } r)
        {
            _vm.CheckoutRefCommand.Execute(r);
        }
    }

    private static Core.Models.GitRef? RefOf(object? sender)
        => (sender as Control)?.DataContext is SidebarEntry { Ref: { } r } ? r : null;

    private void OnSidebarCheckout(object? sender, RoutedEventArgs e) => _vm.CheckoutRefCommand.Execute(RefOf(sender));

    private void OnSidebarMerge(object? sender, RoutedEventArgs e) => _vm.MergeRefCommand.Execute(RefOf(sender));

    private void OnSidebarDelete(object? sender, RoutedEventArgs e) => _vm.DeleteBranchCommand.Execute(RefOf(sender));

    private sealed class FocusSearchCommand(Control target) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            target.Focus();
            (target as TextBox)?.SelectAll();
        }
    }
}
