using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitCross.App.ViewModels;
using GitCross.App.Views;
using GitCross.Core.Settings;

namespace GitCross.App.UiTests;

public class WindowRenderTests
{
    private static string OutDir
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("GITCROSS_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "git-cross-shots");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static string MakeRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gxc-ui-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        void G(string args)
        {
            var psi = new ProcessStartInfo("git", args) { WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
        }

        G("init -q -b main"); G("config user.name Ada Lovelace"); G("config user.email ada@example.com");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "one\ntwo\nthree\n");
        G("add ."); G("commit -qm \"Initial commit\"");
        G("checkout -qb feature/login");
        File.WriteAllText(Path.Combine(dir, "login.cs"), "class Login {}\n");
        G("add ."); G("commit -qm \"Add login screen\"");
        G("checkout -q main");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "one\nTWO\nthree\nfour\n");
        G("commit -qam \"Tweak a.txt on main\"");
        G("merge -q --no-ff feature/login -m \"Merge feature/login\""); G("tag v1.0");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "one\nTWO\nthree\nfour\nfive\n");
        File.WriteAllText(Path.Combine(dir, "new.txt"), "hello\n");
        return dir;
    }

    private static void Shot(Window w, string name)
    {
        using var fs = File.Create(Path.Combine(OutDir, name));
        w.CaptureRenderedFrame()!.Save(fs, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task Welcome_screen_renders()
    {
        var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "gxc-nosettings-" + Guid.NewGuid() + ".json"));
        var w = new MainWindow(new MainViewModel(settings));
        w.Show();
        await Settle();
        Shot(w, "welcome.png");
        Assert.False(((MainViewModel)w.DataContext!).HasRepo);
    }

    [AvaloniaFact]
    public async Task Repository_view_renders_history_and_commit_tab()
    {
        var repo = MakeRepo();
        var settings = new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json"));
        var vm = new MainViewModel(settings);
        var w = new MainWindow(vm);
        w.Show();
        Assert.True(await vm.OpenAsync(repo));
        await Settle();

        Assert.Equal("main", vm.BranchText);
        Assert.True(vm.Items[0].IsWorkingDir);
        Assert.Contains(vm.Items, i => i.Labels.Any(l => l.Text == "v1.0"));
        vm.SelectedItem = vm.Items[1];
        await Settle();
        Assert.NotEmpty(vm.CommitFiles);
        Assert.Contains("commits", vm.StatusText);
        Assert.False(vm.IsError);
        Shot(w, "history.png");

        vm.SelectedItem = vm.Items[0];
        await Settle();
        vm.SelectedUnstaged = vm.Unstaged.First(f => f.Path == "a.txt");
        await Settle();
        Assert.NotEmpty(vm.Diff);
        Shot(w, "commit-tab.png");

        vm.SearchText = "login";
        await Settle();
        Shot(w, "search.png");
        vm.SearchText = "";
        await Settle();

        vm.CycleThemeCommand.Execute(null); // System -> Light
        vm.CycleThemeCommand.Execute(null); // Light -> Dark
        await Settle();
        Shot(w, "commit-dark.png");
        vm.SelectedItem = vm.Items[1];
        await Settle();
        Shot(w, "history-dark.png");

        vm.SearchText = "login";
        await Settle();
        Assert.All(vm.Items, i => Assert.Contains("login", i.Subject + string.Join(' ', i.Labels.Select(l => l.Text)), StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task History_actions_update_the_list()
    {
        var repo = MakeRepo();
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        Assert.True(await vm.OpenAsync(repo));
        var tip = vm.Items.First(i => !i.IsWorkingDir);

        await vm.TagAsync(tip.Commit!.Sha, "rc1");
        Assert.Contains(vm.Items, i => i.Labels.Any(l => l.Text == "rc1"));

        var before = vm.Items.Count;
        await vm.ResetToAsync(vm.Items.Last().Commit!.Sha, Core.Git.ResetMode.Mixed);
        Assert.True(vm.Items.Count(i => !i.IsWorkingDir) < before);
    }

    [AvaloniaFact]
    public async Task Hunk_staging_moves_one_hunk_to_the_index()
    {
        var repo = MakeRepo();
        var f = Path.Combine(repo, "big.txt");
        File.WriteAllText(f, string.Join("\n", Enumerable.Range(1, 30).Select(i => $"line{i}")) + "\n");
        Git(repo, "add big.txt"); Git(repo, "commit -qm big");
        File.WriteAllText(f, string.Join("\n", Enumerable.Range(1, 30).Select(i => i is 2 or 28 ? $"CHANGED{i}" : $"line{i}")) + "\n");

        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        var w = new MainWindow(vm);
        w.Show();
        await vm.OpenAsync(repo);
        vm.SelectedItem = vm.Items[0];
        await Settle();
        vm.SelectedUnstaged = vm.Unstaged.First(x => x.Path == "big.txt");
        await Settle();
        Assert.True(vm.HunkActionsEnabled);
        Shot(w, "hunks.png");

        vm.HunkPrimaryCommand.Execute(1);
        await Settle();
        Assert.Contains(vm.Staged, x => x.Path == "big.txt");
        Assert.Contains(vm.Unstaged, x => x.Path == "big.txt"); // the other hunk is still unstaged
        Assert.Equal(1, vm.Diff.Count(l => l.IsHunk)); // diff reloaded with the remaining hunk
    }

    [AvaloniaFact]
    public async Task Stash_appears_in_sidebar_with_files_and_can_be_dropped()
    {
        var repo = MakeRepo();
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        var w = new MainWindow(vm);
        w.Show();
        await vm.OpenAsync(repo);
        vm.StashCommand.Execute(null);
        await Settle();

        var entry = vm.Sidebar.Single(e => e.IsStash);
        Assert.DoesNotContain(vm.Items, i => i.IsWorkingDir); // working tree is clean now
        vm.SelectedSidebar = entry;
        await Settle();
        Assert.Contains(vm.CommitFiles, f => f.Path == "a.txt");
        Shot(w, "stash.png");

        await vm.StashDropAsync(entry.Stash!);
        Assert.DoesNotContain(vm.Sidebar, e => e.IsStash);
    }

    [AvaloniaFact]
    public async Task Blame_tab_and_file_history_work()
    {
        var repo = MakeRepo();
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        var w = new MainWindow(vm);
        w.Show();
        await vm.OpenAsync(repo);

        await vm.ShowBlameAsync("a.txt", null);
        await Settle();
        Assert.Equal(2, vm.SelectedTab);
        Assert.NotEmpty(vm.BlameRows);
        Assert.True(vm.BlameRows[0].ShowMeta);
        Shot(w, "blame.png");

        await vm.ShowFileHistoryAsync("login.cs");
        await Settle();
        Assert.True(vm.HasFileFilter);
        Assert.Equal(["Add login screen"], vm.Items.Select(i => i.Subject));
        Shot(w, "file-history.png");

        await vm.ShowCommitAsync(vm.Items[0].Commit!.Sha);
        Assert.False(vm.HasFileFilter);
        Assert.Equal("Add login screen", vm.SelectedItem?.Subject);
    }

    [AvaloniaFact]
    public async Task Conflicting_merge_shows_banner_and_can_be_resolved()
    {
        var repo = Path.Combine(Path.GetTempPath(), "gxc-ui-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(repo);
        Git(repo, "init -q -b main"); Git(repo, "config user.name Ada"); Git(repo, "config user.email a@x");
        File.WriteAllText(Path.Combine(repo, "f.txt"), "base\n"); Git(repo, "add ."); Git(repo, "commit -qm base");
        Git(repo, "checkout -qb topic"); File.WriteAllText(Path.Combine(repo, "f.txt"), "topic\n"); Git(repo, "commit -qam topic");
        Git(repo, "checkout -q main"); File.WriteAllText(Path.Combine(repo, "f.txt"), "main\n"); Git(repo, "commit -qam main");

        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        var w = new MainWindow(vm);
        w.Show();
        await vm.OpenAsync(repo);

        await vm.MergeRefCommand.ExecuteAsync(vm.Sidebar.First(e => e.Ref?.Name == "topic").Ref);
        await Settle();
        Assert.True(vm.HasOperation);
        Assert.Equal(1, vm.ConflictCount);
        Assert.False(vm.CanContinue);
        Assert.Contains("stopped", vm.StatusText);

        vm.SelectedUnstaged = vm.Unstaged.Single();
        await Settle();
        Assert.Contains(vm.Diff, l => l.Kind == Core.Diff.DiffLineKind.Added && l.Text == "topic");
        Shot(w, "conflict.png");

        await vm.ResolveAsync(vm.Unstaged.Single(), ours: false);
        await Settle();
        Assert.True(vm.CanContinue);
        await vm.ContinueOperationCommand.ExecuteAsync(null);
        await Settle();
        Assert.False(vm.HasOperation);
        Assert.Contains(vm.Items, i => i.Subject.StartsWith("Merge"));
    }

    /// <summary>Generates N linear commits (with a side branch every 50) via fast-import — much faster than N `git commit`.</summary>
    private static string BigRepo(int commits)
    {
        var repo = NewRepo();
        var sb = new System.Text.StringBuilder();
        for (var i = 1; i <= commits; i++)
        {
            sb.Append("commit refs/heads/main\ncommitter Ada <a@x> ").Append(1_700_000_000 + i).Append(" +0000\ndata ").Append($"c{i}".Length).Append('\n').Append($"c{i}\n");
            if (i == 1) { sb.Append("from refs/heads/main^0\n"); }
            sb.Append("M 100644 inline f.txt\ndata ").Append($"v{i}\n".Length).Append('\n').Append($"v{i}\n\n");
        }

        var psi = new ProcessStartInfo("git", "fast-import --quiet") { WorkingDirectory = repo, RedirectStandardInput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(sb.ToString());
        p.StandardInput.Close();
        p.WaitForExit();
        Git(repo, "reset -q --hard main");
        return repo;
    }

    [AvaloniaFact]
    public async Task Large_history_opens_in_reasonable_time()
    {
        var repo = BigRepo(20000);
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        vm.Settings.MaxCommits = 10000;
        var w = new MainWindow(vm);
        w.Show();
        var sw = Stopwatch.StartNew();
        await vm.OpenAsync(repo);
        var open = sw.ElapsedMilliseconds;
        await Settle();
        sw.Restart();
        vm.SearchText = "c19999";
        var search = sw.ElapsedMilliseconds;
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "gxc-perf.txt"), $"open={open}ms search={search}ms items={vm.Items.Count}\n");

        Assert.Single(vm.Items);
        Assert.True(open < 15_000, $"opening 10k commits took {open} ms");
        Assert.True(search < 3_000, $"filtering took {search} ms");
    }

    [AvaloniaFact]
    public async Task Inline_highlight_and_split_view_render()
    {
        var repo = NewRepo();
        var f = Path.Combine(repo, "cart.js");
        File.WriteAllText(f, "function total(items) {\n  let total = price * 2;\n  const tax = 0.2;\n  return total + tax;\n}\n");
        Git(repo, "add ."); Git(repo, "commit -qm cart");
        File.WriteAllText(f, "function total(items) {\n  let total = price * 3;\n  const tax = 0.22;\n  const discount = 5;\n  return total + tax - discount;\n}\n");

        var settingsPath = Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json");
        var vm = new MainViewModel(new SettingsStore(settingsPath));
        var w = new MainWindow(vm);
        w.Show();
        await vm.OpenAsync(repo);
        vm.SelectedItem = vm.Items[0];
        await Settle();
        vm.SelectedUnstaged = vm.Unstaged.Single(x => x.Path == "cart.js");
        await Settle();

        var removed = vm.Diff.First(l => l.Kind == Core.Diff.DiffLineKind.Removed);
        Assert.Equal("2", removed.Text.Substring(removed.ChangeStart, removed.ChangeLength));
        Assert.True(vm.ShowUnified);
        Shot(w, "diff-unified.png");

        vm.IsSplitView = true;
        await Settle();
        Assert.True(vm.ShowSplit);
        Assert.False(w.FindControl<Control>("LineActionsBar")!.IsEffectivelyVisible, "line selection is unified-view only");
        Assert.NotEmpty(vm.SplitRows);
        Shot(w, "diff-split.png");

        // the choice is remembered across restarts
        Assert.True(new SettingsStore(settingsPath).Load().SplitDiff);
    }

    [AvaloniaFact]
    public async Task Selected_lines_can_be_staged_individually()
    {
        var repo = NewRepo();
        File.WriteAllText(Path.Combine(repo, "list.txt"), "a\nb\nc\nd\n");
        Git(repo, "add ."); Git(repo, "commit -qm list");
        File.WriteAllText(Path.Combine(repo, "list.txt"), "a\nb\nNEW1\nNEW2\nNEW3\nc\nd\n");

        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        var w = new MainWindow(vm);
        w.Show();
        await vm.OpenAsync(repo);
        vm.SelectedItem = vm.Items[0];
        await Settle();
        vm.SelectedUnstaged = vm.Unstaged.Single(x => x.Path == "list.txt");
        await Settle();
        Assert.True(vm.LineActionsVisible);
        Assert.True(w.FindControl<Control>("LineActionsBar")!.IsEffectivelyVisible, "the line-action strip must actually be shown");
        Assert.False(vm.LinePrimaryCommand.CanExecute(null)); // nothing selected yet

        // select through the real ListBox so the view wiring is exercised too
        var list = w.FindControl<ListBox>("UnifiedDiffList")!;
        list.SelectedItems!.Add(vm.Diff.Single(l => l.Text == "NEW2"));
        await Settle();
        Assert.Equal(1, vm.SelectedChangeCount);
        Assert.Equal("1 changed line selected", vm.LineSelectionText);
        Shot(w, "line-staging.png");

        await vm.LinePrimaryCommand.ExecuteAsync(null);
        await Settle();

        var staged = Process.Start(new ProcessStartInfo("git", "show :list.txt") { WorkingDirectory = repo, RedirectStandardOutput = true })!;
        var content = staged.StandardOutput.ReadToEnd().Replace("\r\n", "\n");
        staged.WaitForExit();
        Assert.Equal("a\nb\nNEW2\nc\nd\n", content);
        Assert.Contains(vm.Staged, f => f.Path == "list.txt");
        Assert.Contains(vm.Unstaged, f => f.Path == "list.txt");
        Assert.Equal(2, vm.Diff.Count(l => l.Kind == Core.Diff.DiffLineKind.Added)); // NEW1 and NEW3 remain unstaged
    }

    private static string FeatureRepo()
    {
        var repo = NewRepo();
        foreach (var (file, msg) in new[] { ("login.txt", "Add login form"), ("login2.txt", "Fix typo in login form"), ("logout.txt", "Add logout button"), ("debug.txt", "WIP debug output") })
        {
            File.WriteAllText(Path.Combine(repo, file), file); Git(repo, "add ."); Git(repo, $"commit -qm \"{msg}\"");
        }

        return repo;
    }

    [AvaloniaFact]
    public async Task Interactive_rebase_dialog_validates_and_renders()
    {
        var repo = FeatureRepo();
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        await vm.OpenAsync(repo);
        var baseSha = vm.Items.Last(i => !i.IsWorkingDir).Commit!.Sha; // "base"
        var plan = await vm.PrepareInteractiveRebaseAsync(baseSha, $"{baseSha[..8]} “base”");
        Assert.NotNull(plan);
        Assert.Equal(["Add login form", "Fix typo in login form", "Add logout button", "WIP debug output"], plan!.Rows.Select(r => r.Subject));
        Assert.True(plan.CanStart);

        plan.Rows[0].Action = Core.Rebase.RebaseAction.Squash;       // first commit cannot be squashed
        Assert.False(plan.CanStart);
        Assert.Contains("first commit", plan.Problem);
        plan.Rows[0].Action = Core.Rebase.RebaseAction.Pick;

        plan.Rows[1].Action = Core.Rebase.RebaseAction.Fixup;
        plan.Rows[2].Action = Core.Rebase.RebaseAction.Reword;
        plan.Rows[2].Message = "Add a logout button to the header";
        plan.Rows[3].Action = Core.Rebase.RebaseAction.Drop;
        plan.Selected = plan.Rows[2];

        var dlg = new RebaseWindow(plan);
        dlg.Show();
        await Settle();
        Assert.True(plan.CanStart);
        Assert.Equal("Add a logout button to the header", plan.Rows[2].Message); // async prefill must not clobber typed text
        Shot(dlg, "interactive-rebase.png");
        dlg.Close();
    }

    [AvaloniaFact]
    public async Task Choosing_reword_prefills_the_current_message()
    {
        var repo = FeatureRepo();
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        await vm.OpenAsync(repo);
        var plan = (await vm.PrepareInteractiveRebaseAsync(vm.Items.Last(i => !i.IsWorkingDir).Commit!.Sha, "base"))!;
        plan.Rows[0].Action = Core.Rebase.RebaseAction.Reword;
        Assert.False(plan.CanStart); // empty message is rejected until the prefill arrives or the user types
        await Settle();
        Assert.Equal("Add login form", plan.Rows[0].Message);
        Assert.True(plan.CanStart);
    }

    [AvaloniaFact]
    public async Task Interactive_rebase_flow_rewrites_history()
    {
        var repo = FeatureRepo();
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        await vm.OpenAsync(repo);
        var baseSha = vm.Items.Last(i => !i.IsWorkingDir).Commit!.Sha;
        var plan = (await vm.PrepareInteractiveRebaseAsync(baseSha, "base"))!;

        plan.Rows[1].Action = Core.Rebase.RebaseAction.Fixup;
        plan.Rows[2].Action = Core.Rebase.RebaseAction.Reword;
        plan.Rows[2].Message = "Add a logout button to the header";
        plan.Rows[3].Action = Core.Rebase.RebaseAction.Drop;
        await vm.InteractiveRebaseAsync(baseSha, plan.ToTodo());
        await Settle();

        Assert.False(vm.IsError, vm.StatusText);
        Assert.Equal(["Add a logout button to the header", "Add login form", "base"], vm.Items.Where(i => !i.IsWorkingDir).Select(i => i.Subject));
        Assert.False(File.Exists(Path.Combine(repo, "debug.txt")));
        Assert.True(File.Exists(Path.Combine(repo, "login2.txt"))); // fixup kept the file content
    }

    [AvaloniaFact]
    public async Task Rebase_with_no_commits_reports_instead_of_opening_dialog()
    {
        var repo = NewRepo();
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        await vm.OpenAsync(repo);
        Assert.Null(await vm.PrepareInteractiveRebaseAsync(vm.Items[0].Commit!.Sha, "base"));
        Assert.Contains("No commits", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Submodules_are_listed_and_can_be_opened_and_left()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
        try
        {
            var alpha = NewRepo();
            var beta = NewRepo();
            var super = NewRepo();
            Git(super, $"submodule add -q \"{alpha}\" libs/alpha");
            Git(super, $"submodule add -q \"{beta}\" libs/beta");
            Git(super, "commit -qm \"add submodules\"");

            // a plain clone starts with both uninitialized; initialize only alpha, then move alpha ahead
            var work = Path.Combine(Path.GetTempPath(), "gxc-ui-work-" + Guid.NewGuid().ToString("N")[..8]);
            Git(Path.GetTempPath(), $"clone -q \"{super}\" \"{work}\"");
            Git(work, "submodule update --init -q libs/alpha");
            File.WriteAllText(Path.Combine(work, "libs", "alpha", "new.txt"), "x");
            Git(Path.Combine(work, "libs", "alpha"), "config user.name Ada"); Git(Path.Combine(work, "libs", "alpha"), "config user.email a@x");
            Git(Path.Combine(work, "libs", "alpha"), "add .");
            Git(Path.Combine(work, "libs", "alpha"), "commit -qm \"alpha moves ahead\"");

            var vm = new MainViewModel(new SettingsStore(Path.Combine(work, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
            var w = new MainWindow(vm);
            w.Show();
            await vm.OpenAsync(work);
            await Settle();

            var subs = vm.Sidebar.Where(e => e.IsSubmodule).ToList();
            Assert.Equal(["libs/alpha", "libs/beta"], subs.Select(e => e.Text));
            Assert.Equal(Core.Submodules.SubmoduleState.Modified, subs[0].Submodule!.State);
            Assert.Equal(Core.Submodules.SubmoduleState.NotInitialized, subs[1].Submodule!.State);
            Shot(w, "submodules.png");

            // an uninitialized submodule cannot be opened
            await vm.OpenSubmoduleAsync(subs[1].Submodule!);
            Assert.False(vm.HasParent);
            Assert.Contains("not initialized", vm.StatusText);

            // initialize it from the app, then open it
            await vm.UpdateSubmoduleAsync(subs[1].Submodule!);
            Assert.Equal(Core.Submodules.SubmoduleState.UpToDate, vm.Sidebar.Single(e => e.Submodule?.Path == "libs/beta").Submodule!.State);

            var recentBefore = vm.Recent.Count;
            await vm.OpenSubmoduleAsync(vm.Sidebar.Single(e => e.Submodule?.Path == "libs/alpha").Submodule!);
            Assert.True(vm.HasParent);
            Assert.Equal("alpha", vm.RepoName);
            Assert.Contains(vm.Items, i => i.Subject == "alpha moves ahead");
            Assert.StartsWith("◀  Back to gxc-ui-work-", vm.BackText);
            Assert.Equal(recentBefore, vm.Recent.Count); // submodules do not pollute the recent list

            await vm.BackToParentCommand.ExecuteAsync(null);
            Assert.False(vm.HasParent);
            Assert.Equal(Path.GetFileName(work), vm.RepoName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", null);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", null);
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", null);
        }
    }

    private static string NewRepo()
    {
        var repo = Path.Combine(Path.GetTempPath(), "gxc-ui-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(repo);
        Git(repo, "init -q -b main"); Git(repo, "config user.name Ada"); Git(repo, "config user.email a@x");
        File.WriteAllText(Path.Combine(repo, "f.txt"), "base\n"); Git(repo, "add ."); Git(repo, "commit -qm base");
        return repo;
    }

    [AvaloniaFact]
    public async Task Squash_merge_leaves_changes_staged_and_opens_commit_tab()
    {
        var repo = NewRepo();
        Git(repo, "checkout -qb topic"); File.WriteAllText(Path.Combine(repo, "t.txt"), "t"); Git(repo, "add ."); Git(repo, "commit -qm topic");
        Git(repo, "checkout -q main");
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        await vm.OpenAsync(repo);

        await vm.MergeWithAsync(vm.Sidebar.First(e => e.Ref?.Name == "topic").Ref!, Core.Git.MergeMode.Squash);
        Assert.Equal(1, vm.SelectedTab);
        Assert.Contains(vm.Staged, f => f.Path == "t.txt");
        Assert.Contains("squash", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.HasOperation);
    }

    [AvaloniaFact]
    public async Task Rebase_conflict_shows_rebase_banner_then_continues()
    {
        var repo = NewRepo();
        Git(repo, "checkout -qb topic"); File.WriteAllText(Path.Combine(repo, "f.txt"), "topic\n"); Git(repo, "commit -qam topic");
        Git(repo, "checkout -q main"); File.WriteAllText(Path.Combine(repo, "f.txt"), "main\n"); Git(repo, "commit -qam main");
        Git(repo, "checkout -q topic");
        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        await vm.OpenAsync(repo);

        await vm.RebaseOntoAsync(vm.Sidebar.First(e => e.Ref?.Name == "main").Ref!);
        Assert.Equal(Core.Models.GitOperation.Rebase, vm.Operation);
        Assert.Contains("Rebase", vm.OperationText);
        Assert.Equal(1, vm.ConflictCount);

        await vm.ResolveAsync(vm.Unstaged.Single(), ours: false);
        await vm.ContinueOperationCommand.ExecuteAsync(null);
        Assert.False(vm.HasOperation);
        Assert.Equal("topic", vm.Items.First(i => !i.IsWorkingDir).Subject);
    }

    private static void Git(string dir, string args)
    {
        var psi = new ProcessStartInfo("git", args) { WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }

    [AvaloniaFact]
    public async Task Settings_dialog_shows_fake_identity_and_saves_values()
    {
        // Screenshots are committed to the repo: never let them read the developer's real ~/.gitconfig.
        var cfg = Path.Combine(Path.GetTempPath(), "gxc-fakecfg-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(cfg, "[user]\n\tname = Ada Lovelace\n\temail = ada@example.com\n");
        var old = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        // a stand-in "collaterale" on PATH so the tool status is deterministic and never shows a developer's real install
        var bin = Path.Combine(Path.GetTempPath(), "gxc-bin-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(bin);
        var fake = Path.Combine(bin, OperatingSystem.IsWindows() ? "collaterale.exe" : "collaterale");
        File.WriteAllText(fake, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + oldPath);
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", cfg);
        try
        {
            var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "gxc-nos-" + Guid.NewGuid() + ".json")).Load();
            var dlg = new SettingsWindow(settings);
            dlg.Show();
            await Settle();
            Assert.Equal("Ada Lovelace", dlg.FindControl<TextBox>("NameBox")!.Text);
            Assert.StartsWith("✓ git version", dlg.FindControl<TextBlock>("GitStatusText")!.Text);
            Assert.Matches(@"^git-cross \d+\.\d+\.\d+  ·  MIT License$", dlg.FindControl<TextBlock>("VersionText")!.Text);
            Assert.Contains("Inspired by Git Extensions", dlg.FindControl<TextBlock>("CreditText")!.Text);
            Assert.Contains("Not affiliated", dlg.FindControl<TextBlock>("CreditText")!.Text);
            Assert.Contains("Collaterale was found", dlg.FindControl<TextBlock>("MergeStatus")!.Text); // nothing configured yet: hint only

            dlg.FindControl<ComboBox>("PresetBox")!.SelectedIndex = 1; // Collaterale
            await Settle();
            Assert.Contains("--merge", dlg.FindControl<TextBox>("MergeBox")!.Text);
            Assert.Equal("collaterale \"$LOCAL\" \"$REMOTE\"", dlg.FindControl<TextBox>("DiffBox")!.Text);
            Assert.Equal("✓ Found " + Path.GetFileName(fake), dlg.FindControl<TextBlock>("MergeStatus")!.Text);

            dlg.FindControl<TextBox>("MergeBox")!.Text = "collaterale $MERGE";
            await Settle();
            Assert.Contains("Unknown variable $MERGE", dlg.FindControl<TextBlock>("MergeStatus")!.Text);
            dlg.FindControl<TextBox>("MergeBox")!.Text = "no-such-tool-xyz $MERGED";
            await Settle();
            Assert.Contains("was not found", dlg.FindControl<TextBlock>("MergeStatus")!.Text);

            dlg.FindControl<ComboBox>("PresetBox")!.SelectedIndex = 0;
            dlg.FindControl<ComboBox>("PresetBox")!.SelectedIndex = 1;
            await Settle();
            Shot(dlg, "settings-dialog.png");
            dlg.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", old);
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Directory.Delete(bin, true);
            File.Delete(cfg);
        }
    }

    [AvaloniaFact]
    public async Task Conflict_can_be_resolved_with_the_configured_merge_tool()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var repo = NewRepo();
        File.WriteAllText(Path.Combine(repo, "f.txt"), "base\n");
        Git(repo, "add ."); Git(repo, "commit -qm base");
        Git(repo, "checkout -qb topic");
        File.WriteAllText(Path.Combine(repo, "f.txt"), "topic\n"); Git(repo, "commit -qam topic");
        Git(repo, "checkout -q main");
        File.WriteAllText(Path.Combine(repo, "f.txt"), "main\n"); Git(repo, "commit -qam main");
        Git(repo, "merge topic -q");   // conflicts; Git() ignores the exit code

        var vm = new MainViewModel(new SettingsStore(Path.Combine(repo, "..", "gxc-s-" + Guid.NewGuid() + ".json")));
        var w = new MainWindow(vm);
        w.Show();
        await vm.OpenAsync(repo);
        await Settle();
        var file = vm.Unstaged.Single(f => f.IsConflicted);
        Assert.Equal(1, vm.ConflictCount);

        // no tool configured: a clear instruction, nothing changes
        await vm.ResolveInToolAsync(file);
        Assert.True(vm.IsError);
        Assert.Contains("Settings", vm.StatusText);
        Assert.Equal(1, vm.ConflictCount);

        // a tool that gives up leaves the conflict and explains
        vm.Settings.MergeTool = "sh -c 'exit 3'";
        await vm.ResolveInToolAsync(file);
        Assert.True(vm.IsError);
        Assert.Contains("still conflicted", vm.StatusText);
        Assert.Equal(1, vm.ConflictCount);

        // a tool that writes the merged result resolves it and the banner offers Continue
        vm.Settings.MergeTool = "sh -c 'printf \"merged by tool\\n\" > \"$1\"' _ $MERGED";
        await vm.ResolveInToolAsync(file);
        Assert.False(vm.IsError, vm.StatusText);
        Assert.Equal(0, vm.ConflictCount);
        Assert.True(vm.CanContinue);
        Assert.Equal("merged by tool\n", File.ReadAllText(Path.Combine(repo, "f.txt")).Replace("\r\n", "\n"));

        await vm.ContinueOperationCommand.ExecuteAsync(null);
        Assert.False(vm.HasOperation);
    }

    [AvaloniaFact]
    public async Task Dialogs_render()
    {
        var clone = new CloneWindow("/path/to/projects");
        clone.Show();
        clone.FindControl<TextBox>("UrlBox")!.Text = "https://github.com/gitextensions/gitextensions.git";
        await Settle();
        Shot(clone, "clone-dialog.png");
        clone.Close();

        var confirm = new PromptWindow("Hard reset", "Move the current branch to 1b39b6d and DISCARD all uncommitted changes? This cannot be undone.", "Hard reset", false);
        confirm.Show();
        await Settle();
        Shot(confirm, "confirm-dialog.png");
        confirm.Close();
    }
}
