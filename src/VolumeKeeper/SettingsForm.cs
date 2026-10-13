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
    private readonly Label _device;
    private readonly Label _volume;
    private readonly Label _saved;
    private readonly Label _status;
    private readonly ListView _profiles;
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
        var working = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(Math.Min(800, working.Width), Math.Min(660, working.Height));
        Size = new Size(Math.Min(1180, working.Width), Math.Min(800, working.Height));
        StartPosition = FormStartPosition.Manual;
        Location = new Point(working.Left + (working.Width - Width) / 2,
            working.Top + (working.Height - Height) / 2);

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
        _profiles.Columns.Add("保存音量", 145);
        _profiles.Resize += (_, _) => ResizeProfileColumns();
        _profileVolume = new NumericUpDown
        {
            Size = new Size(90, 40),
            Minimum = 0,
            Maximum = 100,
            Enabled = false,
            Font = DotFont(12),
            BackColor = Color.White,
            ForeColor = Ink,
            TextAlign = HorizontalAlignment.Center,
            Margin = new Padding(0, 3, 4, 0)
        };
        _saveProfile = Button("保存音量を変更", 185);
        _deleteProfile = Button("選択した設定を削除", 220, secondary: true);
        _saveCurrent = Button("現在の音量を保存", 210);
        _paused = CheckBox("自動適用を一時停止");
        _startup = CheckBox("Windows ログイン時に起動");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 18),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Sky
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(Label(25, ContentAlignment.MiddleLeft, "VOLUMEKEEPER"), 0, 0);
        header.Controls.Add(Label(10, ContentAlignment.MiddleRight, "F11 全画面 / Esc 戻る"), 1, 0);
        header.Controls.Add(Label(11, ContentAlignment.TopLeft, "出力先ごとに、いつもの音量を。"), 0, 1);
        root.Controls.Add(header, 0, 0);

        var currentCard = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
        var currentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 13, 22, 16),
            ColumnCount = 2,
            RowCount = 3
        };
        currentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        currentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        currentLayout.Controls.Add(Label(11, ContentAlignment.MiddleLeft, "● 現在の出力先"), 0, 0);
        currentLayout.Controls.Add(_device, 0, 1);
        currentLayout.Controls.Add(_saved, 0, 2);
        currentLayout.Controls.Add(_volume, 1, 1);
        _saveCurrent.Dock = DockStyle.Right;
        currentLayout.Controls.Add(_saveCurrent, 1, 2);
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
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        profilesLayout.Controls.Add(Label(14, ContentAlignment.MiddleLeft, "保存済みの機器"), 0, 0);
        profilesLayout.Controls.Add(_profiles, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        actions.Controls.Add(_profileVolume);
        actions.Controls.Add(new Label { Text = "%", Font = DotFont(12), ForeColor = Ink, Size = new Size(30, 40), TextAlign = ContentAlignment.MiddleLeft });
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
            _device.Text = current?.Name ?? "出力先がありません";
            _deviceTip.SetToolTip(_device, current?.Name ?? "");
            _volume.Text = current is null ? "音量 —" :
                $"音量 {Percent(current.Volume)}%{(current.Muted ? "（ミュート中）" : "")}";
            _saved.Text = current is null ? "保存音量 —" : currentProfile is null ?
                "保存音量 未登録" : $"保存音量 {Percent(currentProfile.Volume)}%";
            _saveCurrent.Enabled = current is not null;
            _status.Text = $"● {status}";

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

    private void ResizeProfileColumns()
    {
        if (_profiles.Columns.Count < 2) return;
        _profiles.Columns[1].Width = 145;
        _profiles.Columns[0].Width = Math.Max(180, _profiles.ClientSize.Width - 150);
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
        Margin = Padding.Empty
    };

    private Button Button(string text, int width, bool secondary = false) => new()
    {
        Text = text,
        Size = new Size(width, 40),
        FlatStyle = FlatStyle.Flat,
        BackColor = secondary ? Color.White : Accent,
        ForeColor = secondary ? Ink : Color.White,
        Font = DotFont(11),
        Cursor = Cursors.Hand,
        Margin = new Padding(0, 0, 12, 0),
        UseVisualStyleBackColor = false
    };

    private CheckBox CheckBox(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Ink,
        Font = DotFont(11),
        AutoSize = false,
        Padding = new Padding(4, 0, 0, 0)
    };

    private static int Percent(float level) => (int)Math.Round(level * 100, MidpointRounding.AwayFromZero);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deviceTip.Dispose();
            base.Dispose(disposing);
            foreach (var font in _fonts.Values) font.Dispose();
            _fontCollection.Dispose();
            if (_fontData.IsAllocated) _fontData.Free();
        }
        else base.Dispose(disposing);
    }

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
