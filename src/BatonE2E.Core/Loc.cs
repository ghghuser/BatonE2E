using System.Text;

namespace BatonE2E.Core;

/// <summary>Lang/*.ini（キー=文）の読み込み。テキストエディタで修正可能。</summary>
public static class Loc
{
    private static Dictionary<string, string> _map = new();

    public static string Language { get; private set; } = "ja";

    public static IReadOnlyDictionary<string, string> Map => _map;

    /// <summary>設定値（system / ja / en）を、実際に使う言語（ja / en）へ。system はOSの表示言語が日本語なら ja、それ以外は en。</summary>
    public static string Resolve(string? setting, string? osLanguage = null)
    {
        if (setting == "ja" || setting == "en") return setting;
        var os = osLanguage ?? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(os, "ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
    }

    public static void Load(string langDir, string language)
    {
        Language = language;
        var path = Path.Combine(langDir, language + ".ini");
        _map = File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new();
    }

    public static Dictionary<string, string> Parse(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            d[line[..i].Trim()] = line[(i + 1)..].Replace("\\n", "\n");
        }
        return d;
    }

    /// <summary>キーに対応する文を返す。未定義ならキー名をそのまま返す。{0}等は書式引数。</summary>
    public static string Get(string key, params object[] args)
    {
        if (!_map.TryGetValue(key, out var s)) return key;
        return args.Length == 0 ? s : string.Format(s, args);
    }
}
