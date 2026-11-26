using System.IO;
using System.Windows;
using BatonE2E.Core;
using BatonE2E.Core.Config;
using BatonE2E.Core.Logging;

namespace BatonE2E.Gui;

/// <summary>設定画面のプロファイル（名前付き設定セット）の操作。</summary>
public partial class MainWindow
{
    private void RefreshProfiles()
    {
        var items = new List<string> { Loc.Get("gui.profile.none") };
        try { items.AddRange(ProfileStore.List(App.ConfigPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 一覧が読めなければ「（なし）」だけ */ }
        lbProfiles.ItemsSource = items;
    }

    /// <summary>プロファイル欄の問題を、名前の入力欄を赤枠にして表示する。</summary>
    private void ProfileError(string key, params object[] args)
    {
        SetMsg(Loc.Get(key, args), Msg.Err);
        if (_fields.TryGetValue("Profile", out var arr)) foreach (var c in arr) c.BorderBrush = _errBrush;
    }

    /// <summary>いま使っているプロファイルの表示名（使っていなければ「（なし）」）。</summary>
    private string CurrentProfileName => _curProfile.Length > 0 ? _curProfile : Loc.Get("gui.profile.none");

    /// <summary>入力欄の名前で保存する。現在のプロファイルと同じ名前なら上書き、違う名前なら新しいプロファイルを作り、以後それを使う。
    /// 下の「保存」と同じく全体の設定も保存する。</summary>
    private void OnProfileSave(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var name = ProfileStore.Clean(tbProfileName.Text);
        var nameIssue = ProfileStore.CheckName(name);
        if (nameIssue != null) { ProfileError(nameIssue.Key, nameIssue.Args); return; }
        if (name != _curProfile && ProfileStore.Exists(App.ConfigPath, name)
            && !ConfirmDialog.Ask(this, Loc.Get("gui.profile.confirm.overwrite", name), Loc.Get("gui.profile.title"), false))
        {
            SetMsg(Loc.Get("gui.set.cancelled"), Msg.Warn);
            return;
        }
        var prev = _curProfile;
        _curProfile = name;
        OnSave(sender, e);
        if (_set.CurrentProfile != name)   // 検査・確認・書き込みのどれかで保存されなかった
        {
            _curProfile = prev;
            tbProfileName.Text = CurrentProfileName;
            return;
        }
        RefreshProfiles();
        SetMsg(Loc.Get("gui.profile.saved", name) + (_engine != null ? "\n" + Loc.Get("gui.set.saved.running", DateTime.Now.ToString("HH:mm:ss")) : ""), Msg.Ok);
    }

    /// <summary>別のプロファイルへ切り替える（一覧から選んだとき）。未保存の変更があれば破棄してよいか確認する。切り替えた名前はすぐ config.json に記録する。</summary>
    private void SwitchProfile(string name, bool skipConfirm = false, bool quiet = false)
    {
        ClearMarks();
        var cur = CurrentProfileName;
        tbProfileName.Text = cur;
        if (name == _curProfile) return;
        if (name.Length == 0)   // 「（なし）」: プロファイルを外す。入力欄の内容はそのまま（保存で config.json に全項目を書く）
        {
            _curProfile = "";
            tbProfileName.Text = CurrentProfileName;
            UpdateDirty();
            if (!quiet) SetMsg(Loc.Get("gui.profile.detached"), Msg.Warn);
            return;
        }
        if (ProfileStore.CheckName(name) != null || !ProfileStore.Exists(App.ConfigPath, name)) { ProfileError("gui.profile.err.none", name); return; }
        if (!skipConfirm && ReadUi(out _).ToJson() != _set.ToJson()
            && !ConfirmDialog.Ask(this, Loc.Get("gui.profile.confirm.discard", cur), Loc.Get("gui.profile.title"), false))
        {
            SetMsg(Loc.Get("gui.set.cancelled"), Msg.Warn);
            return;
        }
        var p = ProfileStore.Load(App.ConfigPath, name, _set);
        if (p == null) { ProfileError("gui.profile.err.broken", name); return; }
        // 弱い設定のプロファイルを選んだだけで中継が弱くなるため、保存時と同じ確認を出す
        var before = _set.SecurityWarnings();
        var added = p.SecurityWarnings().Where(w => !before.Contains(w)).ToList();
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
        try { p.Save(App.ConfigPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProfileError("gui.profile.err.io", ex.Message);
            return;
        }
        _set = p;
        _curProfile = p.CurrentProfile;
        tbProfileName.Text = _curProfile;
        LoadUi(p);
        UpdateSecBanner();
        UpdateSourceCard();
        if (!quiet) SetMsg(Loc.Get(_engine != null ? "gui.profile.switched.running" : "gui.profile.switched", name), Msg.Ok);
    }

    private void OnProfileDelete(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var name = ProfileStore.Clean(tbProfileName.Text);
        var nameIssue = ProfileStore.CheckName(name);
        if (nameIssue != null) { ProfileError(nameIssue.Key, nameIssue.Args); return; }
        if (!ProfileStore.Exists(App.ConfigPath, name)) { ProfileError("gui.profile.err.none", name); return; }
        if (!ConfirmDialog.Ask(this, Loc.Get("gui.profile.confirm.delete", name), Loc.Get("gui.profile.title"), false))
        {
            SetMsg(Loc.Get("gui.set.cancelled"), Msg.Warn);
            return;
        }
        var wasCurrent = name == _curProfile;
        if (!ProfileStore.Delete(App.ConfigPath, name)) { ProfileError("gui.profile.err.io", name); return; }
        RefreshProfiles();
        if (wasCurrent)
        {
            // 使っていたものを消したら「（なし）」にする。入力欄の内容はそのまま。config.json は保存済みの内容で書き直す
            _curProfile = "";
            _set.CurrentProfile = "";
            try { _set.Save(App.ConfigPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ProfileError("gui.profile.err.io", ex.Message); }
            tbProfileName.Text = CurrentProfileName;
            UpdateDirty();
        }
        else tbProfileName.Text = CurrentProfileName;
        SetMsg(Loc.Get("gui.profile.deleted", name), Msg.Ok);
    }
}
