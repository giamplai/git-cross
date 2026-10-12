using GitCross.Core.Graph;
using GitCross.Core.Models;

namespace GitCross.App.ViewModels;

public sealed record RefLabel(string Text, RefKind Kind, bool IsCurrent);

/// <summary>One line of the history list: a commit, or the synthetic "uncommitted changes" row.</summary>
public sealed class HistoryItem
{
    public Commit? Commit { get; init; }
    public GraphRow? Row { get; init; }
    public bool IsWorkingDir { get; init; }
    public string Subject { get; init; } = "";
    public string Author { get; init; } = "";
    public string Initials { get; init; } = "";
    public string When { get; init; } = "";
    public string FullDate { get; init; } = "";
    public string ShortSha { get; init; } = "";
    public IReadOnlyList<RefLabel> Labels { get; init; } = [];
    public double GraphWidth { get; init; }
}

public sealed record SidebarEntry(string Text, GitRef? Ref = null, bool IsHeader = false, string Count = "", StashEntry? Stash = null)
{
    public bool IsItem => !IsHeader;
    public bool IsRefItem => Ref is not null;
    public bool IsStash => Stash is not null;
}

public sealed record RecentRepo(string Name, string Path);
