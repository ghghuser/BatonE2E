using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>改行をLFにそろえる（保存し直しでCRLFになってもスクリプトのハッシュが変わらないように）。</summary>
    public static string NormalizeNewlines(string html) => html.Replace("\r\n", "\n");

    private static readonly Regex ScriptRx = new(@"<script>(?<s>[\s\S]*?)</script>", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    /// <summary>ページ内の最初の属性なし&lt;script&gt;の内容のCSPハッシュ（'sha256-…'）。無ければ null。</summary>
    public static string? ScriptHashOf(string html)
    {
        var m = ScriptRx.Match(NormalizeNewlines(html));
        if (!m.Success) return null;
        return "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups["s"].Value))) + "'";
    }

    private static string? _stockHash;
    /// <summary>標準の視聴ページのスクリプトのハッシュ。CSPはこのスクリプトだけを許可する。</summary>
    public static string StockScriptHash => _stockHash ??= ScriptHashOf(ReadEmbedded()) ?? throw new InvalidOperationException("viewer-script-missing");

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
