using GitCross.Core.Git;
using GitCross.Core.Graph;
using GitCross.Core.Models;
using GitCross.Core.Submodules;

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

public sealed record SidebarEntry(string Text, GitRef? Ref = null, bool IsHeader = false, string Count = "", StashEntry? Stash = null, SubmoduleInfo? Submodule = null, SubmoduleLiveStatus? Live = null)
{
    public bool IsSubmodule => Submodule is not null;
    public bool IsSubmoduleHeader => IsHeader && Text == "SUBMODULES";
    public string? Tip => Submodule is { } m ? $"{m.Path}\n{m.Url}\n{m.State}{(m.Describe is null ? "" : "  ·  " + m.Describe)}{(Live is null ? "" : "\nbranch: " + (Live.Branch ?? "detached HEAD") + (Live.Dirty ? "\nuncommitted changes" : ""))}" : null;
    /// <summary>Short live state shown after a submodule name: branch (or "detached"), ahead/behind, uncommitted changes.</summary>
    public string Detail => Live is null
        ? ""
        : $"{Live.Branch ?? "detached"}{(Live.Ahead > 0 ? $" ↑{Live.Ahead}" : "")}{(Live.Behind > 0 ? $" ↓{Live.Behind}" : "")}{(Live.Dirty ? " ●" : "")}";

    public bool IsItem => !IsHeader;
    public bool IsRefItem => Ref is not null;
    public bool IsStash => Stash is not null;
}

public sealed record RecentRepo(string Name, string Path);

/// <summary>A blame line plus presentation hints: metadata is shown only on the first line of a run from one commit.</summary>
public sealed record BlameRow(GitCross.Core.Blame.BlameLine Line, bool ShowMeta, bool Stripe, string When)
{
    public string Author => ShowMeta ? Line.Author : "";
    public string Sha => ShowMeta ? Line.ShortSha : "";
    public string Date => ShowMeta ? When : "";
}
