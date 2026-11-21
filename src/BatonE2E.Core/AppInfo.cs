using System.Reflection;

namespace BatonE2E.Core;

/// <summary>アプリ名と版数の取得。</summary>
public static class AppInfo
{
    public const string Name = "BatonE2E";

    public static string Version =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.0.0.0";

    public static string Title => $"{Name} v{Version}";
}
