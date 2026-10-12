using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCross.Core.Models;
using GitCross.Core.Rebase;

namespace GitCross.App.ViewModels;

public partial class RebaseRow(Commit commit) : ObservableObject
{
    public Commit Commit { get; } = commit;
    public string ShortSha => Commit.ShortSha;
    public string Subject => Commit.Subject;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDropped)), NotifyPropertyChangedFor(nameof(IsFolded)), NotifyPropertyChangedFor(nameof(NeedsMessage))]
    private RebaseAction _action = RebaseAction.Pick;

    [ObservableProperty] private string _message = "";

    public bool IsDropped => Action == RebaseAction.Drop;
    public bool IsFolded => Action is RebaseAction.Squash or RebaseAction.Fixup;
    public bool NeedsMessage => Action == RebaseAction.Reword;
}

/// <summary>The editable plan shown by the interactive rebase dialog (oldest commit first = applied first).</summary>
public partial class RebaseViewModel : ObservableObject
{
    public static RebaseAction[] Actions { get; } = Enum.GetValues<RebaseAction>();

    public RebaseViewModel(string ontoLabel, IEnumerable<Commit> oldestFirst, Func<string, Task<string>> loadMessage)
    {
        OntoLabel = ontoLabel;
        _loadMessage = loadMessage;
        foreach (var c in oldestFirst)
        {
            var row = new RebaseRow(c);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RebaseRow.Action))
                {
                    _ = OnActionChangedAsync(row);
                }

                Revalidate();
            };
            Rows.Add(row);
        }

        Revalidate();
    }

    private readonly Func<string, Task<string>> _loadMessage;

    public string OntoLabel { get; }
    public ObservableCollection<RebaseRow> Rows { get; } = [];

    [ObservableProperty] private RebaseRow? _selected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProblem)), NotifyPropertyChangedFor(nameof(CanStart))] private string? _problem;

    public bool HasProblem => Problem is not null;
    public bool CanStart => Problem is null;

    public int KeptCount => Rows.Count(r => !r.IsDropped);

    /// <summary>Prefill the editor with the commit's current message the first time Reword is chosen.</summary>
    private async Task OnActionChangedAsync(RebaseRow row)
    {
        if (row.Action == RebaseAction.Reword && row.Message.Length == 0)
        {
            var current = (await _loadMessage(row.Commit.Sha)).TrimEnd();
            // The user may have typed while the message was loading: never overwrite their text.
            if (row.Message.Length == 0 && row.Action == RebaseAction.Reword)
            {
                row.Message = current;
            }
        }
    }

    [RelayCommand]
    private void MoveUp(RebaseRow? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(RebaseRow? row) => Move(row, 1);

    private void Move(RebaseRow? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        var i = Rows.IndexOf(row);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Rows.Count)
        {
            return;
        }

        Rows.Move(i, j);
        Selected = row;
        Revalidate();
    }

    private void Revalidate()
    {
        Problem = InteractiveRebasePlanner.Validate(ToTodo());
        OnPropertyChanged(nameof(KeptCount));
    }

    public IReadOnlyList<RebaseTodoItem> ToTodo()
        => Rows.Select(r => new RebaseTodoItem(r.Action, r.Commit.Sha, r.Commit.Subject, r.Action == RebaseAction.Reword ? r.Message : null)).ToList();
}
