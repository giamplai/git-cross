using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitCross.App.ViewModels;
using GitCross.Core.Rebase;

namespace GitCross.App.Views;

/// <summary>Edits an interactive rebase plan. Closes with the todo list, or null when cancelled.</summary>
public partial class RebaseWindow : Window
{
    public RebaseWindow()
    {
        InitializeComponent();
    }

    public RebaseWindow(RebaseViewModel vm) : this()
    {
        DataContext = vm;
        vm.Selected ??= vm.Rows.FirstOrDefault();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnStart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RebaseViewModel { CanStart: true } vm)
        {
            Close(vm.ToTodo());
        }
    }
}
