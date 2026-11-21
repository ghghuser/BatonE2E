using BatonE2E.Core;

// 自己テスト（外部ライブラリなし）。失敗があれば終了コード1。
var failed = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "OK  " : "NG  ")}{name}");
    if (!ok) failed++;
}

// Loc.Parse
var d = Loc.Parse("; コメント\r\na.b=こんにちは\r\nc=1\\n2\r\n[sec]\r\n");
Check("Loc.Parse: 基本", d["a.b"] == "こんにちは");
Check("Loc.Parse: 改行展開", d["c"] == "1\n2");
Check("Loc.Parse: コメント・節は無視", d.Count == 2);

// ja.ini の重複キー検査
var iniPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BatonE2E", "Lang", "ja.ini");
if (File.Exists(iniPath))
{
    var keys = File.ReadAllLines(iniPath).Where(l => l.Length > 0 && l[0] != ';' && l.Contains('='))
        .Select(l => l[..l.IndexOf('=')].Trim()).ToList();
    Check("ja.ini: キー重複なし", keys.Count == keys.Distinct().Count());
}
else Check("ja.ini: ファイルが見つかる", false);

// 版数
Check("AppInfo: 版数形式", Version.TryParse(AppInfo.Version, out var v) && v!.Major == 1);

Console.WriteLine(failed == 0 ? "全テスト成功" : $"失敗: {failed}件");
return failed == 0 ? 0 : 1;
