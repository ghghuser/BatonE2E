namespace BatonE2E.Tests;

/// <summary>自己テストの最小フレームワーク。</summary>
internal static class T
{
    public static int Failed;

    public static void Check(string name, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "NG  ")}{name}");
        if (!ok) Failed++;
    }
}
