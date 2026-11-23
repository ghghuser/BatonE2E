using System.IO;
using System.Windows;
using BatonE2E.Core;
using BatonE2E.Core.Config;

namespace BatonE2E.Gui;

/// <summary>設定画面のプロファイル（名前付き設定セット）の操作。</summary>
public partial class MainWindow
{
    private void RefreshProfiles()
    {
        try { lbProfiles.ItemsSource = ProfileStore.List(App.ConfigPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lbProfiles.ItemsSource = null; }
    }

    /// <summary>プロファイル欄の問題を、名前の入力欄を赤枠にして表示する。</summary>
    private void ProfileError(string key, params object[] args)
    {
        SetMsg(Loc.Get(key, args), Msg.Err);
        if (_fields.TryGetValue("Profile", out var arr)) foreach (var c in arr) c.BorderBrush = _errBrush;
    }

    private void OnProfileSave(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var name = ProfileStore.Clean(tbProfileName.Text);
        var nameIssue = ProfileStore.CheckName(name);
        if (nameIssue != null) { ProfileError(nameIssue.Key, nameIssue.Args); return; }
        var ns = ReadUi(out var issue);
        issue ??= ns.Check();
        if (issue != null) { ShowIssue(issue); return; }
        try
        {
            if (ProfileStore.Exists(App.ConfigPath, name)
                && !ConfirmDialog.Ask(this, Loc.Get("gui.profile.confirm.overwrite", name), Loc.Get("gui.profile.title"), false))
            {
                SetMsg(Loc.Get("gui.set.cancelled"), Msg.Warn);
                return;
            }
            ProfileStore.Save(App.ConfigPath, name, ns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProfileError("gui.profile.err.io", ex.Message);
            return;
        }
        RefreshProfiles();
        tbProfileName.Text = name;
        SetMsg(Loc.Get("gui.profile.saved", name), Msg.Ok);
    }

    private void OnProfileLoad(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var name = ProfileStore.Clean(tbProfileName.Text);
        var nameIssue = ProfileStore.CheckName(name);
        if (nameIssue != null) { ProfileError(nameIssue.Key, nameIssue.Args); return; }
        if (!ProfileStore.Exists(App.ConfigPath, name)) { ProfileError("gui.profile.err.none", name); return; }
        var p = ProfileStore.Load(App.ConfigPath, name, _set);
        if (p == null) { ProfileError("gui.profile.err.broken", name); return; }
        LoadUi(p);
        SetMsg(Loc.Get("gui.profile.loaded", name), Msg.Warn);
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
        if (!ProfileStore.Delete(App.ConfigPath, name)) { ProfileError("gui.profile.err.io", name); return; }
        RefreshProfiles();
        tbProfileName.Text = "";
        SetMsg(Loc.Get("gui.profile.deleted", name), Msg.Ok);
    }
}
