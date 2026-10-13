using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VolumeKeeper;

internal sealed class SettingsForm : Form
{
    private static readonly Color Sky = Color.FromArgb(185, 234, 250);
    private static readonly Color Paper = Color.FromArgb(245, 252, 255);
    private static readonly Color Ink = Color.FromArgb(24, 61, 80);
    private static readonly Color Accent = Color.FromArgb(16, 119, 151);
    private static readonly Color Border = Color.FromArgb(102, 184, 207);

    private readonly PrivateFontCollection _fontCollection = new();
    private readonly Dictionary<float, Font> _fonts = new();
    private GCHandle _fontData;
    private IntPtr _gdiFontResource;
    private readonly Label _device;
    private readonly Label _volume;
    private readonly Label _saved;
    private readonly Label _status;
    private readonly ListView _profiles;
    private readonly NumericUpDown _currentVolume;
    private readonly Button _applyCurrent;
    private readonly NumericUpDown _profileVolume;
    private readonly Button _saveProfile;
    private readonly Button _deleteProfile;
    private readonly Button _saveCurrent;
    private readonly CheckBox _paused;
    private readonly CheckBox _startup;
    private readonly ToolTip _deviceTip = new();
    private bool _refreshing;
    private bool _fullscreen;
    private Rectangle _previousBounds;
    private FormWindowState _previousWindowState;

    public event Action? SaveCurrentRequested;
    public event Action<float>? ApplyCurrentRequested;
    public event Action<DeviceProfile, float>? SaveProfileRequested;
    public event Action<DeviceProfile>? DeleteProfileRequested;
    public event Action<bool>? PauseChanged;
    public event Action<bool>? StartupChanged;

    public SettingsForm()
    {
        LoadDotFont();
        Font = DotFont(11);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "VolumeKeeper";
        Icon = SystemIcons.Application;
        BackColor = Sky;
        ForeColor = Ink;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        KeyPreview = true;

        _device = Label(16, ContentAlignment.MiddleLeft);
        _device.AutoEllipsis = true;
        _volume = Label(19, ContentAlignment.MiddleLeft);
        _saved = Label(12, ContentAlignment.MiddleLeft);
        _status = Label(11, ContentAlignment.MiddleLeft);
        _status.AutoEllipsis = true;
        _profiles = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Ink,
            Font = DotFont(11)
        };
        _profiles.Columns.Add("機器", 500);
        _profiles.Columns.Add("保存音量", 180);
        _profiles.Resize += (_, _) => ResizeProfileColumns();
        _currentVolume = VolumeInput();
        _applyCurrent = Button("音量を適用して保存", 220);
        _applyCurrent.Enabled = false;
        _profileVolume = VolumeInput();
        _saveProfile = Button("保存音量を変更", 185);
        _deleteProfile = Button("選択した設定を削除", 220, secondary: true);
        _saveCurrent = Button("現在の音量を保存", 210);
        _paused = CheckBox("自動適用を一時停止");
        _startup = CheckBox("Windows ログイン時に起動");

