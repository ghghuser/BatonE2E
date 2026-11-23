using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace BatonE2E.Gui;

/// <summary>はい／いいえの確認ダイアログ（暗いテーマ）。標準のMessageBoxは警告アイコンで警告音が鳴るため、
/// 音が必要な「重大なエラー」（クラッシュ報告）以外の確認はこちらを使う。既定は「いいえ」。</summary>
public static class ConfirmDialog
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool Ask(Window? owner, string text, string title, bool warning)
    {
        var res = (Brush)Application.Current.FindResource("PanelBrush");
        var win = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Background = res, MaxWidth = 560, MinWidth = 340, FontFamily = owner?.FontFamily ?? new FontFamily("Yu Gothic UI, Segoe UI"), FontSize = 13,
        };
        if (owner != null && owner.IsVisible) { win.Owner = owner; win.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var yes = new Button { Content = Loc("gui.dlg.yes"), MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
        var no = new Button { Content = Loc("gui.dlg.no"), MinWidth = 84, IsDefault = true, IsCancel = true };
        var result = false;
        yes.Click += (_, _) => { result = true; win.Close(); };
        no.Click += (_, _) => { result = false; win.Close(); };

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock
        {
            Text = warning ? "⚠" : "？", FontSize = 30, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top,
            Foreground = (Brush)Application.Current.FindResource(warning ? "WarnBrush" : "AccentHoverBrush"),
        });
        head.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, VerticalAlignment = VerticalAlignment.Center });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(head);
        root.Children.Add(buttons);
        win.Content = root;

        win.SourceInitialized += (_, _) =>
        {
            try { var on = 1; DwmSetWindowAttribute(new WindowInteropHelper(win).Handle, 20, ref on, sizeof(int)); }
            catch { /* 見た目だけの機能なので無視 */ }
        };
        win.ContentRendered += (_, _) => no.Focus();
        win.ShowDialog();
        return result;
    }

    private static string Loc(string key) => BatonE2E.Core.Loc.Get(key);
}
