using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GitCross.Core.Git;
using GitCross.Core.Settings;

namespace GitCross.App.Views;

/// <summary>Edits <see cref="AppSettings"/> and the user's global git identity. Closes with true when saved.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow() : this(new AppSettings())
    {
    }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        VersionText.Text = $"{AppInfo.Name} {AppInfo.Version}";
        GitPathBox.Text = settings.GitExecutable;
        PullRebaseBox.IsChecked = settings.PullRebase;
        MaxCommitsBox.ItemsSource = AppSettings.MaxCommitsChoices;
        MaxCommitsBox.SelectedItem = AppSettings.MaxCommitsChoices.Contains(settings.MaxCommits) ? settings.MaxCommits : 2000;
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                Close(false);
            }
        };
        Opened += async (_, _) =>
        {
            NameBox.Text = await GitEnvironment.GetGlobalAsync("user.name");
            EmailBox.Text = await GitEnvironment.GetGlobalAsync("user.email");
            await TestGitAsync();
        };
    }

    private async Task TestGitAsync()
    {
        var version = await GitEnvironment.ProbeAsync(GitPathBox.Text);
        GitStatusText.Text = version is null ? "✗ This executable could not be started." : $"✓ {version}";
        GitStatusText.Foreground = version is null ? Brushes.IndianRed : Brushes.SeaGreen;
    }

    private async void OnTestGit(object? sender, RoutedEventArgs e) => await TestGitAsync();

    private async void OnBrowseGit(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select the git executable", AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } p)
        {
            GitPathBox.Text = p;
            await TestGitAsync();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var path = string.IsNullOrWhiteSpace(GitPathBox.Text) ? null : GitPathBox.Text.Trim();
        if (await GitEnvironment.ProbeAsync(path) is null)
        {
            ErrorText.Text = "The git executable cannot be started. Fix the path (or clear it) before saving.";
            ErrorText.IsVisible = true;
            return;
        }

        try
        {
            await GitEnvironment.SetGlobalAsync("user.name", NameBox.Text ?? "");
            await GitEnvironment.SetGlobalAsync("user.email", EmailBox.Text ?? "");
        }
        catch (GitException ex)
        {
            ErrorText.Text = "Could not write the global git config: " + ex.Message;
            ErrorText.IsVisible = true;
            return;
        }

        _settings.GitExecutable = path;
        _settings.PullRebase = PullRebaseBox.IsChecked == true;
        _settings.MaxCommits = MaxCommitsBox.SelectedItem is int n ? n : 2000;
        Close(true);
    }
}
