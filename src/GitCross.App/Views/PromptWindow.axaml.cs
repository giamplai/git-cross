using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GitCross.App.Views;

/// <summary>Small modal for confirmations and one-line input. Returns the text (or "" for a confirmation), null when cancelled.</summary>
public partial class PromptWindow : Window
{
    private readonly bool _needsInput;

    public PromptWindow() : this("Confirm", "", "OK", false)
    {
    }

    public PromptWindow(string title, string message, string okText, bool needsInput, string? watermark = null)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        Message.IsVisible = message.Length > 0;
        Ok.Content = okText;
        _needsInput = needsInput;
        Input.IsVisible = needsInput;
        Input.PlaceholderText = watermark;
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                Close(null);
            }
        };
        Opened += (_, _) => (needsInput ? Input : (Control)Ok).Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var text = Input.Text?.Trim() ?? "";
        if (_needsInput && text.Length == 0)
        {
            return;
        }

        Close(text);
    }

    public static Task<string?> AskAsync(Window owner, string title, string message, string okText, bool needsInput = false, string? watermark = null)
        => new PromptWindow(title, message, okText, needsInput, watermark).ShowDialog<string?>(owner);
}
