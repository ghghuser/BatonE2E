using BatonE2E.Core.Config;

namespace BatonE2E.Tests;

internal static class PresetTests
{
    public static void Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "batone2e-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = Path.Combine(dir, "config.json");
        try
        {
            T.Check("プリセット: 一覧は最初は空", PresetStore.List(cfg).Count == 0);
            var a = new AppSettings { SourcePort = 9100, ViewPort = 9200, PageTitle = "夜の配信", Language = "en", LogToFile = true };
            PresetStore.Save(cfg, "  夜の配信用  ", a);
            PresetStore.Save(cfg, "Home", new AppSettings { SourcePort = 9300 });
            var names = PresetStore.List(cfg);
            T.Check("プリセット: 保存した名前が一覧に出る（前後の空白は除く）", names.Count == 2 && names.Contains("夜の配信用") && names.Contains("Home"));
            T.Check("プリセット: 存在確認", PresetStore.Exists(cfg, "Home") && !PresetStore.Exists(cfg, "Nothing"));

            var cur = new AppSettings { Language = "ja", LogToFile = false };
            var l = PresetStore.Load(cfg, "夜の配信用", cur);
            T.Check("プリセット: 内容を読み込める", l != null && l.SourcePort == 9100 && l.ViewPort == 9200 && l.PageTitle == "夜の配信");
            T.Check("プリセット: 言語・ログ出力は現在の値のまま", l != null && l.Language == "ja" && !l.LogToFile);
            T.Check("プリセット: 無い名前は null", PresetStore.Load(cfg, "Nothing", cur) == null);

            PresetStore.Save(cfg, "Home", new AppSettings { SourcePort = 9400 });
            T.Check("プリセット: 同名は上書き（件数は増えない）", PresetStore.List(cfg).Count == 2 && PresetStore.Load(cfg, "Home", cur)!.SourcePort == 9400);

            File.WriteAllText(Path.Combine(PresetStore.Dir(cfg), "Broken.json"), "{ これはJSONではない");
            T.Check("プリセット: 壊れたファイルは null（ファイルは残る）", PresetStore.Load(cfg, "Broken", cur) == null && File.Exists(Path.Combine(PresetStore.Dir(cfg), "Broken.json")));

            T.Check("プリセット: 削除できる", PresetStore.Delete(cfg, "Home") && !PresetStore.Exists(cfg, "Home") && !PresetStore.Delete(cfg, "Home"));

            string[] bad = { "", "   ", "a/b", "..\\x", "a:b", "con", "NUL.txt", "end.", "x\ty", new string('あ', 41) };
            T.Check("プリセット: 使えない名前は拒否", bad.All(n => PresetStore.CheckName(n) != null));
            string[] good = { "夜の配信", "Home 2", "a-b_c", new string('あ', 40) };
            T.Check("プリセット: 普通の名前は許可", good.All(n => PresetStore.CheckName(n) == null));
            var threw = false;
            try { PresetStore.Save(cfg, "../evil", new AppSettings()); } catch (ArgumentException) { threw = true; }
            T.Check("プリセット: フォルダ外への保存は例外", threw && !File.Exists(Path.Combine(dir, "evil.json")));
            T.Check("プリセット: 保存フォルダ外へは読み書きしない", PresetStore.Load(cfg, "../config", cur) == null && !PresetStore.Delete(cfg, "../config"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
