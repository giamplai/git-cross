using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GitCross.Core.Git;
using GitCross.Core.Settings;
using GitCross.Core.Tools;

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
        VersionText.Text = $"{AppInfo.Name} {AppInfo.Version}  ·  MIT License";
        CreditText.Text = AppInfo.Credit;
        GitPathBox.Text = settings.GitExecutable;
        PullRebaseBox.IsChecked = settings.PullRebase;
        MaxCommitsBox.ItemsSource = AppSettings.MaxCommitsChoices;
        MaxCommitsBox.SelectedItem = AppSettings.MaxCommitsChoices.Contains(settings.MaxCommits) ? settings.MaxCommits : 2000;
        MergeBox.Text = settings.MergeTool;
        DiffBox.Text = settings.DiffTool;
        var presetNames = new List<string> { "Custom" };
        presetNames.AddRange(ExternalTool.Presets.Select(p => p.Name));
        PresetBox.ItemsSource = presetNames;
        PresetBox.SelectedIndex = Math.Max(0, ExternalTool.Presets.ToList().FindIndex(p => p.Merge == settings.MergeTool && p.Diff == settings.DiffTool) + 1);
        PresetBox.SelectionChanged += (_, _) =>
        {
            if (PresetBox.SelectedIndex > 0)
            {
                var preset = ExternalTool.Presets[PresetBox.SelectedIndex - 1];
                MergeBox.Text = preset.Merge;
                DiffBox.Text = preset.Diff;
            }
        };
        MergeBox.TextChanged += (_, _) => ShowToolStatus(MergeBox.Text, MergeStatus, "merge");
        DiffBox.TextChanged += (_, _) => ShowToolStatus(DiffBox.Text, DiffStatus, "diff");
        ShowToolStatus(MergeBox.Text, MergeStatus, "merge");
        ShowToolStatus(DiffBox.Text, DiffStatus, "diff");
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

    private static void ShowToolStatus(string? template, TextBlock target, string kind)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            var found = ExternalTool.LocateCollaterale();
            target.Text = found is null ? $"No {kind} tool: only the built-in resolution is used." : $"No {kind} tool set. Collaterale was found on this computer: pick the Collaterale preset to use it.";
            ToolTip.SetTip(target, found);
            target.Foreground = Brushes.Gray;
            return;
        }

        var exe = ExternalTool.ResolveExecutable(template);
        var unknown = CommandTemplate.UnknownVariables(template, [CommandTemplate.Base, CommandTemplate.Local, CommandTemplate.Remote, CommandTemplate.Merged]);
        if (unknown.Count > 0)
        {
            target.Text = $"✗ Unknown variable {string.Join(", ", unknown)} (use $BASE, $LOCAL, $REMOTE, $MERGED).";
            target.Foreground = Brushes.IndianRed;
        }
        else if (exe is null)
        {
            target.Text = $"✗ “{CommandTemplate.Split(template).FirstOrDefault()}” was not found on your PATH.";
            target.Foreground = Brushes.IndianRed;
        }
        else
        {
            target.Text = $"✓ Found {Path.GetFileName(exe)}";
            ToolTip.SetTip(target, exe);
            target.Foreground = Brushes.SeaGreen;
        }
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

        var known = new[] { CommandTemplate.Base, CommandTemplate.Local, CommandTemplate.Remote, CommandTemplate.Merged };
        foreach (var t in new[] { MergeBox.Text, DiffBox.Text })
        {
            if (!string.IsNullOrWhiteSpace(t) && CommandTemplate.UnknownVariables(t, known) is { Count: > 0 } bad)
            {
                ErrorText.Text = $"The tool command uses an unknown variable ({string.Join(", ", bad)}). Fix it before saving.";
                ErrorText.IsVisible = true;
                return;
            }
        }

        _settings.MergeTool = string.IsNullOrWhiteSpace(MergeBox.Text) ? null : MergeBox.Text.Trim();
        _settings.DiffTool = string.IsNullOrWhiteSpace(DiffBox.Text) ? null : DiffBox.Text.Trim();
        _settings.GitExecutable = path;
        _settings.PullRebase = PullRebaseBox.IsChecked == true;
        _settings.MaxCommits = MaxCommitsBox.SelectedItem is int n ? n : 2000;
        Close(true);
    }
}
