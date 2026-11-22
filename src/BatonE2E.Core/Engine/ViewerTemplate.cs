using System.Reflection;
using System.Text;

namespace BatonE2E.Core.Engine;

/// <summary>標準の視聴ページ（exeに埋め込み）。ひな形として出力し、利用者が編集して差し替えられる。</summary>
public static class ViewerTemplate
{
    public static string ReadEmbedded()
    {
        using var s = typeof(ViewerTemplate).Assembly.GetManifestResourceStream("viewer.html")
                      ?? throw new InvalidOperationException("viewer-page-missing");
        using var rd = new StreamReader(s, Encoding.UTF8);
        return rd.ReadToEnd();
    }

    public const string CssSample = """
/* viewer.css: 視聴ページの見た目を変えるCSS（exeの横に置く）。
   標準ページの<head>末尾に差し込まれるので、ここに書いたものが優先されます。
   例:
   body { background: #101820; }
   main { max-width: 560px; }
   h1   { color: #ffd166; }
*/
""";
}
