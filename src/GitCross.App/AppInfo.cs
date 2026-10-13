using System.Reflection;

namespace GitCross.App;

public static class AppInfo
{
    public const string Name = "git-cross";

    /// <summary>Version from the build ("0.1.0"); any "+commit" suffix added by the SDK is removed.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
