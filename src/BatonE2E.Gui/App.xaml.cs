using System.Windows;
using System.Windows.Threading;
using BatonE2E.Core;
using BatonE2E.Core.Config;
using BatonE2E.Core.Logging;

namespace BatonE2E.Gui;

public partial class App : Application
{
    public static string ConfigPath { get; } = Path.Combine(AppContext.BaseDirectory, "config.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        var settings = AppSettings.Load(ConfigPath, out var corrupted);
        Loc.Load(Path.Combine(AppContext.BaseDirectory, "Lang"), settings.Language);
        AppLog.ToConsole = false;
        AppLog.FilePath = settings.LogToFile ? Path.Combine(AppContext.BaseDirectory, "BatonE2E.log") : null;

        new MainWindow(settings, corrupted).Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "BatonE2E-crash.log"), e.Exception.ToString()); } catch { /* 書けなくても続行 */ }
        AppLog.Error(Loc.Get("gui.err.crash", e.Exception.Message));
        MessageBox.Show(Loc.Get("gui.err.crash", e.Exception.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
