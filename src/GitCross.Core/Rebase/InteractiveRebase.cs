using System.Text;
using GitCross.Core.Models;

namespace GitCross.Core.Rebase;

public enum RebaseAction { Pick, Reword, Squash, Fixup, Drop }

/// <summary>One line of the rebase todo list. <see cref="NewMessage"/> replaces the commit message (for Reword, or the result of a squash group).</summary>
public sealed record RebaseTodoItem(RebaseAction Action, string Sha, string Subject, string? NewMessage = null);

/// <summary>Builds the todo script and validates a plan before it is handed to <c>git rebase -i</c>.</summary>
public static class InteractiveRebasePlanner
{
    /// <summary>Null when the plan can run, otherwise a message for the user.</summary>
    public static string? Validate(IReadOnlyList<RebaseTodoItem> todo)
    {
        var kept = todo.Where(t => t.Action != RebaseAction.Drop).ToList();
        if (kept.Count == 0)
        {
            return "Nothing would be left: at least one commit must be kept (use Abort instead to cancel).";
        }

        if (kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup)
        {
            return "The first commit cannot be squashed or fixed up: there is no earlier commit to merge it into.";
        }

        var reword = todo.FirstOrDefault(t => t.Action == RebaseAction.Reword && string.IsNullOrWhiteSpace(t.NewMessage));
        return reword is null ? null : $"Enter a message for the reworded commit {reword.Sha[..Math.Min(8, reword.Sha.Length)]}.";
    }

    /// <summary>
    /// Todo file text. Messages are written to files in <paramref name="messageDir"/> and applied with
    /// <c>exec git commit --amend -F</c> after the commit (or its squash group) so no editor is ever needed.
    /// </summary>
    public static string BuildTodo(IReadOnlyList<RebaseTodoItem> todo, string messageDir)
    {
        var sb = new StringBuilder();
        var kept = todo.Where(t => t.Action != RebaseAction.Drop).ToList();
        for (var i = 0; i < kept.Count; i++)
        {
            var item = kept[i];
            if (item.Action is RebaseAction.Pick or RebaseAction.Reword)
            {
                sb.Append("pick ").Append(item.Sha).Append(' ').AppendLine(OneLine(item.Subject));
                // squash/fixup items directly following belong to this group
                var last = i;
                while (last + 1 < kept.Count && kept[last + 1].Action is RebaseAction.Squash or RebaseAction.Fixup)
                {
                    last++;
                    var s = kept[last];
                    sb.Append(s.Action == RebaseAction.Squash ? "squash " : "fixup ").Append(s.Sha).Append(' ').AppendLine(OneLine(s.Subject));
                }

                if (!string.IsNullOrWhiteSpace(item.NewMessage))
                {
                    var file = Path.Combine(messageDir, $"msg-{item.Sha[..Math.Min(12, item.Sha.Length)]}.txt");
                    File.WriteAllText(file, item.NewMessage.TrimEnd() + "\n");
                    sb.Append("exec git commit --amend --allow-empty -F ").AppendLine(ShellQuote(file));
                }

                i = last;
            }
            else
            {
                // A squash/fixup with no group head (only possible if validation was skipped): keep git's behavior.
                sb.Append(item.Action == RebaseAction.Squash ? "squash " : "fixup ").Append(item.Sha).Append(' ').AppendLine(OneLine(item.Subject));
            }
        }

        return sb.ToString();
    }

    private static string OneLine(string s) => s.ReplaceLineEndings(" ");

    /// <summary>POSIX single-quote escaping; git runs editors and exec lines through sh on every OS (also Git for Windows).</summary>
    public static string ShellQuote(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''") + "'";
}
