using System.IO;
using System.Windows;
using BatonE2E.Core;
using BatonE2E.Core.Config;

namespace BatonE2E.Gui;

/// <summary>設定画面のプリセット（名前付き設定セット）の操作。</summary>
public partial class MainWindow
{
    private void RefreshPresets()
    {
        try { lbPresets.ItemsSource = PresetStore.List(App.ConfigPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lbPresets.ItemsSource = null; }
    }

    /// <summary>プリセット欄の問題を、名前の入力欄を赤枠にして表示する。</summary>
    private void PresetError(string key, params object[] args)
    {
        SetMsg(Loc.Get(key, args), Msg.Err);
        if (_fields.TryGetValue("Preset", out var arr)) foreach (var c in arr) c.BorderBrush = _errBrush;
    }

    private void OnPresetSave(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var name = PresetStore.Clean(tbPresetName.Text);
        var nameIssue = PresetStore.CheckName(name);
        if (nameIssue != null) { PresetError(nameIssue.Key, nameIssue.Args); return; }
        var ns = ReadUi(out var issue);
        issue ??= ns.Check();
        if (issue != null) { ShowIssue(issue); return; }
        try
        {
            if (PresetStore.Exists(App.ConfigPath, name)
                && MessageBox.Show(this, Loc.Get("gui.preset.confirm.overwrite", name), Loc.Get("gui.preset.title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                SetMsg(Loc.Get("gui.set.cancelled"), Msg.Warn);
                return;
            }
            PresetStore.Save(App.ConfigPath, name, ns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PresetError("gui.preset.err.io", ex.Message);
            return;
        }
        RefreshPresets();
        lbPresets.SelectedItem = name;
        tbPresetName.Text = name;
        SetMsg(Loc.Get("gui.preset.saved", name), Msg.Ok);
    }

    private void OnPresetLoad(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var name = PresetStore.Clean(tbPresetName.Text);
        var nameIssue = PresetStore.CheckName(name);
        if (nameIssue != null) { PresetError(nameIssue.Key, nameIssue.Args); return; }
        if (!PresetStore.Exists(App.ConfigPath, name)) { PresetError("gui.preset.err.none", name); return; }
        var p = PresetStore.Load(App.ConfigPath, name, _set);
        if (p == null) { PresetError("gui.preset.err.broken", name); return; }
        LoadUi(p);
        SetMsg(Loc.Get("gui.preset.loaded", name), Msg.Warn);
    }

    private void OnPresetDelete(object sender, RoutedEventArgs e)
    {
        ClearMarks();
        var name = PresetStore.Clean(tbPresetName.Text);
        var nameIssue = PresetStore.CheckName(name);
        if (nameIssue != null) { PresetError(nameIssue.Key, nameIssue.Args); return; }
        if (!PresetStore.Exists(App.ConfigPath, name)) { PresetError("gui.preset.err.none", name); return; }
        if (MessageBox.Show(this, Loc.Get("gui.preset.confirm.delete", name), Loc.Get("gui.preset.title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            SetMsg(Loc.Get("gui.set.cancelled"), Msg.Warn);
            return;
        }
        if (!PresetStore.Delete(App.ConfigPath, name)) { PresetError("gui.preset.err.io", name); return; }
        RefreshPresets();
        tbPresetName.Text = "";
        SetMsg(Loc.Get("gui.preset.deleted", name), Msg.Ok);
    }
}
