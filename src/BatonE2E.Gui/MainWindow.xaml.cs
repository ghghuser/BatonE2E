using System.IO;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BatonE2E.Core;
using BatonE2E.Core.Config;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;
using BatonE2E.Core.Security;
using Microsoft.Win32;

namespace BatonE2E.Gui;

public partial class MainWindow : Window
{
    private const int MaxLogLines = 5000;

    private AppSettings _set;
    private RelayEngine? _engine;
    private readonly ConcurrentQueue<(LogLv Lv, string Line)> _pending = new();
    private readonly List<(LogLv Lv, string Line)> _logs = new();
    private readonly DispatcherTimer _statTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _logTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Brush _okBrush, _errBrush, _textBrush;

    private readonly TextBox _vSrvServer, _vSrvPort, _vSrvMount, _vSrvUser, _vSrvPass, _vSrvTls, _vSrvFp, _vViewUrl, _vViewPass;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow(AppSettings settings, bool settingsCorrupted)
    {
        _set = settings;
        InitializeComponent();
        _okBrush = (Brush)FindResource("OkBrush");
        _errBrush = (Brush)FindResource("ErrBrush");
        _textBrush = (Brush)FindResource("TextBrush");
        Title = AppInfo.Title;

        AppLog.Written += (lv, line) => _pending.Enqueue((lv, line));

        // 文言
        rbMain.Content = Loc.Get("gui.tab.main");
        tglLog.Content = Loc.Get("gui.tab.log");
        rbSettings.Content = Loc.Get("gui.tab.settings");
        tbBoxSource.Text = Loc.Get("gui.box.source");
        tbBoxView.Text = Loc.Get("gui.box.view");
        tbTunnel.Text = Loc.Get("gui.hint.tunnel");
        _vSrvServer = AddRow(gSource, "gui.lbl.server");
        _vSrvPort = AddRow(gSource, "gui.lbl.port");
        _vSrvMount = AddRow(gSource, "gui.lbl.mount");
        _vSrvUser = AddRow(gSource, "gui.lbl.user");
        _vSrvPass = AddRow(gSource, "gui.lbl.password");
        _vSrvTls = AddRow(gSource, "gui.lbl.tls");
        _vSrvFp = AddRow(gSource, "gui.lbl.fp");
        _vViewUrl = AddRow(gView, "gui.lbl.viewurl");
        _vViewPass = AddRow(gView, "gui.lbl.viewpass");

        rbLogAll.Content = Loc.Get("gui.log.all");
        rbLogWarn.Content = Loc.Get("gui.log.warn");
        btnLogCopy.Content = Loc.Get("gui.log.copy");
        btnLogSave.Content = Loc.Get("gui.log.save");
        btnLogClear.Content = Loc.Get("gui.log.clear");

        BuildSettingsForm();

        // 操作
        rbMain.Checked += (_, _) => ShowPanel(0);
        rbSettings.Checked += (_, _) => ShowPanel(1);
        tglLog.Checked += (_, _) => SetLogOpen(true);
        tglLog.Unchecked += (_, _) => SetLogOpen(false);
        rbLogAll.Checked += (_, _) => RebuildLog();
        rbLogWarn.Checked += (_, _) => RebuildLog();
        btnStartStop.Click += OnStartStop;
        btnLogCopy.Click += (_, _) => CopyText(txtLog.Text);
        btnLogSave.Click += OnLogSave;
        btnLogClear.Click += (_, _) => { _logs.Clear(); txtLog.Clear(); tbLogCount.Text = Loc.Get("gui.log.lines", 0); };
        btnOpenFolder.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { SetMsg(ex.Message); }
        };
        _statTimer.Tick += (_, _) => UpdateStats();
        _logTimer.Tick += (_, _) => FlushLogs();
        _statTimer.Start();
        _logTimer.Start();

