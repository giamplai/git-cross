using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GitCross.Core.Git;

namespace GitCross.App.Views;

public partial class CloneWindow : Window
{
    public CloneWindow() : this(null)
    {
    }

    public CloneWindow(string? lastDirectory)
    {
        InitializeComponent();
        ParentBox.Text = lastDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                Close(null);
            }
        };
        UrlBox.TextChanged += (_, _) => UpdateTarget();
        ParentBox.TextChanged += (_, _) => UpdateTarget();
        UpdateTarget();
    }

    private string Target => Path.Combine(ParentBox.Text ?? "", GitRepository.SuggestFolderName(UrlBox.Text ?? ""));

    private void UpdateTarget()
        => TargetText.Text = string.IsNullOrWhiteSpace(UrlBox.Text) ? "" : $"Will clone into {Target}";

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Destination folder" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } p)
        {
            ParentBox.Text = p;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnClone(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text))
        {
            ShowError("Enter a repository URL.");
            return;
        }

        var target = Target;
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            ShowError($"'{target}' already exists and is not empty.");
            return;
        }

        ErrorText.IsVisible = false;
        Progress.IsVisible = true;
        CloneButton.IsEnabled = false;
        try
        {
            var repo = await GitRepository.CloneAsync(UrlBox.Text.Trim(), target);
            Close(repo.Root);
        }
        catch (GitException ex)
        {
            ShowError(ex.Message);
            Progress.IsVisible = false;
            CloneButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
