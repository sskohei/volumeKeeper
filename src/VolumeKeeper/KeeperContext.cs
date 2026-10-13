namespace VolumeKeeper;

internal sealed class KeeperContext : ApplicationContext
{
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly string? _loadWarning;
    private readonly SettingsForm _form;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _switchTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 300 };
    private AudioService? _audio;
    private string _status = "";
    private bool _exiting;
    private bool _hasInitialSnapshot;
    private string? _lastObservedEndpointId;

    public KeeperContext()
    {
        _settings = _store.Load(out var warning);
        _loadWarning = warning;
        _status = warning ?? "準備中...";
        _form = new SettingsForm();
        _form.FormClosing += OnFormClosing;
        _form.SaveCurrentRequested += SaveCurrent;
        _form.ApplyCurrentRequested += ApplyCurrentVolume;
        _form.SaveProfileRequested += SaveProfile;
        _form.DeleteProfileRequested += DeleteProfile;
        _form.PauseChanged += SetPaused;
        _form.StartupChanged += SetStartup;
        _ = _form.Handle;

        var menu = new ContextMenuStrip();
        menu.Items.Add("設定を開く", null, (_, _) => ShowWindow());
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Text = "VolumeKeeper",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        _switchTimer.Tick += (_, _) =>
        {
            _switchTimer.Stop();
            RefreshAudio();
        };
        _pollTimer.Tick += (_, _) => RefreshAudio();
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Persist();
        };

        RefreshAudio();
        _pollTimer.Start();
        _form.Show();
        if (_loadWarning is not null)
            MessageBox.Show(_form, _loadWarning, "VolumeKeeper", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
    }

    private void ShowWindow()
    {
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        _form.Hide();
    }

    private void Post(Action action)
    {
        try
        {
            if (!_form.IsDisposed && _form.IsHandleCreated) _form.BeginInvoke(action);
        }
        catch (InvalidOperationException) { }
    }

    private void ScheduleSwitch() => Post(() =>
    {
        _switchTimer.Stop();
        _switchTimer.Start();
    });

    private void RefreshAudio()
    {
        try
        {
            if (_audio is null)
            {
                _audio = new AudioService();
                _audio.DeviceChanged += ScheduleSwitch;
                _audio.VolumeChanged += (id, level, muted, ours) =>
                    Post(() => OnVolumeChanged(id, level, muted, ours));
            }

            var previousId = _audio.Current?.EndpointId;
            var previousVolume = _audio.Current?.Volume;
            var changed = _audio.RefreshDefault();
            var current = _audio.Current;
            if (VolumePolicy.ShouldApplyOnSwitch(changed, _hasInitialSnapshot,
                    _lastObservedEndpointId, current?.EndpointId, _settings.Paused) && current is not null)
            {
                var profile = _settings.Find(current);
                if (profile is not null)
                {
                    _audio.SetVolume(profile.Volume);
                    _settings.Save(current, profile.Volume);
                    ScheduleSave();
                }
            }
            else if (!changed && current is not null && previousVolume.HasValue &&
                     string.Equals(previousId, current.EndpointId, StringComparison.Ordinal) &&
                     Math.Abs(current.Volume - previousVolume.Value) > VolumePolicy.ChangeTolerance)
            {
                // Catch a change if its COM notification was missed or is still queued.
                _settings.Save(current, current.Volume);
                ScheduleSave();
            }

            _status = current is null ? "再生用の出力先がありません。" :
                changed && _settings.Find(current) is null ? "未登録の機器です。音量を調整すると自動保存します。" :
                "監視中";
            _hasInitialSnapshot = true;
            _lastObservedEndpointId = current?.EndpointId;
            UpdateView();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _status = $"音声機器を操作できません: {error.Message}";
            try { _audio?.Dispose(); }
            catch (System.Runtime.InteropServices.COMException) { }
            _audio = null;
            UpdateView();
        }
    }

    private void OnVolumeChanged(string endpointId, float level, bool muted, bool ours)
    {
        var current = _audio?.Current;
        if (current is null || !string.Equals(current.EndpointId, endpointId, StringComparison.Ordinal)) return;
        var previousVolume = current.Volume;
        try { _audio!.ReadCurrentVolume(); }
        catch (System.Runtime.InteropServices.COMException)
        {
            ScheduleSwitch();
            return;
        }
        if (Math.Abs(current.Volume - level) > VolumePolicy.ChangeTolerance) return; // An older queued notification.
        var volumeChanged = Math.Abs(level - previousVolume) > VolumePolicy.ChangeTolerance;
        current.Muted = muted;
        if (!ours && volumeChanged)
        {
            _settings.Save(current, level);
            ScheduleSave();
            _status = "音量を自動保存しました。";
        }
        UpdateView();
    }

    private void SaveCurrent()
    {
        var current = _audio?.Current;
        if (current is null) return;
        _settings.Save(current, current.Volume);
        ScheduleSave();
        _status = "現在の音量を保存しました。";
        UpdateView();
    }

    private void ApplyCurrentVolume(float level)
    {
        var current = _audio?.Current;
        if (current is null) return;
        try
        {
            _audio!.SetVolume(level);
            // Save the actual level reported by the device, which may have coarser steps.
            _settings.Save(current, current.Volume);
            ScheduleSave();
            _status = $"音量 {VolumePercent.Format(current.Volume)}% を適用して保存しました。";
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _status = $"{current.Name} の音量を変更できません: {error.Message}";
        }
        UpdateView();
    }

    private void SaveProfile(DeviceProfile profile, float level)
    {
        profile.Volume = Math.Clamp(level, 0f, 1f);
        ScheduleSave();
        _status = "保存音量を変更しました。次回の切り替え時に適用します。";
        UpdateView();
    }

    private void DeleteProfile(DeviceProfile profile)
    {
        _settings.Profiles.Remove(profile);
        ScheduleSave();
        _status = "設定を削除しました。";
        UpdateView();
    }

    private void SetPaused(bool paused)
    {
        _settings.Paused = paused;
        ScheduleSave();
        _status = paused ? "自動適用を一時停止中。音量変更の自動保存は続けます。" : "監視中";
        UpdateView();
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
            _status = enabled ? "ログイン時の自動起動を有効にしました。" : "ログイン時の自動起動を無効にしました。";
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException)
        {
            _status = $"自動起動を設定できません: {error.Message}";
        }
        UpdateView();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Persist()
    {
        try { _store.Save(_settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _status = $"設定を保存できません: {error.Message}";
            UpdateView();
        }
    }

    private void UpdateView()
    {
        bool startup;
        try { startup = StartupRegistration.IsEnabled(); }
        catch (UnauthorizedAccessException) { startup = false; }
        var current = _audio?.Current;
        _form.ShowState(current, current is null ? null : _settings.Find(current),
            _settings.Profiles, _settings.Paused, startup, _status);
        _tray.Text = _settings.Paused ? "VolumeKeeper（一時停止中）" : "VolumeKeeper";
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        _pollTimer.Stop();
        _switchTimer.Stop();
        _saveTimer.Stop();
        Persist();
        _audio?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _form.Close();
        _form.Dispose();
        base.ExitThreadCore();
    }
}
