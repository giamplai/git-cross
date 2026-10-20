using Avalonia.Controls;
using Avalonia.Interactivity;
using GitCross.Core.Git;

namespace GitCross.App.Views;

/// <summary>What the user asked for in the tag dialog: the tag, and the remote to push it to (null = do not push).</summary>
public sealed record TagDialogResult(TagOptions Options, string? PushRemote);

public partial class TagWindow : Window
{
    private readonly Func<TagOptions, Task<string?>>? _validate;
    private readonly string? _remote;

    public TagWindow() : this("", [], null)
    {
    }

    /// <param name="target">Commit or reference to pre-fill.</param>
    /// <param name="remotes">Remotes of the repository; the first (origin when present) is offered for pushing.</param>
    /// <param name="validate">Returns why the options cannot be used (shown inline), or null.</param>
    public TagWindow(string target, IReadOnlyList<string> remotes, Func<TagOptions, Task<string?>>? validate)
    {
        InitializeComponent();
        _validate = validate;
        _remote = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
        TargetBox.Text = target;
        PushBox.IsVisible = _remote is not null;
        PushBox.Content = $"Push to '{_remote}' after creating";
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                Close(null);
            }
        };
        Opened += (_, _) => NameBox.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnCreate(object? sender, RoutedEventArgs e)
    {
        var o = new TagOptions(
            NameBox.Text?.Trim() ?? "",
            string.IsNullOrWhiteSpace(TargetBox.Text) ? null : TargetBox.Text.Trim(),
            string.IsNullOrWhiteSpace(MessageBox.Text) ? null : MessageBox.Text.Trim(),
            SignBox.IsChecked == true,
            ForceBox.IsChecked == true);
        if (_validate is not null && await _validate(o) is { } problem)
        {
            ErrorText.Text = problem;
            ErrorText.IsVisible = true;
            return;
        }

        Close(new TagDialogResult(o, PushBox.IsChecked == true ? _remote : null));
    }
}
