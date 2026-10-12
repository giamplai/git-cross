namespace GitCross.Core.Models;

public enum RefKind { LocalBranch, RemoteBranch, Tag, Stash, Head }

public sealed record GitRef(string Name, string FullName, RefKind Kind, string Sha, bool IsCurrent = false);

public sealed record Commit(
    string Sha,
    IReadOnlyList<string> Parents,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Subject)
{
    public string ShortSha => Sha.Length > 8 ? Sha[..8] : Sha;
}

public enum ChangeKind { Added, Modified, Deleted, Renamed, Copied, Untracked, Conflicted, TypeChanged }

public sealed record FileChange(string Path, ChangeKind Kind, string? OldPath = null);

public sealed record WorkingStatus(
    string? Branch,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Staged,
    IReadOnlyList<FileChange> Unstaged);