        LoadUi(_set);
        UpdateSecBanner();
        RestoreUiState();
        ShowStoppedValues();
        UpdateStats();
        tbLogCount.Text = Loc.Get("gui.log.lines", 0);
        if (settingsCorrupted)
        {
            AppLog.Warn(Loc.Get("gui.set.corrupt"));
            SetMsg(Loc.Get("gui.set.corrupt"));
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            var on = 1;
            DwmSetWindowAttribute(h, 20, ref on, sizeof(int)); // タイトルバーをダークに（対応OSのみ）
        }
        catch { /* 見た目だけの機能なので無視 */ }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_engine != null)
        {
            if (MessageBox.Show(Loc.Get("gui.confirm.close"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            var eng = _engine;
            _engine = null;
            try { Task.Run(async () => await eng.DisposeAsync()).Wait(5000); } catch { /* 終了時の失敗は無視 */ }
        }
        SaveUiState();
        base.OnClosing(e);
    }

    // ---------- ウィンドウ位置・大きさの保存と復元（window.json） ----------

    private sealed class UiState
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
        public bool LogOpen { get; set; } = true;
    }

    private static string UiStatePath => Path.Combine(AppContext.BaseDirectory, "window.json");

    private void RestoreUiState()
    {
        try
        {
            if (!File.Exists(UiStatePath)) return;
            var u = System.Text.Json.JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiStatePath));
            if (u == null) return;
            if (u.Width >= MinWidth && u.Height >= MinHeight && u.Width < 20000 && u.Height < 20000)
            {
                // 画面（複数モニター含む）から外れていたら位置は復元しない
                var vl = SystemParameters.VirtualScreenLeft;
                var vt = SystemParameters.VirtualScreenTop;
                var vr = vl + SystemParameters.VirtualScreenWidth;
                var vb = vt + SystemParameters.VirtualScreenHeight;
                var visible = u.Left + 100 < vr && u.Left + u.Width - 100 > vl && u.Top < vb - 50 && u.Top + 50 > vt;
                Width = Math.Min(u.Width, SystemParameters.VirtualScreenWidth);
                Height = Math.Min(u.Height, SystemParameters.VirtualScreenHeight);
                if (visible)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = u.Left;
                    Top = u.Top;
                }
            }
            if (u.Maximized) WindowState = WindowState.Maximized;
            _suppressLog = true;
            tglLog.IsChecked = u.LogOpen;
            ApplyLogOpenInstant(u.LogOpen);
            _suppressLog = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            /* 復元できなくても既定の大きさで起動 */
        }
    }

    private void SaveUiState()
    {
        try
        {
            var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var u = new UiState
            {
                Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height,
                Maximized = WindowState == WindowState.Maximized,
                LogOpen = tglLog.IsChecked == true,
            };
            File.WriteAllText(UiStatePath, System.Text.Json.JsonSerializer.Serialize(u));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            /* 保存できなくても終了は続行 */
        }
    }

    // ---------- 共通部品 ----------

    private TextBox AddRow(Grid g, string labelKey)
    {
        var r = g.RowDefinitions.Count;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lbl = new TextBlock { Text = Loc.Get(labelKey), Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 3, 8, 3) };
        Grid.SetRow(lbl, r);
        g.Children.Add(lbl);
        var tb = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 3, 6, 3) };
        Grid.SetRow(tb, r);
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        var b = new Button { Content = Loc.Get("gui.btn.copy"), Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(10, 3, 10, 3) };
        b.Click += (_, _) => CopyText(tb.Text);
        Grid.SetRow(b, r);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        return tb;
    }

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) when (ex is COMException or System.Runtime.InteropServices.ExternalException) { /* 他のアプリがクリップボードを使用中 */ }
    }

    private void ShowPanel(int i)
    {
        pMain.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        pSettings.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private const double LogWidth = 410;
    private bool _suppressLog;
    private int _logAnim;

    /// <summary>ログを右側へ引き出す／しまう。ウィンドウ自体が右へ広がる・縮む（最大化中は内側で開閉）。</summary>
    private async void SetLogOpen(bool open)
    {
        if (colLog == null || _suppressLog) return;
        var id = ++_logAnim;
        var from = colLog.Width.Value;
        var to = open ? LogWidth : 0;
        colLog.MinWidth = 0;
        pLog.Visibility = Visibility.Visible;
        if (open)
        {
            // 画面の右端からはみ出すなら、先に左へ寄せる
            var wa = SystemParameters.WorkArea;
            var need = Width + (to - from);
            if (WindowState == WindowState.Normal && Left + need > wa.Right) Left = Math.Max(wa.Left, wa.Right - need);
        }
        var grow = WindowState == WindowState.Normal;
        var baseWidth = Width - from;
        const int steps = 10;
        for (var i = 1; i <= steps; i++)
        {
            if (id != _logAnim) return;
            var t = i / (double)steps;
            var v = from + (to - from) * (1 - Math.Pow(1 - t, 3));
            colLog.Width = new GridLength(v);
            if (grow) Width = baseWidth + v;
            await Task.Delay(12);
        }
        if (id != _logAnim) return;
        colLog.Width = new GridLength(to);
        if (grow) Width = baseWidth + to;
        if (!open) pLog.Visibility = Visibility.Collapsed;
    }

    private void ApplyLogOpenInstant(bool open)
    {
        colLog.Width = new GridLength(open ? LogWidth : 0);
        pLog.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 開始・停止 ----------

    private async void OnStartStop(object sender, RoutedEventArgs e)
    {
        btnStartStop.IsEnabled = false;
        try
        {
            if (_engine == null) await StartAsync();
            else await StopAsync();
        }
        finally { btnStartStop.IsEnabled = true; }
    }

    private async Task StartAsync()
    {
        tbError.Text = "";
        var err = _set.Check();
        if (err != null) { tbError.Text = IssueText(err); return; }

        tbState.Text = Loc.Get("gui.state.starting");
        var o = _set.ToEngineOptions();
        var eng = new RelayEngine(o);
        try { await eng.StartAsync(); }
        catch (Exception ex)
        {
            var m = Loc.Get("gui.err.start", ex.Message);
            AppLog.Error(m);
            tbError.Text = m;
            await eng.DisposeAsync();
            UpdateStats();
            return;
        }
        _engine = eng;

        _vSrvServer.Text = o.SourceBind == "0.0.0.0" ? Loc.Get("gui.val.anyaddr") : o.SourceBind;
        _vSrvPort.Text = o.SourcePort.ToString();
        _vSrvMount.Text = o.Mount;
        _vSrvUser.Text = "source";
        _vSrvPass.Text = o.SourcePassword;
        _vSrvTls.Text = Loc.Get(o.SourceTls switch { SourceTlsMode.Off => "gui.tls.off", SourceTlsMode.On => "gui.tls.on", _ => "gui.tls.auto" });
        _vSrvFp.Text = eng.SourceCertFingerprintColon ?? "—";
        _vViewUrl.Text = $"http://127.0.0.1:{o.ViewPort}{o.ViewPath}";
        _vViewPass.Text = o.ViewPassword;
        tbRegen.Text = eng.SourceCertRegenerated ? Loc.Get("gui.hint.regen") : "";
        tbHint.Text = "";
        btnStartStop.Content = Loc.Get("gui.btn.stop");
        AppLog.Info(Loc.Get("gui.log.started"));
        foreach (var w in _set.SecurityWarnings()) AppLog.Warn(Loc.Get("gui.sec.log", Loc.Get(w)));
        UpdateStats();
    }

    private async Task StopAsync()
    {
        var eng = _engine;
        _engine = null;
        if (eng != null) await eng.DisposeAsync();
        AppLog.Info(Loc.Get("gui.log.stopped"));
        ShowStoppedValues();
        UpdateStats();
    }

    private void ShowStoppedValues()
    {
        foreach (var t in new[] { _vSrvServer, _vSrvPort, _vSrvMount, _vSrvUser, _vSrvPass, _vSrvTls, _vSrvFp, _vViewUrl, _vViewPass }) t.Text = "—";
        tbHint.Text = Loc.Get("gui.hint.stopped");
        tbRegen.Text = "";
        btnStartStop.Content = Loc.Get("gui.btn.start");
    }

    private void UpdateStats()
    {
        var eng = _engine;
        if (eng == null)
        {
            tbState.Text = Loc.Get("gui.state.stopped");
            tbState.Foreground = _textBrush;
            tbSource.Text = "";
            tbStats.Text = "";
            tbEnc.Text = "";
            return;
        }
        var st = eng.Stats;
        var live = st.SourceActive;
        tbState.Text = Loc.Get(live ? "gui.state.live" : "gui.state.running");
        tbState.Foreground = live ? _okBrush : _textBrush;
        tbSource.Text = live ? Loc.Get("gui.src.active", st.SourceMime) : Loc.Get("gui.src.waiting");
        tbStats.Text = Loc.Get("gui.stat.viewers", eng.Hub?.ViewerCount ?? 0) + "    "
                       + Loc.Get("gui.stat.bytes", st.BytesIn / 1024) + "    "
                       + Loc.Get("gui.stat.frames", st.Frames);
        if (st.CheckNg > 0) { tbEnc.Text = Loc.Get("gui.enc.ng", st.CheckNg); tbEnc.Foreground = _errBrush; }
        else if (st.Frames == 0) { tbEnc.Text = Loc.Get("gui.enc.idle"); tbEnc.Foreground = _textBrush; }
        else { tbEnc.Text = Loc.Get("gui.enc.ok", st.CheckOk); tbEnc.Foreground = _okBrush; }
    }

    // ---------- ログ ----------

    private bool PassFilter(LogLv lv) => rbLogWarn.IsChecked != true || lv >= LogLv.Warn;

    private void FlushLogs()
    {
        if (_pending.IsEmpty) return;
        var added = new List<(LogLv Lv, string Line)>();
        while (_pending.TryDequeue(out var x)) added.Add(x);
        _logs.AddRange(added);
        if (_logs.Count > MaxLogLines)
        {
            _logs.RemoveRange(0, _logs.Count - MaxLogLines + 1000);
            RebuildLog();
            return;
        }
        var show = added.Where(x => PassFilter(x.Lv)).Select(x => x.Line).ToList();
        if (show.Count > 0)
        {
            var atEnd = txtLog.VerticalOffset + txtLog.ViewportHeight >= txtLog.ExtentHeight - 2;
            txtLog.AppendText(string.Join("\r\n", show) + "\r\n");
            if (atEnd) txtLog.ScrollToEnd();
        }
        tbLogCount.Text = Loc.Get("gui.log.lines", _logs.Count);
    }

    private void RebuildLog()
    {
        if (txtLog == null) return;
        var lines = _logs.Where(x => PassFilter(x.Lv)).Select(x => x.Line).ToList();
        txtLog.Text = lines.Count == 0 ? "" : string.Join("\r\n", lines) + "\r\n";
        txtLog.ScrollToEnd();
        tbLogCount.Text = Loc.Get("gui.log.lines", _logs.Count);
    }

    private void OnLogSave(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"BatonE2E-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = "Text (*.txt)|*.txt",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllLines(dlg.FileName, _logs.Select(x => x.Line));
            AppLog.Info(Loc.Get("gui.log.saved", dlg.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error(Loc.Get("log.error", ex.Message));
        }
    }
}
