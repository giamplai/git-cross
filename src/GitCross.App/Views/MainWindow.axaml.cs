using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GitCross.App.ViewModels;
using GitCross.Core.Git;

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
        _vm.Ask = (title, message, ok, hint) => PromptWindow.AskAsync(this, title, message, ok, true, hint);
        _vm.Inform = async (title, message) => await PromptWindow.AskAsync(this, title, message, "OK");
        _vm.Confirm = async (title, message) => await PromptWindow.AskAsync(this, title, message, "Discard") is not null;

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
                await _vm.RefreshSilentAsync();
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

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (await new SettingsWindow(_vm.Settings).ShowDialog<bool>(this))
        {
            await _vm.ApplySettingsAsync();
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
        else if (_vm.SelectedSidebar?.Submodule is { } sub)
        {
            _ = _vm.OpenSubmoduleAsync(sub);
        }
    }

    private static Core.Models.GitRef? RefOf(object? sender)
        => (sender as Control)?.DataContext is SidebarEntry { Ref: { } r } ? r : null;

    private void OnSidebarCheckout(object? sender, RoutedEventArgs e) => _vm.CheckoutRefCommand.Execute(RefOf(sender));

    private async void OnSidebarMerge(object? sender, RoutedEventArgs e)
    {
        if (RefOf(sender) is { } r) { await _vm.MergeWithAsync(r, MergeMode.Default); }
    }

    private async void OnSidebarMergeNoFf(object? sender, RoutedEventArgs e)
    {
        if (RefOf(sender) is { } r) { await _vm.MergeWithAsync(r, MergeMode.NoFastForward); }
    }

    private async void OnSidebarMergeSquash(object? sender, RoutedEventArgs e)
    {
        if (RefOf(sender) is { } r) { await _vm.MergeWithAsync(r, MergeMode.Squash); }
    }

    private async void OnSidebarRebase(object? sender, RoutedEventArgs e)
    {
        if (RefOf(sender) is { } r
            && await PromptWindow.AskAsync(this, "Rebase", $"Rebase '{_vm.CurrentBranch}' onto '{r.Name}'? This rewrites the commits of the current branch.", "Rebase") is not null)
        {
            await _vm.RebaseOntoAsync(r);
        }
    }

    private void OnSidebarDelete(object? sender, RoutedEventArgs e) => _vm.DeleteBranchCommand.Execute(RefOf(sender));

    private static HistoryItem? ItemOf(object? sender)
        => (sender as Control)?.DataContext is HistoryItem { IsWorkingDir: false, Commit: not null } i ? i : null;

    private async void OnCommitCheckout(object? s, RoutedEventArgs e)
    {
        if (ItemOf(s) is { } i) { await _vm.CheckoutShaAsync(i.Commit!.Sha); }
    }

    private async void OnCommitCherryPick(object? s, RoutedEventArgs e)
    {
        if (ItemOf(s) is { } i) { await _vm.CherryPickAsync(i.Commit!.Sha); }
    }

    private async void OnCommitRevert(object? s, RoutedEventArgs e)
    {
        if (ItemOf(s) is { } i) { await _vm.RevertAsync(i.Commit!.Sha); }
    }

    private async void OnCommitInteractiveRebase(object? s, RoutedEventArgs e)
    {
        if (ItemOf(s) is not { } i)
        {
            return;
        }

        var plan = await _vm.PrepareInteractiveRebaseAsync(i.Commit!.Sha, $"{i.ShortSha} “{i.Subject}”");
        if (plan is null)
        {
            return;
        }

        if (await new RebaseWindow(plan).ShowDialog<IReadOnlyList<Core.Rebase.RebaseTodoItem>?>(this) is { } todo)
        {
            await _vm.InteractiveRebaseAsync(i.Commit!.Sha, todo);
        }
    }

    private async void OnCommitTag(object? s, RoutedEventArgs e)
    {
        if (ItemOf(s) is { } i)
        {
            await ShowTagDialogAsync(i.Commit!.Sha);
        }
    }

    private async void OnTagCreateHead(object? s, RoutedEventArgs e) => await ShowTagDialogAsync("HEAD");

    private async Task ShowTagDialogAsync(string target)
    {
        var dialog = new TagWindow(target, await _vm.GetRemoteNamesAsync(), _vm.ValidateTagAsync);
        if (await dialog.ShowDialog<TagDialogResult?>(this) is { } r)
        {
            await _vm.CreateTagAsync(r.Options, r.PushRemote);
        }
    }

    private async void OnTagPush(object? s, RoutedEventArgs e)
    {
        if (RefOf(s) is { } r) { await _vm.PushTagAsync(r); }
    }

    private async void OnTagDelete(object? s, RoutedEventArgs e)
    {
        if (RefOf(s) is { } r) { await _vm.DeleteTagAsync(r); }
    }

    private async void OnTagDeleteRemote(object? s, RoutedEventArgs e)
    {
        if (RefOf(s) is { } r) { await _vm.DeleteRemoteTagAsync(r); }
    }

    private async void OnCommitCopy(object? s, RoutedEventArgs e)
    {
        if (ItemOf(s) is { } i && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
        {
            await cb.SetTextAsync(i.Commit!.Sha);
        }
    }

    private void OnResetSoft(object? s, RoutedEventArgs e) => _ = Reset(s, ResetMode.Soft);

    private void OnResetMixed(object? s, RoutedEventArgs e) => _ = Reset(s, ResetMode.Mixed);

    private void OnResetHard(object? s, RoutedEventArgs e) => _ = Reset(s, ResetMode.Hard);

    private async Task Reset(object? sender, ResetMode mode)
    {
        if (ItemOf(sender) is not { } i)
        {
            return;
        }

        if (mode == ResetMode.Hard
            && await PromptWindow.AskAsync(this, "Hard reset", $"Move the current branch to {i.ShortSha} and DISCARD all uncommitted changes? This cannot be undone.", "Hard reset") is null)
        {
            return;
        }

        await _vm.ResetToAsync(i.Commit!.Sha, mode);
    }

    private static Core.Submodules.SubmoduleInfo? SubOf(object? sender)
        => (sender as Control)?.DataContext is SidebarEntry { Submodule: { } m } ? m : null;

    private async void OnSubmoduleOpen(object? s, RoutedEventArgs e)
    {
        if (SubOf(s) is { } m) { await _vm.OpenSubmoduleAsync(m); }
    }

    private async void OnSubmoduleUpdate(object? s, RoutedEventArgs e)
    {
        if (SubOf(s) is { } m) { await _vm.UpdateSubmoduleAsync(m); }
    }

    private void OnSubmoduleUpdateAll(object? s, RoutedEventArgs e) => _vm.UpdateAllSubmodulesCommand.Execute(null);

    private async void OnSubmoduleBranchAll(object? s, RoutedEventArgs e) => await _vm.CreateBranchEverywhereAsync();

    private async void OnSubmoduleAlign(object? s, RoutedEventArgs e) => await _vm.AlignSubmodulesAsync();

    private async void OnSubmoduleCommitAll(object? s, RoutedEventArgs e) => await _vm.CommitEverywhereAsync();

    private async void OnSubmodulePullAll(object? s, RoutedEventArgs e) => await _vm.PullEverywhereAsync();

    private async void OnSubmodulePushAll(object? s, RoutedEventArgs e) => await _vm.PushEverywhereAsync();

    private static Core.Models.StashEntry? StashOf(object? sender)
        => (sender as Control)?.DataContext is SidebarEntry { Stash: { } st } ? st : null;

    private async void OnStashApply(object? s, RoutedEventArgs e)
    {
        if (StashOf(s) is { } st) { await _vm.StashApplyAsync(st); }
    }

    private async void OnStashPop(object? s, RoutedEventArgs e)
    {
        if (StashOf(s) is { } st) { await _vm.StashPopEntryAsync(st); }
    }

    private async void OnStashDrop(object? s, RoutedEventArgs e)
    {
        if (StashOf(s) is { } st
            && await PromptWindow.AskAsync(this, "Drop stash", $"Permanently delete {st.Ref} ({st.Message})?", "Drop") is not null)
        {
            await _vm.StashDropAsync(st);
        }
    }

    private void OnDiffSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb)
        {
            _vm.SetDiffSelection(lb.SelectedItems?.OfType<Core.Diff.DiffLine>().ToList() ?? []);
        }
    }

    private static string? PathOf(object? sender) => (sender as Control)?.DataContext is Core.Models.FileChange f ? f.Path : null;

    private async void OnBlameCommitFile(object? s, RoutedEventArgs e)
    {
        if (PathOf(s) is { } p) { await _vm.ShowBlameAsync(p, _vm.DetailSha); }
    }

    private async void OnBlameWorkFile(object? s, RoutedEventArgs e)
    {
        if (PathOf(s) is { } p) { await _vm.ShowBlameAsync(p, null); }
    }

    private async void OnFileHistory(object? s, RoutedEventArgs e)
    {
        if (PathOf(s) is { } p) { await _vm.ShowFileHistoryAsync(p); }
    }

    private async void OnBlameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is BlameRow row)
        {
            await _vm.ShowCommitAsync(row.Line.Sha);
        }
    }

    private static Core.Models.FileChange? FileOf(object? sender) => (sender as Control)?.DataContext as Core.Models.FileChange;

    private async void OnResolveInTool(object? s, RoutedEventArgs e)
    {
        if (FileOf(s) is { } f) { await _vm.ResolveInToolAsync(f); }
    }

    private async void OnDiffUnstagedInTool(object? s, RoutedEventArgs e)
    {
        if (FileOf(s) is { } f) { await _vm.DiffInToolAsync(f, GitRepository.DiffTarget.Unstaged); }
    }

    private async void OnDiffStagedInTool(object? s, RoutedEventArgs e)
    {
        if (FileOf(s) is { } f) { await _vm.DiffInToolAsync(f, GitRepository.DiffTarget.Staged); }
    }

    private async void OnDiffCommitInTool(object? s, RoutedEventArgs e)
    {
        if (FileOf(s) is { } f) { await _vm.DiffInToolAsync(f, GitRepository.DiffTarget.Commit); }
    }

    private async void OnAcceptOurs(object? s, RoutedEventArgs e)
    {
        if (FileOf(s) is { } f) { await _vm.ResolveAsync(f, ours: true); }
    }

    private async void OnAcceptTheirs(object? s, RoutedEventArgs e)
    {
        if (FileOf(s) is { } f) { await _vm.ResolveAsync(f, ours: false); }
    }

    private async void OnOpenFile(object? s, RoutedEventArgs e)
    {
        if (FileOf(s) is { } f && _vm.FullPath(f.Path) is { } full && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchFileInfoAsync(new FileInfo(full));
        }
    }

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
