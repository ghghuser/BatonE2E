using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BatonE2E.Core;
using BatonE2E.Core.Config;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;
using BatonE2E.Core.Security;

namespace BatonE2E.Gui;

/// <summary>設定画面（見出し：入力欄の形式。詳細はⓘのツールチップ）。</summary>
public partial class MainWindow
{
    private enum Msg { Muted, Ok, Err, Warn }

    private TextBox tbSrcBind = null!, tbSrcPort = null!, tbViewPort = null!, tbViewPath = null!, tbMount = null!, tbPageTitle = null!,
        tbMaxViewers = null!, tbSrcPass = null!, tbViewPass = null!, tbConnLimit = null!, tbRateWin = null!, tbRateMax = null!,
        tbRateBan = null!, tbDeny = null!;
    private RadioButton rbLangJa = null!, rbLangEn = null!, rbTlsAuto = null!, rbTlsOn = null!, rbTlsOff = null!;
    private CheckBox chkAnyUser = null!, chkProxy = null!, chkLogFile = null!;
    private Button btnGenSrc = null!, btnGenView = null!;
    private int _setRow;
    private bool _loading;
    private readonly Dictionary<string, Control[]> _fields = new();

    // ---------- 組み立て ----------

    private void BuildSettingsForm()
    {
        gSet.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        gSet.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Group("gui.grp.basic");
        rbLangJa = Choice("日本語", "lang");
        rbLangEn = Choice("English", "lang");
        Row("gui.set.language", HPanel(rbLangJa, rbLangEn), "gui.tip.language");

        Group("gui.grp.net");
        tbSrcBind = Txt(200);
        Row("gui.set.srcbind", tbSrcBind, "gui.tip.srcbind", "SourceBind", EngineOptions.MinExternalSourcePasswordLength);
        tbSrcPort = Txt(100);
        Row("gui.set.srcport", tbSrcPort, "gui.tip.srcport", "SourcePort");
        tbViewPort = Txt(100);
        Row("gui.set.viewport", tbViewPort, "gui.tip.viewport", "ViewPort");
        tbViewPath = Txt(340);
        Row("gui.set.viewpath", tbViewPath, "gui.tip.viewpath", "ViewPath");
        tbMount = Txt(340);
        Row("gui.set.mount", tbMount, "gui.tip.mount", "Mount");
        tbPageTitle = Txt(340);
        Row("gui.set.title", tbPageTitle, "gui.tip.title", "PageTitle");
        tbMaxViewers = Txt(100);
        Row("gui.set.maxviewers", tbMaxViewers, "gui.tip.maxviewers", "MaxViewers");

        Group("gui.grp.pass");
        tbSrcPass = Txt(340);
        btnGenSrc = new Button { Content = Loc.Get("gui.set.gen"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        btnGenSrc.Click += (_, _) => tbSrcPass.Text = PassphraseGenerator.Generate(4, 4);
        Row("gui.set.srcpass", HPanel(tbSrcPass, btnGenSrc), "gui.tip.srcpass", "SourcePassword");
        tbViewPass = Txt(340);
        btnGenView = new Button { Content = Loc.Get("gui.set.gen"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        btnGenView.Click += (_, _) => tbViewPass.Text = PassphraseGenerator.Generate();
        Row("gui.set.viewpass", HPanel(tbViewPass, btnGenView), "gui.tip.viewpass", "ViewPassword", AppSettings.MinViewPasswordLength, AppSettings.RecommendedViewPasswordLength);

        Group("gui.grp.sec");
        rbTlsAuto = Choice(Loc.Get("gui.tls.auto"), "tls");
        rbTlsOn = Choice(Loc.Get("gui.tls.on"), "tls");
        rbTlsOff = Choice(Loc.Get("gui.tls.off"), "tls");
        Row("gui.set.tls", HPanel(rbTlsAuto, rbTlsOn, rbTlsOff), "gui.tip.tls", "SourceTls");
        chkAnyUser = Check(Loc.Get("gui.set.anyuser"));
        Row("", chkAnyUser, "gui.tip.anyuser");

        Group("gui.grp.access");
        tbConnLimit = Txt(100);
        Row("gui.set.connlimit", tbConnLimit, "gui.tip.connlimit", "MaxConnPerClient");
        tbRateWin = Txt(60); tbRateMax = Txt(60); tbRateBan = Txt(60);
        var ratePanel = HPanel(tbRateWin, Small(Loc.Get("gui.rate.p1")), tbRateMax, Small(Loc.Get("gui.rate.p2")), tbRateBan, Small(Loc.Get("gui.rate.p3")));
        Row("gui.set.rate", ratePanel, "gui.tip.rate", "RateWindowSec,RateMaxRequests,RateBanSec");
        chkProxy = Check(Loc.Get("gui.set.proxy"));
        Row("", chkProxy, "gui.tip.proxy");
        tbDeny = Txt(420);
        tbDeny.AcceptsReturn = true;
        tbDeny.TextWrapping = TextWrapping.NoWrap;
        tbDeny.Height = 110;
        tbDeny.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        tbDeny.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        tbDeny.VerticalContentAlignment = VerticalAlignment.Top;
        tbDeny.FontFamily = new FontFamily("Consolas, MS Gothic");
        Row("gui.set.deny", tbDeny, "gui.tip.deny", "DenyRules");

        Group("gui.grp.other");
        chkLogFile = Check(Loc.Get("gui.set.logfile"));
        Row("", chkLogFile, "gui.tip.logfile");

        btnSave.Content = Loc.Get("gui.set.save");
        btnRestore.Content = Loc.Get("gui.set.restore");
        btnTemplate.Content = Loc.Get("gui.set.template");
        btnTemplate.ToolTip = new ToolTip { Content = new TextBlock { Text = Loc.Get("gui.tip.template"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 } };
        ToolTipService.SetShowDuration(btnTemplate, 60000);
        btnOpenFolder.Content = Loc.Get("gui.set.openfolder");
        tbDirty.Text = Loc.Get("gui.set.dirty");
        btnSave.Click += OnSave;
        btnRestore.Click += OnRestore;
        btnTemplate.Click += OnTemplate;
    }

    private void Group(string key)
    {
        gSet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var b = new Border
        {
            BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, _setRow == 0 ? 0 : 16, 0, 4), Padding = new Thickness(0, 0, 0, 4),
            Child = new TextBlock { Text = Loc.Get(key), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentHoverBrush") },
        };
        Grid.SetRow(b, _setRow++);
        Grid.SetColumnSpan(b, 2);
        gSet.Children.Add(b);
    }

    /// <summary>見出し（右寄せ）：入力欄（左揃えで頭をそろえる）＋ⓘ。</summary>
    private void Row(string labelKey, FrameworkElement input, string? tipKey, string? field = null, params object[] tipArgs)
    {
        gSet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var r = _setRow++;
        var lbl = new TextBlock
        {
            Text = labelKey.Length > 0 ? Loc.Get(labelKey) : "", TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4),
        };
        Grid.SetRow(lbl, r);
        gSet.Children.Add(lbl);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        sp.Children.Add(input);
        if (tipKey != null) sp.Children.Add(Info(tipKey, tipArgs));
        Grid.SetRow(sp, r);
        Grid.SetColumn(sp, 1);
        gSet.Children.Add(sp);
        if (field != null)
        {
            var marks = new List<Control>();
            Collect(input, marks);
            foreach (var f in field.Split(',')) _fields[f] = marks.ToArray();
        }
    }

    private static void Collect(DependencyObject o, List<Control> into)
    {
        if (o is TextBox t) into.Add(t);
        else if (o is Panel p) foreach (var c in p.Children.OfType<DependencyObject>()) Collect(c, into);
    }

    private UIElement Info(string tipKey, object[] args)
    {
        var tb = new TextBlock
        {
            Text = "ⓘ", FontSize = 15, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Help, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = new ToolTip { Content = new TextBlock { Text = Loc.Get(tipKey, args), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 } },
        };
        ToolTipService.SetInitialShowDelay(tb, 100);
        ToolTipService.SetShowDuration(tb, 60000);
        return tb;
    }

    private TextBox Txt(double width)
    {
        var t = new TextBox { Width = width };
        t.TextChanged += (_, _) => UpdateDirty();
        return t;
    }

    private TextBlock Small(string text) => new() { Text = text, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

    private RadioButton Choice(string text, string group)
    {
        var r = new RadioButton { Content = text, GroupName = group, Style = (Style)FindResource("ChoiceButton") };
        r.Checked += (_, _) => UpdateDirty();
        return r;
    }

    private CheckBox Check(string text)
    {
        var c = new CheckBox { Content = text, VerticalAlignment = VerticalAlignment.Center };
        c.Checked += (_, _) => UpdateDirty();
        c.Unchecked += (_, _) => UpdateDirty();
        return c;
    }

    private static StackPanel HPanel(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    // ---------- 値の出し入れ ----------

    private void LoadUi(AppSettings s)
    {
        _loading = true;
        rbLangJa.IsChecked = s.Language == "ja";
        rbLangEn.IsChecked = s.Language == "en";
        tbSrcBind.Text = s.SourceBind;
        tbSrcPort.Text = s.SourcePort.ToString();
        tbViewPort.Text = s.ViewPort.ToString();
        tbViewPath.Text = s.ViewPath;
        tbMount.Text = s.Mount;
        tbPageTitle.Text = s.PageTitle;
        tbMaxViewers.Text = s.MaxViewers.ToString();
        tbSrcPass.Text = s.SourcePassword;
        tbViewPass.Text = s.ViewPassword;
        rbTlsAuto.IsChecked = s.SourceTls == "auto";
        rbTlsOn.IsChecked = s.SourceTls == "on";
        rbTlsOff.IsChecked = s.SourceTls == "off";
        chkAnyUser.IsChecked = s.AcceptAnySourceUser;
        tbConnLimit.Text = s.MaxConnPerClient.ToString();
        tbRateWin.Text = s.RateWindowSec.ToString();
        tbRateMax.Text = s.RateMaxRequests.ToString();
        tbRateBan.Text = s.RateBanSec.ToString();
        chkProxy.IsChecked = s.UseProxyHeader;
        tbDeny.Text = s.DenyRules.Replace("\r", "").Replace("\n", "\r\n");
        chkLogFile.IsChecked = s.LogToFile;
        _loading = false;
        UpdateDirty();
    }

    /// <summary>画面の値から設定を作る。数値が不正なら issue を返す（黙って既定値にしない）。</summary>
    private AppSettings ReadUi(out AppSettings.Issue? issue)
    {
        AppSettings.Issue? iss = null;
        int Num(TextBox t, string field, string labelKey, int min, int max)
        {
            if (int.TryParse(t.Text.Trim(), out var v) && v >= min && v <= max) return v;
            iss ??= new AppSettings.Issue("gui.err.range", field, new object[] { Loc.Get(labelKey), min, max });
            return min;
        }
        var s = new AppSettings
        {
            Language = rbLangEn.IsChecked == true ? "en" : "ja",
            SourceBind = tbSrcBind.Text,
            SourcePort = Num(tbSrcPort, "SourcePort", "gui.set.srcport", 1, 65535),
            ViewPort = Num(tbViewPort, "ViewPort", "gui.set.viewport", 1, 65535),
            ViewPath = tbViewPath.Text,
            Mount = tbMount.Text,
            PageTitle = tbPageTitle.Text,
            MaxViewers = Num(tbMaxViewers, "MaxViewers", "gui.set.maxviewers", 1, 1000),
            SourcePassword = tbSrcPass.Text,
            ViewPassword = tbViewPass.Text,
            SourceTls = rbTlsOn.IsChecked == true ? "on" : rbTlsOff.IsChecked == true ? "off" : "auto",
            AcceptAnySourceUser = chkAnyUser.IsChecked == true,
            MaxConnPerClient = Num(tbConnLimit, "MaxConnPerClient", "gui.set.connlimit", 0, 1000),
            RateWindowSec = Num(tbRateWin, "RateWindowSec", "gui.set.rate", 0, 3600),
            RateMaxRequests = Num(tbRateMax, "RateMaxRequests", "gui.set.rate", 0, 100000),
            RateBanSec = Num(tbRateBan, "RateBanSec", "gui.set.rate", 0, 86400),
            UseProxyHeader = chkProxy.IsChecked == true,
            DenyRules = tbDeny.Text.Replace("\r", ""),
            LogToFile = chkLogFile.IsChecked == true,
        };
        s.Normalize();
        issue = iss;
        return s;
    }

    private void UpdateDirty()
    {
        if (_loading || tbDirty == null || _set == null) return;
        tbDirty.Visibility = ReadUi(out _).ToJson() != _set.ToJson() ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 表示 ----------

    private void SetMsg(string text, Msg kind = Msg.Muted)
    {
        tbSetMsg.Text = text;
        tbSetMsg.Foreground = kind switch
        {
            Msg.Ok => _okBrush,
            Msg.Err => _errBrush,
            Msg.Warn => (Brush)FindResource("WarnBrush"),
            _ => (Brush)FindResource("MutedBrush"),
        };
    }

    private static string IssueText(AppSettings.Issue i) => Loc.Get(i.Key, i.Args);

    private void ClearMarks()
    {
        var line = (Brush)FindResource("LineBrush");
        foreach (var arr in _fields.Values) foreach (var c in arr) c.BorderBrush = line;
    }

    private void ShowIssue(AppSettings.Issue i)
    {
        SetMsg(Loc.Get("gui.set.notsaved", IssueText(i)), Msg.Err);
        var first = true;
        foreach (var f in i.Field.Split(','))
        {
            if (!_fields.TryGetValue(f, out var arr)) continue;
            foreach (var c in arr)
            {
                c.BorderBrush = _errBrush;
                if (first) { c.BringIntoView(); first = false; }
            }
        }
    }

    /// <summary>保存済みの設定にセキュリティを下げる項目があれば、中継画面に常に警告を出す。</summary>
    private void UpdateSecBanner()
    {
        var w = _set.SecurityWarnings();
        tbSecWarn.Text = w.Count == 0 ? "" : Loc.Get("gui.sec.banner") + "\n" + string.Join("\n", w.Select(k => "・" + Loc.Get(k)));
    }

    // ---------- 操作 ----------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var ns = ReadUi(out var issue);
        issue ??= ns.Check();
        if (issue != null) { ShowIssue(issue); return; }

        // セキュリティが下がる設定に「変更した」ときは、必ず確認する
        var before = _set.SecurityWarnings();
        var added = ns.SecurityWarnings().Where(w => !before.Contains(w)).ToList();
        if (added.Count > 0)
        {
            var text = Loc.Get("gui.sec.confirm", string.Join("\n", added.Select(w => "・" + Loc.Get(w))));
            if (MessageBox.Show(this, text, Loc.Get("gui.sec.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                SetMsg(Loc.Get("gui.set.cancelled"), Msg.Warn);
                return;
            }
            foreach (var w in added) AppLog.Warn(Loc.Get("gui.sec.log", Loc.Get(w)));
        }

        try { ns.Save(App.ConfigPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMsg(Loc.Get("gui.set.notsaved", ex.Message), Msg.Err);
            return;
        }
        var langChanged = ns.Language != Loc.Language;
        _set = ns;
        LoadUi(ns);
        UpdateSecBanner();
        AppLog.FilePath = ns.LogToFile ? Path.Combine(AppContext.BaseDirectory, "BatonE2E.log") : null;
        AppLog.Info(Loc.Get("gui.set.saved.log"));
        var msg = Loc.Get(_engine != null ? "gui.set.saved.running" : "gui.set.saved", DateTime.Now.ToString("HH:mm:ss"));
        if (langChanged) msg += "\n" + Loc.Get("gui.set.restart");
        SetMsg(msg, Msg.Ok);
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var b = AppSettings.LoadLatestBackup(App.ConfigPath);
        if (b == null) { SetMsg(Loc.Get("gui.set.nobackup"), Msg.Warn); return; }
        LoadUi(b);
        SetMsg(Loc.Get("gui.set.restored"), Msg.Warn);
    }

    /// <summary>視聴ページのひな形（viewer.html / viewer.css）をexeの横へ出力。既存のファイルは上書きしない。</summary>
    private void OnTemplate(object sender, RoutedEventArgs e)
    {
        var dir = AppContext.BaseDirectory;
        var made = new List<string>();
        var kept = new List<string>();
        try
        {
            var html = Path.Combine(dir, "viewer.html");
            var css = Path.Combine(dir, "viewer.css");
            if (File.Exists(html)) kept.Add("viewer.html");
            else { File.WriteAllText(html, ViewerTemplate.ReadEmbedded(), new UTF8Encoding(false)); made.Add("viewer.html"); }
            if (File.Exists(css)) kept.Add("viewer.css");
            else { File.WriteAllText(css, ViewerTemplate.CssSample, new UTF8Encoding(false)); made.Add("viewer.css"); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMsg(Loc.Get("gui.set.notsaved", ex.Message), Msg.Err);
            return;
        }
        var text = made.Count > 0 ? Loc.Get("gui.set.template.done", string.Join(", ", made), dir) : "";
        if (kept.Count > 0) text += (text.Length > 0 ? "\n" : "") + Loc.Get("gui.set.template.exists", string.Join(", ", kept));
        SetMsg(text, made.Count > 0 ? Msg.Ok : Msg.Warn);
    }
}
