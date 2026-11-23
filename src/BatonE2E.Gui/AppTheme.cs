using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace BatonE2E.Gui;

/// <summary>画面の配色（自動=OSに合わせる / ライト / ダーク）。App.xamlの色のブラシの中身を書き換えるので、画面は即座に変わる。</summary>
public static class AppTheme
{
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;

    private static string _setting = "auto";
    private static bool _hooked;

    // キー: (ダーク, ライト)。ダークの強調色は青みがかった深緑。
    private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
    {
        ["BgBrush"] = ("#1E1F22", "#E9EDEC"),
        ["PanelBrush"] = ("#2B2D31", "#FFFFFF"),
        ["InputBrush"] = ("#17181A", "#FFFFFF"),
        ["CardBrush"] = ("#33363C", "#F0F4F3"),
        ["LineBrush"] = ("#3F4147", "#CBD3D1"),
        ["TextBrush"] = ("#E3E5E8", "#1C2321"),
        ["MutedBrush"] = ("#9AA0A6", "#5B6764"),
        ["AccentBrush"] = ("#19786B", "#1B8272"),
        ["AccentHoverBrush"] = ("#2BA48D", "#0F6153"),
        ["BtnBrush"] = ("#3A3C42", "#DCE3E1"),
        ["BtnHoverBrush"] = ("#4A4D55", "#C9D2CF"),
        ["OkBrush"] = ("#3BA55D", "#1E7F43"),
        ["WarnBrush"] = ("#F0B232", "#A86A00"),
        ["ErrBrush"] = ("#ED4245", "#C62828"),
        ["ThumbBrush"] = ("#4F525A", "#B3BEBB"),
        ["ThumbHoverBrush"] = ("#6B6F79", "#93A19D"),
    };

    /// <summary>設定値（auto/light/dark）を適用する。autoのときはOSの変更にも追従する。</summary>
    public static void Apply(string setting)
    {
        _setting = setting is "light" or "dark" ? setting : "auto";
        if (!_hooked)
        {
            _hooked = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (_setting == "auto" && e.Category == UserPreferenceCategory.General)
                    Application.Current?.Dispatcher.BeginInvoke(() => Apply("auto"));
            };
        }
        var dark = _setting == "dark" || (_setting == "auto" && OsIsDark());
        var res = Application.Current.Resources;
        foreach (var (key, pair) in Palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(dark ? pair.Dark : pair.Light);
            if (res[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c;
            else res[key] = new SolidColorBrush(c);
        }
        IsDark = dark;
        Changed?.Invoke();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>タイトルバーをテーマに合わせる（ダーク/ライト。対応OSのみ）。</summary>
    public static void SetTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            var v = IsDark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref v, sizeof(int));
        }
        catch { /* 見た目だけの機能なので無視 */ }
    }

    /// <summary>OSのアプリ用テーマがダークか。読めなければダーク。</summary>
    private static bool OsIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is not int v || v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}
