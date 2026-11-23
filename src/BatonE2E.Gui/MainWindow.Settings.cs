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
    private RadioButton rbTlsAuto = null!, rbTlsOn = null!, rbTlsOff = null!;
    private ComboBox cbLang = null!;
    private TextBox tbMirrorNo = null!, tbViewPort2 = null!, tbMaxViewers2 = null!;
    private TextBlock tbMirrorPreview = null!;
    private Grid _g = null!;        // いま項目を追加している先のフォーム（中継 / 鏡 / 設定）
    private int _curTab;            // その番号（1=中継 2=鏡 3=設定）
    private CheckBox chkAnyUser = null!, chkProxy = null!, chkLogFile = null!, chkTray = null!;
    private TextBox tbSound = null!, tbUpstream = null!;
    private RadioButton rbModeRelay = null!, rbModeMirror = null!;
    private Button btnGenSrc = null!, btnGenView = null!, btnProfileLoad = null!, btnProfileSave = null!, btnProfileDelete = null!;
    private TextBox tbProfileName = null!;
    private ListBox lbProfiles = null!;
    private System.Windows.Controls.Primitives.Popup popProfiles = null!;
    private DateTime _profilePopupClosedAt = DateTime.MinValue;
    private bool _loading;
    /// <summary>項目名 → 赤枠をつける入力欄（連動する欄は複数）。</summary>
    private readonly Dictionary<string, List<Control>> _fields = new();
    /// <summary>項目名 → その項目があるタブ（問題が起きたとき、そのタブへ切り替える）。</summary>
    private readonly Dictionary<string, List<int>> _fieldTabs = new();

    // ---------- 組み立て ----------

    private void UseForm(Grid g, int tab) { _g = g; _curTab = tab; }

    private void BuildSettingsForm()
    {
        foreach (var g in new[] { gSet, gMirror, gSys })
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        ProfileHeader();

        // ===== 中継タブ（配信ソフトから受けて中継する設定） =====
        UseForm(gSet, 1);
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

        // ===== 鏡タブ（別のBatonE2Eから受けて中継する設定） =====
        UseForm(gMirror, 2);
        Group("gui.grp.mirror");
        rbModeRelay = Choice(Loc.Get("gui.mode.relay"), "mode");
        rbModeMirror = Choice(Loc.Get("gui.mode.mirror"), "mode");
        rbModeRelay.Checked += (_, _) => ApplyModeEnabled();
        rbModeMirror.Checked += (_, _) => ApplyModeEnabled();
        Row("gui.set.mode", HPanel(rbModeRelay, rbModeMirror), "gui.tip.mode", "Mode");
        tbUpstream = Txt(420);
        Row("gui.set.upstream", tbUpstream, "gui.tip.upstream", "UpstreamUrl");
        tbMirrorNo = Txt(60);
        Row("gui.set.mirrorno", tbMirrorNo, "gui.tip.mirrorno", "MirrorNumber");
        tbMirrorPreview = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 440, Foreground = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
        Row("gui.set.mirrorpath", tbMirrorPreview, "gui.tip.mirrorpath");
        tbUpstream.TextChanged += (_, _) => UpdateMirrorPreview();
        tbMirrorNo.TextChanged += (_, _) => UpdateMirrorPreview();

        // 「中継」タブと共通の項目は、同じ値を連動して表示する（どちらで変えても両方が変わる）
        Group("gui.grp.mirrorshared");
        tbViewPort2 = Txt(100);
        LinkText(tbViewPort, tbViewPort2);
        Row("gui.set.viewport", tbViewPort2, "gui.tip.viewport", "ViewPort");
        tbMaxViewers2 = Txt(100);
        LinkText(tbMaxViewers, tbMaxViewers2);
        Row("gui.set.maxviewers", tbMaxViewers2, "gui.tip.maxviewers", "MaxViewers");
        Note("gui.mirror.sharednote");

        // ===== 設定タブ（このPC・このアプリの設定。プロファイルには含めない） =====
        UseForm(gSys, 3);
        Group("gui.grp.lang");
        cbLang = new ComboBox { Width = 260 };
        cbLang.Items.Add(Loc.Get("gui.lang.system"));
        cbLang.Items.Add("日本語");
        cbLang.Items.Add("English");
        cbLang.SelectionChanged += (_, _) => UpdateDirty();
        Row("gui.set.language", cbLang, "gui.tip.language");

        Group("gui.grp.system");
        chkLogFile = Check(Loc.Get("gui.set.logfile"));
        Row("", chkLogFile, "gui.tip.logfile");
        chkTray = Check(Loc.Get("gui.set.tray"));
        Row("", chkTray, "gui.tip.tray");

        Group("gui.grp.notify");
        tbSound = Txt(300);
        var btnBrowse = new Button { Content = Loc.Get("gui.sound.browse"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        btnBrowse.Click += (_, _) => OnBrowseSound();
        var btnTest = new Button { Content = Loc.Get("gui.sound.test"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        btnTest.Click += (_, _) => OnTestSound();
        Row("gui.set.sound", HPanel(tbSound, btnBrowse, btnTest), "gui.tip.sound", "NotifySound");

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

    /// <summary>2つの入力欄の文字を連動させる（どちらを変えても、もう一方が同じになる）。</summary>
    private static void LinkText(TextBox a, TextBox b)
    {
        a.TextChanged += (_, _) => { if (b.Text != a.Text) b.Text = a.Text; };
        b.TextChanged += (_, _) => { if (a.Text != b.Text) a.Text = b.Text; };
    }

    /// <summary>ミラーの視聴ページのパスと題名（自動で決まる）の見本を出す。</summary>
    private void UpdateMirrorPreview()
    {
        if (tbMirrorPreview == null || tbUpstream == null || tbMirrorNo == null) return;
        if (MirrorUrl.ToWebSocketUri(tbUpstream.Text) == null) { tbMirrorPreview.Text = "—"; return; }
        var n = int.TryParse(tbMirrorNo.Text.Trim(), out var v) ? Math.Clamp(v, 1, 99) : 1;
        tbMirrorPreview.Text = Loc.Get("gui.mirror.preview", MirrorUrl.MirrorViewPath(tbUpstream.Text, n), Loc.Get("gui.mirror.suffix", n));
    }

    private void SelectTab(int i) => (i switch { 1 => rbRelay, 2 => rbMirror, 3 => rbSystem, _ => rbMain }).IsChecked = true;

    private void Note(string key)
    {
        _g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var t = new TextBlock
        {
            Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 10, 0, 0), MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetRow(t, _g.RowDefinitions.Count - 1);
        Grid.SetColumnSpan(t, 2);
        _g.Children.Add(t);
    }

    /// <summary>プロファイルの欄。中継・鏡タブの上の共通の帯に、見出し（青文字）と同じ1行で、名前の入力欄（▼で一覧）・ボタン・ⓘを置く。</summary>
    private void ProfileHeader()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = Loc.Get("gui.grp.profile"), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentHoverBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0),
        });

        tbProfileName = new TextBox { Width = 200 };
        var drop = new Button { Content = "▼", Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(2, 0, 0, 0), FontSize = 10 };
        var combo = HPanel(tbProfileName, drop);
        lbProfiles = new ListBox
        {
            MinWidth = 230, MaxHeight = 170, Background = (Brush)FindResource("InputBrush"), Foreground = (Brush)FindResource("TextBrush"),
            BorderThickness = new Thickness(0),
        };
        popProfiles = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = tbProfileName, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false,
            Child = new Border { Background = (Brush)FindResource("InputBrush"), BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(1), Child = lbProfiles },
        };
        popProfiles.Closed += (_, _) => _profilePopupClosedAt = DateTime.UtcNow;
        lbProfiles.SelectionChanged += (_, _) =>
        {
            if (lbProfiles.SelectedItem is not string n) return;
            tbProfileName.Text = n;
            popProfiles.IsOpen = false;
            lbProfiles.SelectedItem = null;
        };
        drop.Click += (_, _) =>
        {
            if ((DateTime.UtcNow - _profilePopupClosedAt).TotalMilliseconds < 250) return; // ▼を押して閉じた直後の再オープンを防ぐ
            RefreshProfiles();
            if (lbProfiles.Items.Count == 0) { SetMsg(Loc.Get("gui.profile.empty"), Msg.Muted); return; }
            popProfiles.IsOpen = true;
        };
        combo.Children.Add(popProfiles);
        row.Children.Add(combo);

        btnProfileSave = ProfileButton("gui.profile.save", OnProfileSave);
        btnProfileLoad = ProfileButton("gui.profile.load", OnProfileLoad);
        btnProfileDelete = ProfileButton("gui.profile.delete", OnProfileDelete);
        row.Children.Add(btnProfileSave);
        row.Children.Add(btnProfileLoad);
        row.Children.Add(btnProfileDelete);
        row.Children.Add(Info("gui.tip.profile", Array.Empty<object>()));
        _fields["Profile"] = new List<Control> { tbProfileName };
        profileBar.Child = row;
        RefreshProfiles();
    }

    private Button ProfileButton(string key, RoutedEventHandler onClick)
    {
        var b = new Button { Content = Loc.Get(key), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        b.Click += onClick;
        return b;
    }

    private void Group(string key)
    {
        _g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var b = new Border
        {
            BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, _g.RowDefinitions.Count == 1 ? 0 : 10, 0, 2), Padding = new Thickness(0, 0, 0, 4),
            Child = new TextBlock { Text = Loc.Get(key), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentHoverBrush") },
        };
        Grid.SetRow(b, _g.RowDefinitions.Count - 1);
        Grid.SetColumnSpan(b, 2);
        _g.Children.Add(b);
    }

    /// <summary>見出し（右寄せ）：入力欄（左揃えで頭をそろえる）＋ⓘ。</summary>
    private void Row(string labelKey, FrameworkElement input, string? tipKey, string? field = null, params object[] tipArgs)
    {
        _g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var r = _g.RowDefinitions.Count - 1;
        var lbl = new TextBlock
        {
            Text = labelKey.Length > 0 ? Loc.Get(labelKey) : "", TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 4, 2),
        };
        Grid.SetRow(lbl, r);
        _g.Children.Add(lbl);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        sp.Children.Add(input);
        if (tipKey != null) sp.Children.Add(Info(tipKey, tipArgs));
        Grid.SetRow(sp, r);
        Grid.SetColumn(sp, 1);
        _g.Children.Add(sp);
        if (field != null)
        {
            var marks = new List<Control>();
            Collect(input, marks);
            foreach (var f in field.Split(','))
            {
                if (!_fields.TryGetValue(f, out var list)) _fields[f] = list = new List<Control>();
                list.AddRange(marks);
                if (!_fieldTabs.TryGetValue(f, out var tabs)) _fieldTabs[f] = tabs = new List<int>();
                tabs.Add(_curTab);
            }
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
        cbLang.SelectedIndex = s.Language switch { "ja" => 1, "en" => 2, _ => 0 };
        tbMirrorNo.Text = s.MirrorNumber.ToString();
        rbModeRelay.IsChecked = s.Mode != "mirror";
        rbModeMirror.IsChecked = s.Mode == "mirror";
        tbUpstream.Text = s.UpstreamUrl;
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
        ApplyModeEnabled();
        UpdateMirrorPreview();
        chkTray.IsChecked = s.MinimizeToTray;
        tbSound.Text = s.NotifySound;
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
            Language = cbLang.SelectedIndex switch { 1 => "ja", 2 => "en", _ => "system" },
            MirrorNumber = Num(tbMirrorNo, "MirrorNumber", "gui.set.mirrorno", 1, 99),
            Mode = rbModeMirror.IsChecked == true ? "mirror" : "relay",
            UpstreamUrl = tbUpstream.Text,
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
            MinimizeToTray = chkTray.IsChecked == true,
            NotifySound = tbSound.Text,
        };
        s.Normalize();
        issue = iss;
        return s;
    }

    /// <summary>モードに合わない入力欄を灰色にする（ミラーは配信ソフトの受け口・パスワードを使わない）。</summary>
    private void ApplyModeEnabled()
    {
        if (tbUpstream == null || tbMirrorNo == null || chkAnyUser == null) return;
        var mirror = rbModeMirror.IsChecked == true;
        tbUpstream.IsEnabled = mirror;
        tbMirrorNo.IsEnabled = mirror;
        // ミラーでは視聴ページのパス・題名は自動で決まるので、これらも使わない
        foreach (var c in new Control[] { tbSrcBind, tbSrcPort, tbMount, tbSrcPass, btnGenSrc, tbViewPass, btnGenView, rbTlsAuto, rbTlsOn, rbTlsOff, chkAnyUser, tbViewPath, tbPageTitle })
            c.IsEnabled = !mirror;
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
        foreach (var list in _fields.Values) foreach (var c in list) c.BorderBrush = line;
    }

    private void ShowIssue(AppSettings.Issue i)
    {
        SetMsg(Loc.Get("gui.set.notsaved", IssueText(i)), Msg.Err);
        var switched = false;
        foreach (var f in i.Field.Split(','))
        {
            if (!_fields.TryGetValue(f, out var list)) continue;
            foreach (var c in list) c.BorderBrush = _errBrush;
            if (switched) continue;
            // 問題のある項目があるタブへ切り替える（連動する項目は、いまの動作モードのタブを優先）
            if (_fieldTabs.TryGetValue(f, out var tabs))
            {
                var prefer = rbModeMirror.IsChecked == true ? 2 : 1;
                SelectTab(tabs.Contains(prefer) ? prefer : tabs[0]);
            }
            foreach (var c in list) c.BringIntoView();
            switched = true;
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
            if (!ConfirmDialog.Ask(this, text, Loc.Get("gui.sec.title"), true))
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
        var langChanged = Loc.Resolve(ns.Language) != Loc.Language;
        _set = ns;
        LoadUi(ns);
        UpdateSecBanner();
        UpdateSourceCard();
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

    /// <summary>視聴ページのひな形（viewer.html / viewer.css）をviewerフォルダへ出力。既存のファイルは上書きしない。</summary>
    private void OnTemplate(object sender, RoutedEventArgs e)
    {
        var dir = ViewerTemplate.Folder;
        var made = new List<string>();
        var kept = new List<string>();
        try
        {
            Directory.CreateDirectory(dir);
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
