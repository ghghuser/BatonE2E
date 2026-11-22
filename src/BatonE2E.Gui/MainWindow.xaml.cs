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
        rbLog.Content = Loc.Get("gui.tab.log");
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

        lblLang.Text = Loc.Get("gui.set.language");
        lblSrcPort.Text = Loc.Get("gui.set.srcport");
        lblViewPort.Text = Loc.Get("gui.set.viewport");
        lblMount.Text = Loc.Get("gui.set.mount");
        lblSrcPass.Text = Loc.Get("gui.set.srcpass");
        lblViewPass.Text = Loc.Get("gui.set.viewpass");
        lblPassNote.Text = Loc.Get("gui.set.passnote");
        lblPageTitle.Text = Loc.Get("gui.set.title");
        lblMaxViewers.Text = Loc.Get("gui.set.maxviewers");
        lblTls.Text = Loc.Get("gui.set.tls");
        rbTlsAuto.Content = Loc.Get("gui.tls.auto");
        rbTlsOn.Content = Loc.Get("gui.tls.on");
        rbTlsOff.Content = Loc.Get("gui.tls.off");
        chkAnyUser.Content = Loc.Get("gui.set.anyuser");
        chkLogFile.Content = Loc.Get("gui.set.logfile");
        btnGenSrc.Content = Loc.Get("gui.set.gen");
        btnGenView.Content = Loc.Get("gui.set.gen");
        btnSave.Content = Loc.Get("gui.set.save");
        btnRestore.Content = Loc.Get("gui.set.restore");
        btnOpenFolder.Content = Loc.Get("gui.set.openfolder");

        // 操作
        rbMain.Checked += (_, _) => ShowPanel(0);
        rbLog.Checked += (_, _) => ShowPanel(1);
        rbSettings.Checked += (_, _) => ShowPanel(2);
        rbLogAll.Checked += (_, _) => RebuildLog();
        rbLogWarn.Checked += (_, _) => RebuildLog();
        btnStartStop.Click += OnStartStop;
        btnLogCopy.Click += (_, _) => CopyText(txtLog.Text);
        btnLogSave.Click += OnLogSave;
        btnLogClear.Click += (_, _) => { _logs.Clear(); txtLog.Clear(); tbLogCount.Text = Loc.Get("gui.log.lines", 0); };
        btnGenSrc.Click += (_, _) => tbSrcPass.Text = PassphraseGenerator.Generate(4, 4);
        btnGenView.Click += (_, _) => tbViewPass.Text = PassphraseGenerator.Generate();
        btnSave.Click += OnSave;
        btnRestore.Click += OnRestore;
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
        base.OnClosing(e);
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
        pLog.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        pSettings.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
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
        if (err != null) { tbError.Text = Loc.Get(err); return; }

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

        _vSrvServer.Text = "127.0.0.1";
        _vSrvPort.Text = o.SourcePort.ToString();
        _vSrvMount.Text = o.Mount;
        _vSrvUser.Text = "source";
        _vSrvPass.Text = o.SourcePassword;
        _vSrvTls.Text = Loc.Get(o.SourceTls switch { SourceTlsMode.Off => "gui.tls.off", SourceTlsMode.On => "gui.tls.on", _ => "gui.tls.auto" });
        _vSrvFp.Text = eng.SourceCertFingerprintColon ?? "—";
        _vViewUrl.Text = $"http://127.0.0.1:{o.ViewPort}/";
        _vViewPass.Text = o.ViewPassword;
        tbRegen.Text = eng.SourceCertRegenerated ? Loc.Get("gui.hint.regen") : "";
        tbHint.Text = "";
        btnStartStop.Content = Loc.Get("gui.btn.stop");
        AppLog.Info(Loc.Get("gui.log.started"));
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

    // ---------- 設定 ----------

    private void LoadUi(AppSettings s)
    {
        rbLangJa.IsChecked = s.Language == "ja";
        rbLangEn.IsChecked = s.Language == "en";
        tbSrcPort.Text = s.SourcePort.ToString();
        tbViewPort.Text = s.ViewPort.ToString();
        tbMount.Text = s.Mount;
        tbSrcPass.Text = s.SourcePassword;
        tbViewPass.Text = s.ViewPassword;
        tbPageTitle.Text = s.PageTitle;
        tbMaxViewers.Text = s.MaxViewers.ToString();
        rbTlsAuto.IsChecked = s.SourceTls == "auto";
        rbTlsOn.IsChecked = s.SourceTls == "on";
        rbTlsOff.IsChecked = s.SourceTls == "off";
        chkAnyUser.IsChecked = s.AcceptAnySourceUser;
        chkLogFile.IsChecked = s.LogToFile;
    }

    private static int ParseInt(string text) => int.TryParse(text.Trim(), out var v) ? v : 0;

    private AppSettings ReadUi()
    {
        var s = new AppSettings
        {
            Language = rbLangEn.IsChecked == true ? "en" : "ja",
            SourcePort = ParseInt(tbSrcPort.Text),
            ViewPort = ParseInt(tbViewPort.Text),
            Mount = tbMount.Text,
            SourcePassword = tbSrcPass.Text,
            ViewPassword = tbViewPass.Text,
            PageTitle = tbPageTitle.Text,
            MaxViewers = ParseInt(tbMaxViewers.Text),
            SourceTls = rbTlsOn.IsChecked == true ? "on" : rbTlsOff.IsChecked == true ? "off" : "auto",
            AcceptAnySourceUser = chkAnyUser.IsChecked == true,
            LogToFile = chkLogFile.IsChecked == true,
        };
        s.Normalize();
        return s;
    }

    private void SetMsg(string text) => tbSetMsg.Text = text;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = ReadUi();
        var err = s.Check();
        if (err != null) { SetMsg(Loc.Get(err)); return; }
        try { s.Save(App.ConfigPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMsg(Loc.Get("log.error", ex.Message));
            return;
        }
        var langChanged = s.Language != Loc.Language;
        _set = s;
        LoadUi(s);
        AppLog.FilePath = s.LogToFile ? Path.Combine(AppContext.BaseDirectory, "BatonE2E.log") : null;
        SetMsg(Loc.Get("gui.set.saved") + (langChanged ? "\n" + Loc.Get("gui.set.restart") : ""));
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var b = AppSettings.LoadLatestBackup(App.ConfigPath);
        if (b == null) { SetMsg(Loc.Get("gui.set.nobackup")); return; }
        LoadUi(b);
        SetMsg(Loc.Get("gui.set.restored"));
    }
}
