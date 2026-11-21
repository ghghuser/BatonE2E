namespace BatonE2E.Core.Logging;

public enum LogLv { Debug = 0, Info = 1, Warn = 2, Error = 3 }

/// <summary>簡易ログ。コンソールとファイルへ出力。パスワード類は呼び出し側で渡さないこと。</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static LogLv Min { get; set; } = LogLv.Info;
    public static bool ToConsole { get; set; } = true;
    public static string? FilePath { get; set; }
    public static event Action<LogLv, string>? Written;

    public static void Write(LogLv lv, string msg)
    {
        if (lv < Min) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{lv}] {msg}";
        lock (Gate)
        {
            if (ToConsole) Console.WriteLine(line);
            if (FilePath != null)
            {
                try { File.AppendAllText(FilePath, line + Environment.NewLine); }
                catch (IOException) { /* ログ失敗で配信を止めない */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        Written?.Invoke(lv, line);
    }

    public static void Debug(string m) => Write(LogLv.Debug, m);
    public static void Info(string m) => Write(LogLv.Info, m);
    public static void Warn(string m) => Write(LogLv.Warn, m);
    public static void Error(string m) => Write(LogLv.Error, m);
}
