namespace VolumeKeeper;

internal sealed class SettingsForm : Form
{
    private readonly Label _device = NewLabel(20, 54, 590, 28);
    private readonly Label _volume = NewLabel(20, 85, 590, 26);
    private readonly Label _saved = NewLabel(20, 113, 590, 26);
    private readonly Label _status = NewLabel(20, 432, 590, 45);
    private readonly ListView _profiles = new()
    {
        Location = new Point(20, 178),
        Size = new Size(590, 150),
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false
    };
    private readonly NumericUpDown _profileVolume = new()
    {
        Location = new Point(20, 343),
        Size = new Size(75, 27),
        Minimum = 0,
        Maximum = 100,
        Enabled = false
    };
    private readonly Button _saveProfile = NewButton("保存音量を変更", 110, 341, 140);
    private readonly Button _deleteProfile = NewButton("選択した設定を削除", 260, 341, 155);
    private readonly Button _saveCurrent = NewButton("現在の音量を保存", 20, 139, 160);
    private readonly CheckBox _paused = new()
    {
        Location = new Point(20, 390),
        AutoSize = true,
        Text = "自動適用を一時停止"
    };
    private readonly CheckBox _startup = new()
    {
        Location = new Point(240, 390),
        AutoSize = true,
        Text = "Windows ログイン時に起動"
    };
    private bool _refreshing;

    public event Action? SaveCurrentRequested;
    public event Action<DeviceProfile, float>? SaveProfileRequested;
    public event Action<DeviceProfile>? DeleteProfileRequested;
    public event Action<bool>? PauseChanged;
    public event Action<bool>? StartupChanged;

    public SettingsForm()
    {
        Text = "VolumeKeeper";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(630, 490);
        MinimumSize = new Size(650, 530);
        MaximizeBox = false;
        Font = new Font("Yu Gothic UI", 9F);

        Controls.Add(NewLabel(20, 17, 590, 28, "機器ごとの音量", 14F));
        Controls.AddRange([_device, _volume, _saved, _saveCurrent]);
        Controls.Add(NewLabel(20, 157, 590, 22, "保存済みの機器"));
        _profiles.Columns.Add("機器", 425);
        _profiles.Columns.Add("保存音量", 130);
        Controls.Add(_profiles);
        Controls.AddRange([_profileVolume, NewLabel(97, 345, 20, 24, "%"),
            _saveProfile, _deleteProfile, _paused, _startup, _status]);

        _saveCurrent.Click += (_, _) => SaveCurrentRequested?.Invoke();
        _profiles.SelectedIndexChanged += (_, _) => UpdateSelection();
        _saveProfile.Click += (_, _) =>
        {
            if (SelectedProfile is { } profile)
                SaveProfileRequested?.Invoke(profile, (float)_profileVolume.Value / 100f);
        };
        _deleteProfile.Click += (_, _) =>
        {
            if (SelectedProfile is { } profile && MessageBox.Show(
                $"「{profile.Name}」の保存音量を削除しますか？", "VolumeKeeper",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                DeleteProfileRequested?.Invoke(profile);
        };
        _paused.CheckedChanged += (_, _) =>
        {
            if (!_refreshing) PauseChanged?.Invoke(_paused.Checked);
        };
        _startup.CheckedChanged += (_, _) =>
        {
            if (!_refreshing) StartupChanged?.Invoke(_startup.Checked);
        };
        UpdateSelection();
    }

    public void ShowState(DeviceInfo? current, DeviceProfile? currentProfile,
        IReadOnlyList<DeviceProfile> profiles,
        bool paused, bool startup, string status)
    {
        _refreshing = true;
        try
        {
            _device.Text = current is null ? "現在の出力先: なし" : $"現在の出力先: {current.Name}";
            _volume.Text = current is null ? "現在の音量: —" :
                $"現在の音量: {Percent(current.Volume)}%{(current.Muted ? "（ミュート中）" : "")}";
            _saved.Text = current is null ? "保存音量: —" : currentProfile is null ?
                "保存音量: 未登録" : $"保存音量: {Percent(currentProfile.Volume)}%";
            _saveCurrent.Enabled = current is not null;
            _status.Text = $"状態: {status}";

            var selected = SelectedProfile;
            _profiles.BeginUpdate();
            try
            {
                _profiles.Items.Clear();
                foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.CurrentCulture))
                {
                    var item = new ListViewItem(profile.Name) { Tag = profile };
                    item.SubItems.Add($"{Percent(profile.Volume)}%");
                    _profiles.Items.Add(item);
                    if (ReferenceEquals(profile, selected)) item.Selected = true;
                }
            }
            finally { _profiles.EndUpdate(); }

            _paused.Checked = paused;
            _startup.Checked = startup;
            UpdateSelection();
        }
        finally { _refreshing = false; }
    }

    private DeviceProfile? SelectedProfile => _profiles.SelectedItems.Count == 0
        ? null : _profiles.SelectedItems[0].Tag as DeviceProfile;

    private void UpdateSelection()
    {
        var profile = SelectedProfile;
        _profileVolume.Enabled = profile is not null;
        _saveProfile.Enabled = profile is not null;
        _deleteProfile.Enabled = profile is not null;
        if (profile is not null)
            _profileVolume.Value = Math.Clamp(Percent(profile.Volume), 0, 100);
    }

    private static int Percent(float level) => (int)Math.Round(level * 100, MidpointRounding.AwayFromZero);

    private static Label NewLabel(int x, int y, int width, int height,
        string text = "", float fontSize = 9F) => new()
    {
        Location = new Point(x, y),
        Size = new Size(width, height),
        Text = text,
        Font = new Font("Yu Gothic UI", fontSize)
    };

    private static Button NewButton(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 30)
    };
}
