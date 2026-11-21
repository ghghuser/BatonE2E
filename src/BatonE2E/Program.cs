using BatonE2E.Core;

namespace BatonE2E;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Loc.Load(Path.Combine(AppContext.BaseDirectory, "Lang"), "ja");
            Console.WriteLine(AppInfo.Title);
            Console.WriteLine(Loc.Get("app.license"));
            Console.WriteLine(Loc.Get("app.p0"));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "BatonE2E-crash.log"), ex.ToString());
            return 1;
        }
    }
}
