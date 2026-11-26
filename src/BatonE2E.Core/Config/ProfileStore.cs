using System.Text.Json;

namespace BatonE2E.Core.Config;

/// <summary>名前付きの設定セット（プロファイル）。config.json と同じフォルダの profiles/ に 1件1ファイルで保存する。</summary>
public static class ProfileStore
{
    public const int MaxNameLength = 40;
    private const string Ext = ".json";
    private static readonly char[] BadChars = "<>:\"/\\|?*".ToCharArray();
    private static readonly string[] Reserved =
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static string Dir(string configPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "profiles");

    /// <summary>名前→ファイルのパス。ディレクトリ成分を含む名前は受け付けない（パストラバーサル対策）。</summary>
    internal static string FileOf(string configPath, string name)
    {
        var safe = Path.GetFileName(name);
        if (safe.Length == 0 || safe != name) throw new ArgumentException("profile-name", nameof(name));
        return Path.Combine(Dir(configPath), safe + Ext);
    }

    internal static string BackupDirOf(string configPath, string name) => Path.Combine(AppSettings.BackupDir(configPath), "profile-" + name);

    /// <summary>「プロファイルを使わない」状態の表示名と同じ名前（作れない）。</summary>
    private static readonly string[] ReservedNone = { "（なし）", "(なし)", "(None)", "（None）" };

    public static bool IsNoneName(string? name) => ReservedNone.Contains(Clean(name), StringComparer.OrdinalIgnoreCase);

    /// <summary>名前の前後の空白を除く。</summary>
    public static string Clean(string? name) => (name ?? "").Trim();

    /// <summary>ファイル名として使えない名前なら問題を返す（なければ null）。</summary>
    public static AppSettings.Issue? CheckName(string? name)
    {
        var n = Clean(name);
        if (n.Length == 0) return new("gui.profile.err.empty", "Profile", Array.Empty<object>());
        if (IsNoneName(n)) return new("gui.profile.err.reserved", "Profile", Array.Empty<object>());
        if (n.Length > MaxNameLength) return new("gui.profile.err.long", "Profile", new object[] { MaxNameLength });
        if (n.Any(c => c < ' ' || BadChars.Contains(c)) || n.EndsWith('.') || Reserved.Contains(n.Split('.')[0].ToUpperInvariant()))
            return new("gui.profile.err.chars", "Profile", Array.Empty<object>());
        return null;
    }

    /// <summary>保存済みの名前の一覧（五十音・ABC順）。</summary>
    public static List<string> List(string configPath)
    {
        var d = Dir(configPath);
        if (!Directory.Exists(d)) return new();
        return Directory.GetFiles(d, "*" + Ext).Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => CheckName(n) == null).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool Exists(string configPath, string name) => CheckName(name) == null && File.Exists(FileOf(configPath, Clean(name)));

    /// <summary>保存（原子的に書き込み）。名前が不正なら例外。</summary>
    public static void Save(string configPath, string name, AppSettings s)
    {
        if (CheckName(name) != null) throw new ArgumentException("profile-name", nameof(name));
        s.Normalize();
        Directory.CreateDirectory(Dir(configPath));
        var n = Clean(name);
        AppSettings.WriteWithBackup(FileOf(configPath, n), s.ProfileJson(), BackupDirOf(configPath, n), "profile-");
    }

    /// <summary>読み込む。無い・壊れている場合は null（ファイルは触らない）。
    /// 言語・ログのファイル出力・トレイ格納・通知音は全体の設定なので、プロファイルでは切り替えず current の値を保つ。</summary>
    public static AppSettings? Load(string configPath, string name, AppSettings current)
    {
        if (CheckName(name) != null) return null;
        var s = AppSettings.TryRead(FileOf(configPath, Clean(name)));
        if (s == null) return null;
        s.CurrentProfile = Clean(name);
        s.CopyGlobalsFrom(current);
        return s;
    }

    public static bool Delete(string configPath, string name)
    {
        if (CheckName(name) != null) return false;
        var p = FileOf(configPath, Clean(name));
        if (!File.Exists(p)) return false;
        try { File.Delete(p); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
