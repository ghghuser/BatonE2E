using System.IO;
using System.Windows;
using System.Windows.Media;
using BatonE2E.Core;
using BatonE2E.Core.Config;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;
using Microsoft.Win32;

namespace BatonE2E.Gui;

/// <summary>視聴者が接続したときの通知音（wav/mp3/wma。WPF標準のMediaPlayerで再生）。</summary>
public partial class MainWindow
{
    private JoinNotifier? _joinNotifier;
    private MediaPlayer? _player;
    private string? _soundWarnedFor;

    /// <summary>別スレッドから呼ばれるため、画面スレッドへ渡してから判定・再生する。</summary>
    private void OnViewerJoined(string remote)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var path = _set.NotifySound;
            if (path.Length == 0 || _joinNotifier == null) return;
            if (!_joinNotifier.ShouldNotify(remote, DateTime.Now)) return;
            PlaySound(path);
        });
    }

    private static string FullSoundPath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    private void PlaySound(string path)
    {
        var full = FullSoundPath(path);
        if (!File.Exists(full))
        {
            if (_soundWarnedFor != full) AppLog.Warn(Loc.Get("log.sound.missing", full));
            _soundWarnedFor = full;
            return;
        }
        _soundWarnedFor = null;
        try
        {
            if (_player == null)
            {
                _player = new MediaPlayer();
                _player.MediaFailed += (_, a) => AppLog.Warn(Loc.Get("log.sound.fail", a.ErrorException?.Message ?? "?"));
            }
            _player.Stop();
            _player.Open(new Uri(full));
            _player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException or NotSupportedException)
        {
            AppLog.Warn(Loc.Get("log.sound.fail", ex.Message));
        }
    }

    private void OnBrowseSound()
    {
        var dlg = new OpenFileDialog { Filter = Loc.Get("gui.sound.filter"), CheckFileExists = true };
        if (dlg.ShowDialog(this) == true) tbSound.Text = dlg.FileName;
    }

    private void OnTestSound()
    {
        ClearMarks();
        var path = tbSound.Text.Trim();
        string? err = null;
        if (path.Length == 0) err = Loc.Get("gui.sound.err.empty");
        else if (!AppSettings.SoundExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) err = Loc.Get("gui.err.soundext");
        else if (!File.Exists(FullSoundPath(path))) err = Loc.Get("gui.sound.err.missing", FullSoundPath(path));
        if (err != null)
        {
            SetMsg(err, Msg.Err);
            if (_fields.TryGetValue("NotifySound", out var arr)) foreach (var c in arr) c.BorderBrush = _errBrush;
            return;
        }
        SetMsg("", Msg.Muted);
        PlaySound(path);
    }
}
