using System.Diagnostics;
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

    private static void Shot(MainWindow w, string name)
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
        Shot(w, "01-welcome.png");
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
        Shot(w, "02-history.png");

        vm.SelectedItem = vm.Items[0];
        await Settle();
        vm.SelectedUnstaged = vm.Unstaged.First(f => f.Path == "a.txt");
        await Settle();
        Assert.NotEmpty(vm.Diff);
        Shot(w, "03-commit.png");

        vm.CycleThemeCommand.Execute(null); // System -> Light
        vm.CycleThemeCommand.Execute(null); // Light -> Dark
        await Settle();
        Shot(w, "04-commit-dark.png");
        vm.SelectedItem = vm.Items[1];
        await Settle();
        Shot(w, "05-history-dark.png");

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
        Shot(w, "06-hunks.png");

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
        Shot(w, "07-stash.png");

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
        Shot(w, "08-blame.png");

        await vm.ShowFileHistoryAsync("login.cs");
        await Settle();
        Assert.True(vm.HasFileFilter);
        Assert.Equal(["Add login screen"], vm.Items.Select(i => i.Subject));
        Shot(w, "09-file-history.png");

        await vm.ShowCommitAsync(vm.Items[0].Commit!.Sha);
        Assert.False(vm.HasFileFilter);
        Assert.Equal("Add login screen", vm.SelectedItem?.Subject);
    }

    private static void Git(string dir, string args)
    {
        var psi = new ProcessStartInfo("git", args) { WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }
}
