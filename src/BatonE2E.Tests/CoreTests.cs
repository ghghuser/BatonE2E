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

        T.Check("AppInfo: 版数形式", Version.TryParse(AppInfo.Version, out var v) && v!.Major == 1);
    }
}
