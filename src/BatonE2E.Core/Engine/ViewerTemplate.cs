using System.Reflection;
using System.Text;

namespace BatonE2E.Core.Engine;

/// <summary>標準の視聴ページ（exeに埋め込み）。ひな形として出力し、利用者が編集して差し替えられる。</summary>
public static class ViewerTemplate
{
    /// <summary>差し替えファイルを置くフォルダ名（exeの横）。</summary>
    public const string FolderName = "ViewerPage";

    public static string Folder => Path.Combine(AppContext.BaseDirectory, FolderName);

    /// <summary>差し替えファイルの場所（ViewerPageフォルダ内）。無ければ null。</summary>
    public static string? FindFile(string name)
    {
        var p = Path.Combine(Folder, name);
        return File.Exists(p) ? p : null;
    }

    public static string ReadEmbedded()
    {
        using var s = typeof(ViewerTemplate).Assembly.GetManifestResourceStream("viewer.html")
                      ?? throw new InvalidOperationException("viewer-page-missing");
        using var rd = new StreamReader(s, Encoding.UTF8);
        return rd.ReadToEnd();
    }

    public const string CssSample = """
/* viewer.css: 視聴ページの見た目を変えるCSS（ViewerPageフォルダに置く）。
   標準ページの<head>末尾に差し込まれるので、ここに書いたものが優先されます。
   例:
   body { background: #101820; }
   main { max-width: 560px; }
   h1   { color: #ffd166; }
*/
""";
}
