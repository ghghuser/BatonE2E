using System.Text.Json;

namespace BatonE2E.Core.Config;

/// <summary>名前付きの設定セット（プリセット）。config.json と同じフォルダの presets/ に 1件1ファイルで保存する。</summary>
public static class PresetStore
{
    public const int MaxNameLength = 40;
    private const string Ext = ".json";
    private static readonly char[] BadChars = "<>:\"/\\|?*".ToCharArray();
    private static readonly string[] Reserved =
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static string Dir(string configPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "presets");

    private static string FileOf(string configPath, string name) => Path.Combine(Dir(configPath), name + Ext);

    /// <summary>名前の前後の空白を除く。</summary>
    public static string Clean(string? name) => (name ?? "").Trim();

    /// <summary>ファイル名として使えない名前なら問題を返す（なければ null）。</summary>
    public static AppSettings.Issue? CheckName(string? name)
    {
        var n = Clean(name);
        if (n.Length == 0) return new("gui.preset.err.empty", "Preset", Array.Empty<object>());
        if (n.Length > MaxNameLength) return new("gui.preset.err.long", "Preset", new object[] { MaxNameLength });
        if (n.Any(c => c < ' ' || BadChars.Contains(c)) || n.EndsWith('.') || Reserved.Contains(n.Split('.')[0].ToUpperInvariant()))
            return new("gui.preset.err.chars", "Preset", Array.Empty<object>());
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
        if (CheckName(name) != null) throw new ArgumentException("preset-name", nameof(name));
        s.Normalize();
        Directory.CreateDirectory(Dir(configPath));
        var path = FileOf(configPath, Clean(name));
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, s.ToJson());
        File.Move(tmp, path, true);
    }

    /// <summary>読み込む。無い・壊れている場合は null（ファイルは触らない）。
    /// 言語・ログのファイル出力・トレイ格納は全体の設定なので、プリセットでは切り替えず current の値を保つ。</summary>
    public static AppSettings? Load(string configPath, string name, AppSettings current)
    {
        if (CheckName(name) != null) return null;
        var s = AppSettings.TryRead(FileOf(configPath, Clean(name)));
        if (s == null) return null;
        s.Language = current.Language;
        s.LogToFile = current.LogToFile;
        s.MinimizeToTray = current.MinimizeToTray;
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