        var titleHeight = DotFont(24).Height + 16;
        var subtitleHeight = DotFont(11).Height + 12;
        var headerHeight = titleHeight + subtitleHeight + 12;
        var currentTitleHeight = DotFont(11).Height + 12;
        var currentBottomHeight = Math.Max(_saveCurrent.Height, DotFont(12).Height) + 10;
        var currentFineHeight = Math.Max(_currentVolume.Height, _applyCurrent.Height) + 12;
        var currentHeight = 43 + currentTitleHeight +
            Math.Max(DotFont(16).Height, DotFont(19).Height) + 18 +
            currentBottomHeight + currentFineHeight;
        var profilesTitleHeight = DotFont(14).Height + 12;
        var actionsHeight = Math.Max(_profileVolume.Height, _saveProfile.Height) + 18;
        var optionsHeight = Math.Max(64, DotFont(11).Height + 24) + 14;
        var statusHeight = DotFont(11).Height + 20;
        var working = Screen.FromPoint(Cursor.Position).WorkingArea;
        var minimumWidth = Math.Max(840,
            _profileVolume.Width + 30 + _saveProfile.Width + _deleteProfile.Width + 150);
        var minimumHeight = headerHeight + currentHeight + optionsHeight + statusHeight +
            profilesTitleHeight + actionsHeight + 110;
        MinimumSize = new Size(Math.Min(minimumWidth, working.Width),
            Math.Min(minimumHeight, working.Height));
        Size = new Size(Math.Min(Math.Max(1180, minimumWidth), working.Width),
            Math.Min(Math.Max(800, minimumHeight), working.Height));
        StartPosition = FormStartPosition.Manual;
        Location = new Point(working.Left + (working.Width - Width) / 2,
            working.Top + (working.Height - Height) / 2);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 18),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Sky
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, headerHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, currentHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, optionsHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, statusHeight));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, titleHeight));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(Label(24, ContentAlignment.MiddleLeft, "VOLUMEKEEPER"), 0, 0);
        header.Controls.Add(Label(10, ContentAlignment.MiddleRight, "F11 全画面 / Esc 戻る"), 1, 0);
        header.Controls.Add(Label(11, ContentAlignment.TopLeft, "出力先ごとに、いつもの音量を。"), 0, 1);
        root.Controls.Add(header, 0, 0);

        var currentCard = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
        var currentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 13, 22, 16),
            ColumnCount = 2,
            RowCount = 4
        };
        currentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        currentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, currentTitleHeight));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, currentBottomHeight));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, currentFineHeight));
        currentLayout.Controls.Add(Label(11, ContentAlignment.MiddleLeft, "● 現在の出力先"), 0, 0);
        currentLayout.Controls.Add(_device, 0, 1);
        currentLayout.Controls.Add(_saved, 0, 2);
        currentLayout.Controls.Add(_volume, 1, 1);
        _saveCurrent.Dock = DockStyle.Right;
        currentLayout.Controls.Add(_saveCurrent, 1, 2);
        var currentFine = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        currentFine.Controls.Add(new Label
        {
            Text = "現在の音量を調整",
            Font = DotFont(11),
            ForeColor = Ink,
            Size = new Size(190, _currentVolume.Height),
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true
        });
        currentFine.Controls.Add(_currentVolume);
        currentFine.Controls.Add(new Label
        {
            Text = "%（0.01%刻み）",
            Font = DotFont(11),
            ForeColor = Ink,
            Size = new Size(150, _currentVolume.Height),
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true
        });
        currentFine.Controls.Add(_applyCurrent);
        currentLayout.Controls.Add(currentFine, 0, 3);
        currentLayout.SetColumnSpan(currentFine, 2);
        currentCard.Controls.Add(currentLayout);
        root.Controls.Add(currentCard, 0, 1);

        var profilesCard = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
        var profilesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 12, 18, 12),
            ColumnCount = 1,
            RowCount = 3
        };
        profilesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, profilesTitleHeight));
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, actionsHeight));
        profilesLayout.Controls.Add(Label(14, ContentAlignment.MiddleLeft, "保存済みの機器"), 0, 0);
        profilesLayout.Controls.Add(_profiles, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        actions.Controls.Add(_profileVolume);
        actions.Controls.Add(new Label { Text = "%", Font = DotFont(12), ForeColor = Ink,
            Size = new Size(30, _profileVolume.Height), TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true });
        actions.Controls.Add(_saveProfile);
        actions.Controls.Add(_deleteProfile);
        profilesLayout.Controls.Add(actions, 0, 2);
        profilesCard.Controls.Add(profilesLayout);
        root.Controls.Add(profilesCard, 0, 2);

        var optionsCard = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 6, 22, 6), ColumnCount = 2 };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        options.Controls.Add(_paused, 0, 0);
        options.Controls.Add(_startup, 1, 0);
        optionsCard.Controls.Add(options);
        root.Controls.Add(optionsCard, 0, 3);

        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        status.Controls.Add(Label(10, ContentAlignment.MiddleLeft, "STATUS"), 0, 0);
        status.Controls.Add(_status, 1, 0);
        root.Controls.Add(status, 0, 4);

        _saveCurrent.Click += (_, _) => SaveCurrentRequested?.Invoke();
        _applyCurrent.Click += (_, _) =>
            ApplyCurrentRequested?.Invoke((float)_currentVolume.Value / 100f);
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
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11 || (_fullscreen && e.KeyCode == Keys.Escape))
            {
                ToggleFullscreen();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
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
            var editingCurrent = _currentVolume.ContainsFocus &&
                string.Equals(_device.Tag as string, current?.EndpointId, StringComparison.Ordinal);
            _device.Tag = current?.EndpointId;
            _device.Text = current?.Name ?? "出力先がありません";
            _deviceTip.SetToolTip(_device, current?.Name ?? "");
            _volume.Text = current is null ? "音量 —" :
                $"音量 {VolumePercent.Format(current.Volume)}%{(current.Muted ? "（ミュート中）" : "")}";
            _saved.Text = current is null ? "保存音量 —" : currentProfile is null ?
                "保存音量 未登録" : $"保存音量 {VolumePercent.Format(currentProfile.Volume)}%";
            _saveCurrent.Enabled = current is not null;
            _currentVolume.Enabled = current is not null;
            _applyCurrent.Enabled = current is not null;
            if (current is not null && !editingCurrent)
                _currentVolume.Value = VolumePercent.ToDecimal(current.Volume);
            _status.Text = $"● {status}";

            var selected = SelectedProfile;
            var editingProfile = _profileVolume.ContainsFocus ? _profileVolume.Value : (decimal?)null;
            _profiles.BeginUpdate();
            try
            {
                _profiles.Items.Clear();
                foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.CurrentCulture))
                {
                    var item = new ListViewItem(profile.Name) { Tag = profile };
                    item.SubItems.Add($"{VolumePercent.Format(profile.Volume)}%");
                    _profiles.Items.Add(item);
                    if (ReferenceEquals(profile, selected)) item.Selected = true;
                }
            }
            finally { _profiles.EndUpdate(); }

            _paused.Checked = paused;
            _startup.Checked = startup;
            UpdateSelection();
            if (editingProfile.HasValue && ReferenceEquals(SelectedProfile, selected))
                _profileVolume.Value = editingProfile.Value;
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
            _profileVolume.Value = VolumePercent.ToDecimal(profile.Volume);
    }

    private void ResizeProfileColumns()
    {
        if (_profiles.Columns.Count < 2) return;
        var volumeWidth = Math.Max(180, TextRenderer.MeasureText("保存音量", _profiles.Font).Width + 40);
        _profiles.Columns[1].Width = volumeWidth;
        _profiles.Columns[0].Width = Math.Max(180, _profiles.ClientSize.Width - volumeWidth - 6);
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _previousWindowState = WindowState;
            _previousBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var screen = Screen.FromControl(this).Bounds;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screen;
            _fullscreen = true;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _previousBounds;
            WindowState = _previousWindowState;
            _fullscreen = false;
        }
    }

    private void LoadDotFont()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VolumeKeeper.Fonts.DotGothic16-Regular.ttf")
            ?? throw new InvalidOperationException("ドットフォントが見つかりません。");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        _fontData = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        _fontCollection.AddMemoryFont(_fontData.AddrOfPinnedObject(), bytes.Length);
        // ListView and NumericUpDown render through GDI, so they also need a GDI font resource.
        _gdiFontResource = AddFontMemResourceEx(_fontData.AddrOfPinnedObject(),
            (uint)bytes.Length, IntPtr.Zero, out var fontCount);
        if (_gdiFontResource == IntPtr.Zero || fontCount == 0)
            throw new InvalidOperationException("ドットフォントを Windows に登録できません。");
    }

    private Font DotFont(float size)
    {
        if (!_fonts.TryGetValue(size, out var font))
        {
            font = new Font(_fontCollection.Families[0], size, FontStyle.Regular, GraphicsUnit.Point);
            _fonts.Add(size, font);
        }
        return font;
    }

    private Label Label(float size, ContentAlignment align, string text = "") => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        TextAlign = align,
        ForeColor = Ink,
        Font = DotFont(size),
        Margin = Padding.Empty,
        UseCompatibleTextRendering = true
    };

    private Button Button(string text, int width, bool secondary = false)
    {
        var font = DotFont(11);
        var measuredWidth = TextRenderer.MeasureText(text, font).Width + 32;
        var button = new Button
        {
            Text = text,
            Size = new Size(Math.Max(width, measuredWidth), Math.Max(44, font.Height + 14)),
            FlatStyle = FlatStyle.Flat,
            BackColor = secondary ? Color.White : Accent,
            ForeColor = secondary ? Ink : Color.White,
            Font = font,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 12, 0),
            UseVisualStyleBackColor = false,
            UseCompatibleTextRendering = true
        };
        button.FlatAppearance.BorderColor = secondary ? Border : Accent;
        return button;
    }

    private CheckBox CheckBox(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Ink,
        Font = DotFont(11),
        AutoSize = false,
        Padding = new Padding(4, 0, 0, 0),
        UseCompatibleTextRendering = true
    };

    private NumericUpDown VolumeInput() => new()
    {
        Size = new Size(110, Math.Max(44, DotFont(12).Height + 14)),
        Minimum = 0,
        Maximum = 100,
        DecimalPlaces = 2,
        Increment = 0.01m,
        Enabled = false,
        Font = DotFont(12),
        BackColor = Color.White,
        ForeColor = Ink,
        TextAlign = HorizontalAlignment.Center,
        Margin = new Padding(0, 3, 4, 0)
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deviceTip.Dispose();
            base.Dispose(disposing);
            foreach (var font in _fonts.Values) font.Dispose();
            _fontCollection.Dispose();
            if (_gdiFontResource != IntPtr.Zero) RemoveFontMemResourceEx(_gdiFontResource);
            if (_fontData.IsAllocated) _fontData.Free();
        }
        else base.Dispose(disposing);
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr AddFontMemResourceEx(
        IntPtr fontData, uint fontSize, IntPtr reserved, out uint fontCount);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveFontMemResourceEx(IntPtr resourceHandle);

    private sealed class SurfacePanel : Panel
    {
        public SurfacePanel()
        {
            BackColor = Paper;
            Padding = Padding.Empty;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
