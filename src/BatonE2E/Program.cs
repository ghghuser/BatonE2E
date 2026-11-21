using System.Text;
using BatonE2E.Core;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;
using BatonE2E.Core.Security;

namespace BatonE2E;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            Loc.Load(Path.Combine(AppContext.BaseDirectory, "Lang"), "ja");
            AppLog.FilePath = Path.Combine(AppContext.BaseDirectory, "BatonE2E.log");
            return await RunAsync(args);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "BatonE2E-crash.log"), ex.ToString());
            Console.WriteLine(Loc.Get("console.fatal", ex.Message));
            if (!Console.IsInputRedirected)
            {
                Console.WriteLine(Loc.Get("console.press.enter"));
                Console.ReadLine();
            }
            return 1;
        }
    }

    private static string? Opt(string[] args, string name, string env)
    {
        var i = Array.IndexOf(args, name);
        if (i >= 0 && i + 1 < args.Length) return args[i + 1];
        var e = Environment.GetEnvironmentVariable(env);
        return string.IsNullOrEmpty(e) ? null : e;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var o = new EngineOptions
        {
            SourcePort = int.Parse(Opt(args, "--source-port", "BATONE2E_SOURCE_PORT") ?? "8000"),
            ViewPort = int.Parse(Opt(args, "--view-port", "BATONE2E_VIEW_PORT") ?? "8080"),
            Mount = Opt(args, "--mount", "BATONE2E_MOUNT") ?? "/stream",
            MaxViewers = int.Parse(Opt(args, "--max-viewers", "BATONE2E_MAX_VIEWERS") ?? "20"),
            SourcePassword = Opt(args, "--source-password", "BATONE2E_SOURCE_PASSWORD") ?? PassphraseGenerator.Generate(4, 4),
            ViewPassword = Opt(args, "--password", "BATONE2E_PASSWORD") ?? PassphraseGenerator.Generate(),
        };

        Console.WriteLine(AppInfo.Title);
        Console.WriteLine(Loc.Get("app.license"));
        await using var engine = new RelayEngine(o);
        await engine.StartAsync();

        Console.WriteLine();
        Console.WriteLine(Loc.Get("console.source.title"));
        Console.WriteLine("  " + Loc.Get("console.source.server"));
        Console.WriteLine("  " + Loc.Get("console.source.port", o.SourcePort));
        Console.WriteLine("  " + Loc.Get("console.source.mount", o.Mount));
        Console.WriteLine("  " + Loc.Get("console.source.user"));
        Console.WriteLine("  " + Loc.Get("console.source.password", o.SourcePassword));
        Console.WriteLine("  " + Loc.Get("console.source.tls"));
        Console.WriteLine(Loc.Get("console.view.title"));
        Console.WriteLine("  " + Loc.Get("console.view.url", $"http://127.0.0.1:{o.ViewPort}/"));
        Console.WriteLine("  " + Loc.Get("console.view.tunnel"));
        Console.WriteLine("  " + Loc.Get("console.view.passphrase", o.ViewPassword));
        Console.WriteLine(Loc.Get("console.warn.random"));
        Console.WriteLine(Loc.Get("console.stop"));
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        long last = 0;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                var st = engine.Stats;
                var kb = st.BytesIn / 1024;
                var src = st.SourceActive ? Loc.Get("console.stats.source", st.SourceMime) : Loc.Get("console.stats.none");
                AppLog.Info(Loc.Get("console.stats", kb, (kb - last) / 10, st.Frames, engine.Hub?.ViewerCount ?? 0, st.CheckOk, st.CheckNg, src));
                last = kb;
            }
        }
        catch (OperationCanceledException) { }
        return 0;
    }
}
