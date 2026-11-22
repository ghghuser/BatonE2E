using System.Runtime.InteropServices;
using System.Windows;
using BatonE2E.Core;
using BatonE2E.Core.Logging;

namespace BatonE2E.Gui;

/// <summary>タスクトレイ（通知領域）のアイコン。最小化で格納、状態を色で表示、右クリックで開く・開始/停止・終了。</summary>
public partial class MainWindow
{
    private enum TrayState { Stopped, Waiting, Live }

    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _miStartStop;
    private readonly Dictionary<TrayState, System.Drawing.Icon> _trayIcons = new();
    private TrayState? _trayShown;
    private WindowState _stateBeforeMin = WindowState.Normal;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private void InitTray()
    {
        try
        {
            _trayIcons[TrayState.Stopped] = MakeIcon(System.Drawing.Color.FromArgb(0x9A, 0xA0, 0xA6));
            _trayIcons[TrayState.Waiting] = MakeIcon(System.Drawing.Color.FromArgb(0xF0, 0xB2, 0x32));
            _trayIcons[TrayState.Live] = MakeIcon(System.Drawing.Color.FromArgb(0x3B, 0xA5, 0x5D));

            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem(Loc.Get("gui.tray.open")) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
            open.Click += (_, _) => RestoreFromTray();
            _miStartStop = new System.Windows.Forms.ToolStripMenuItem();
            _miStartStop.Click += (_, _) => TrayStartStop();
            var exit = new System.Windows.Forms.ToolStripMenuItem(Loc.Get("gui.tray.exit"));
            exit.Click += (_, _) => { RestoreFromTray(); Close(); };
            menu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { open, _miStartStop, new System.Windows.Forms.ToolStripSeparator(), exit });

            _tray = new System.Windows.Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
            _tray.DoubleClick += (_, _) => RestoreFromTray();
            UpdateTray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException)
        {
            // トレイが使えなくても本体は動かす（最小化は通常のタスクバーへ）
            AppLog.Warn(Loc.Get("gui.tray.err", ex.Message));
            DisposeTray();
        }
    }

    /// <summary>状態を表す丸いアイコンを描く（外部ファイルなし）。</summary>
    private static System.Drawing.Icon MakeIcon(System.Drawing.Color c)
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new System.Drawing.SolidBrush(c);
            using var rim = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x1E, 0x1F, 0x22), 3);
            g.FillEllipse(fill, 3, 3, 26, 26);
            g.DrawEllipse(rim, 3, 3, 26, 26);
        }
        var h = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    private TrayState CurrentTrayState()
    {
        var eng = _engine;
        if (eng == null) return TrayState.Stopped;
        return eng.Stats.SourceActive ? TrayState.Live : TrayState.Waiting;
    }

    private void UpdateTray()
    {
        if (_tray == null) return;
        var st = CurrentTrayState();
        if (_trayShown != st)
        {
            _tray.Icon = _trayIcons[st];
            _trayShown = st;
        }
        if (_miStartStop != null) _miStartStop.Text = Loc.Get(_engine == null ? "gui.btn.start" : "gui.btn.stop");
        var text = AppInfo.Name + " - " + Loc.Get(st switch { TrayState.Live => "gui.state.live", TrayState.Waiting => "gui.state.running", _ => "gui.state.stopped" });
        if (st != TrayState.Stopped) text += "\n" + Loc.Get("gui.stat.viewers", _engine?.Hub?.ViewerCount ?? 0);
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    private async void TrayStartStop()
    {
        await ToggleAsync();
        // ウィンドウが隠れていると画面の赤文字が見えないため、失敗は吹き出しでも知らせる
        if (!IsVisible && tbError.Text.Length > 0)
            _tray?.ShowBalloonTip(5000, AppInfo.Name, tbError.Text, System.Windows.Forms.ToolTipIcon.Error);
    }

    private void RestoreFromTray()
    {
        if (!IsVisible) Show();
        WindowState = _stateBeforeMin == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Minimized) _stateBeforeMin = WindowState;
        else if (_tray != null && _set.MinimizeToTray) Hide();
    }

    private void DisposeTray()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        foreach (var i in _trayIcons.Values) i.Dispose();
        _trayIcons.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeTray();
        base.OnClosed(e);
    }
}
