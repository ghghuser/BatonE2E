using BatonE2E.Core;

namespace BatonE2E.Tests;

internal static class CoreTests
{
    public static void Run(string iniPath)
    {
        var d = Loc.Parse("; コメント\r\na.b=こんにちは\r\nc=1\\n2\r\n[sec]\r\n");
        T.Check("Loc.Parse: 基本", d["a.b"] == "こんにちは");
        T.Check("Loc.Parse: 改行展開", d["c"] == "1\n2");
        T.Check("Loc.Parse: コメント・節は無視", d.Count == 2);

        if (File.Exists(iniPath))
        {
            var keys = File.ReadAllLines(iniPath).Where(l => l.Length > 0 && l[0] != ';' && l.Contains('='))
                .Select(l => l[..l.IndexOf('=')].Trim()).ToList();
            T.Check("ja.ini: キー重複なし", keys.Count == keys.Distinct().Count());
        }
        else T.Check("ja.ini: ファイルが見つかる", false);

        // en.ini は ja.ini と同じキー・同じ書式引数（{0}等）を持つこと
        var en = Path.Combine(Path.GetDirectoryName(iniPath) ?? ".", "en.ini");
        if (File.Exists(iniPath) && File.Exists(en))
        {
            var ja = Loc.Parse(File.ReadAllText(iniPath));
            var e = Loc.Parse(File.ReadAllText(en));
            T.Check("en.ini: キーがja.iniと一致", ja.Keys.OrderBy(k => k).SequenceEqual(e.Keys.OrderBy(k => k)));
            static string Ph(string s) => string.Concat(System.Text.RegularExpressions.Regex.Matches(s, @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(x => x));
            var bad = ja.Where(kv => e.TryGetValue(kv.Key, out var v) && Ph(v) != Ph(kv.Value)).Select(kv => kv.Key).ToList();
            T.Check("en.ini: 書式引数がja.iniと一致" + (bad.Count > 0 ? "（" + string.Join(",", bad) + "）" : ""), bad.Count == 0);
        }
        else T.Check("en.ini: ファイルが見つかる", false);

        T.Check("AppInfo: 版数形式", Version.TryParse(AppInfo.Version, out var v) && v!.Major == 1);
    }
}
