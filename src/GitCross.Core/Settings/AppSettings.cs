using System.Text.Json;

namespace GitCross.Core.Settings;

public sealed class AppSettings
{
    public const int MaxRecent = 15;

    public List<string> RecentRepositories { get; set; } = [];

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    public string? LastCloneDirectory { get; set; }

    public void AddRecent(string path)
    {
        RecentRepositories.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));
        RecentRepositories.Insert(0, path);
        if (RecentRepositories.Count > MaxRecent)
        {
            RecentRepositories.RemoveRange(MaxRecent, RecentRepositories.Count - MaxRecent);
        }
    }
}

/// <summary>JSON settings file. Missing or corrupt files yield defaults; saves are atomic.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "git-cross", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
