using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace PhotoSaverAnimated;

public sealed class ConfigForm : Form
{
    // ── Colour palette ────────────────────────────────────────────────────────
    private static readonly Color C_BG      = Color.FromArgb(15, 15, 28);
    private static readonly Color C_HEADER  = Color.FromArgb(30, 95, 210);
    private static readonly Color C_FOOTER  = Color.FromArgb(10, 10, 22);
    private static readonly Color C_INPUT   = Color.FromArgb(36, 36, 58);
    private static readonly Color C_TEXT    = Color.FromArgb(220, 228, 244);
    private static readonly Color C_MUTED   = Color.FromArgb(130, 145, 175);
    private static readonly Color C_LABEL   = Color.FromArgb(90, 125, 200);
    private static readonly Color C_GREEN   = Color.FromArgb(34, 197, 100);
    private static readonly Color C_ORANGE  = Color.FromArgb(251, 146, 60);
    private static readonly Color C_RED     = Color.FromArgb(239, 68, 68);
    private static readonly Color C_ACCENT  = Color.FromArgb(59, 130, 246);
    private static readonly Color C_DIV     = Color.FromArgb(40, 40, 65);
    private static readonly Color C_CREDIT  = Color.FromArgb(80, 95, 130);

    private readonly TextBox       _folderBox;
    private readonly NumericUpDown _intervalSpin;
    private readonly NumericUpDown _maxPhotosSpin;
    private readonly Label         _countLabel;
    private readonly RadioButton   _rbAll;
    private readonly RadioButton   _rbPrimary;
    private readonly CheckBox      _chkRolling;
    private readonly RadioButton   _rbPolaroid;
    private readonly RadioButton   _rbThinBorder;
    private readonly RadioButton   _rbNoBorder;
    private readonly TextBox       _bgFileBox;
    private readonly Label         _bgTypeLabel;

    public ConfigForm()
    {
        Text            = "Photo Screensaver Animated — Settings";
        ClientSize      = new Size(460, 560);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = C_BG;
        ForeColor       = C_TEXT;
        Font            = new Font("Segoe UI", 9.5f);

        // ── Header ───────────────────────────────────────────────────────────
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = C_HEADER };
        header.Paint += PaintHeader;

        // ── Footer ───────────────────────────────────────────────────────────
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = C_FOOTER };
        footer.Paint += PaintFooter;

        var creditLabel = new Label
        {
            Text      = "by Jose-Jorge HERNANDEZ",
            Location  = new Point(18, 26),
            AutoSize  = true,
            ForeColor = C_CREDIT,
            Font      = new Font("Segoe UI", 8f, FontStyle.Italic),
        };

        var cancelBtn = FlatBtn("Cancel", 258, 22, 90, 34, C_INPUT, C_TEXT);
        cancelBtn.FlatAppearance.BorderSize  = 1;
        cancelBtn.FlatAppearance.BorderColor = C_DIV;
        cancelBtn.Click += (_, _) => Close();

        var saveBtn = FlatBtn("Save", 357, 22, 82, 34, C_ACCENT, Color.White);
        saveBtn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.Click += (_, _) => { OnSave(); Close(); };

        footer.Controls.AddRange(new Control[] { creditLabel, cancelBtn, saveBtn });

        // ── Scrollable content ────────────────────────────────────────────────
        var scroll = new Panel
        {
            Dock              = DockStyle.Fill,
            AutoScroll        = true,
            AutoScrollMargin  = new Size(0, 20),
            BackColor         = C_BG,
        };

        // Section: Media folder
        scroll.Controls.Add(SectionLabel("MEDIA FOLDER", 22, 16));
        _folderBox = new TextBox
        {
            Location    = new Point(22, 36),
            Size        = new Size(316, 28),
            BackColor   = C_INPUT,
            ForeColor   = C_TEXT,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly    = true,
            Cursor      = Cursors.Arrow,
            Font        = new Font("Segoe UI", 9f),
        };
        scroll.Controls.Add(_folderBox);

        var browseBtn = FlatBtn("Browse…", 346, 36, 92, 28, C_INPUT, C_TEXT);
        browseBtn.FlatAppearance.BorderSize  = 1;
        browseBtn.FlatAppearance.BorderColor = C_ACCENT;
        browseBtn.Click += OnBrowse;
        scroll.Controls.Add(browseBtn);

        _countLabel = new Label
        {
            Location  = new Point(24, 70),
            Size      = new Size(420, 18),
            Font      = new Font("Segoe UI", 8.5f),
            ForeColor = C_MUTED,
        };
        scroll.Controls.Add(_countLabel);

        scroll.Controls.Add(Divider(22, 97, 418));

        // Section: Interval
        scroll.Controls.Add(SectionLabel("SECONDS BETWEEN PHOTOS", 22, 113));
        scroll.Controls.Add(MutedLabel("Gap before the next photo flies in", 24, 132));
        _intervalSpin = StyledSpin(386, 113, 1, 60);
        scroll.Controls.Add(_intervalSpin);

        scroll.Controls.Add(Divider(22, 157, 418));

        // Section: Max photos
        scroll.Controls.Add(SectionLabel("MAX PHOTOS ON SCREEN", 22, 173));
        scroll.Controls.Add(MutedLabel("Cards (photos, GIFs, videos) accumulate to this limit, then reset", 24, 192));
        _maxPhotosSpin = StyledSpin(386, 173, 1, 20);
        scroll.Controls.Add(_maxPhotosSpin);

        scroll.Controls.Add(Divider(22, 218, 418));

        // Section: Monitor display
        scroll.Controls.Add(SectionLabel("DISPLAY MONITORS", 22, 234));

        _rbAll = new RadioButton
        {
            Text      = "All active monitors",
            Location  = new Point(22, 254),
            AutoSize  = true,
            ForeColor = C_TEXT,
            BackColor = C_BG,
            Font      = new Font("Segoe UI", 9.5f),
        };
        _rbPrimary = new RadioButton
        {
            Text      = "Primary monitor only",
            Location  = new Point(22, 276),
            AutoSize  = true,
            ForeColor = C_TEXT,
            BackColor = C_BG,
            Font      = new Font("Segoe UI", 9.5f),
        };
        int screenCount = Screen.AllScreens.Length;
        scroll.Controls.Add(MutedLabel(
            screenCount > 1
                ? $"{screenCount} monitors detected on this system"
                : "Only 1 monitor detected — multi-monitor has no effect",
            22, 298));
        scroll.Controls.Add(_rbAll);
        scroll.Controls.Add(_rbPrimary);

        scroll.Controls.Add(Divider(22, 316, 418));

        // Section: Display cycle (rolling mode)
        scroll.Controls.Add(SectionLabel("DISPLAY CYCLE", 22, 332));
        _chkRolling = new CheckBox
        {
            Text      = "Continuous — new photos replace the oldest card",
            Location  = new Point(22, 350),
            Size      = new Size(418, 19),
            ForeColor = C_TEXT,
            BackColor = C_BG,
            Font      = new Font("Segoe UI", 9.5f),
        };
        scroll.Controls.Add(_chkRolling);
        scroll.Controls.Add(MutedLabel("Off: photos accumulate until the screen resets. On: endless rolling flow.", 24, 372));

        scroll.Controls.Add(Divider(22, 396, 418));

        // Section: Card border style
        scroll.Controls.Add(SectionLabel("CARD BORDER", 22, 412));
        _rbPolaroid = new RadioButton
        {
            Text      = "Polaroid — classic instant-photo look",
            Location  = new Point(22, 430),
            AutoSize  = true,
            ForeColor = C_TEXT,
            BackColor = C_BG,
            Font      = new Font("Segoe UI", 9.5f),
        };
        _rbThinBorder = new RadioButton
        {
            Text      = "Thin white border",
            Location  = new Point(22, 450),
            AutoSize  = true,
            ForeColor = C_TEXT,
            BackColor = C_BG,
            Font      = new Font("Segoe UI", 9.5f),
        };
        _rbNoBorder = new RadioButton
        {
            Text      = "No border — photo only",
            Location  = new Point(22, 470),
            AutoSize  = true,
            ForeColor = C_TEXT,
            BackColor = C_BG,
            Font      = new Font("Segoe UI", 9.5f),
        };
        scroll.Controls.Add(_rbPolaroid);
        scroll.Controls.Add(_rbThinBorder);
        scroll.Controls.Add(_rbNoBorder);

        scroll.Controls.Add(Divider(22, 497, 418));

        // Section: Animated background
        scroll.Controls.Add(SectionLabel("BACKGROUND", 22, 513));
        scroll.Controls.Add(MutedLabel("A media file to play behind the cards — GIF, WebP, video or still image.", 24, 530));

        _bgFileBox = new TextBox
        {
            Location    = new Point(22, 548),
            Size        = new Size(246, 28),
            BackColor   = C_INPUT,
            ForeColor   = C_TEXT,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly    = true,
            Cursor      = Cursors.Arrow,
            Font        = new Font("Segoe UI", 9f),
        };
        scroll.Controls.Add(_bgFileBox);

        var bgBrowseBtn = FlatBtn("Browse…", 276, 548, 76, 28, C_INPUT, C_TEXT);
        bgBrowseBtn.FlatAppearance.BorderSize  = 1;
        bgBrowseBtn.FlatAppearance.BorderColor = C_ACCENT;
        bgBrowseBtn.Click += OnBrowseBackground;
        scroll.Controls.Add(bgBrowseBtn);

        var bgClearBtn = FlatBtn("Clear", 360, 548, 60, 28, C_INPUT, C_MUTED);
        bgClearBtn.FlatAppearance.BorderSize  = 1;
        bgClearBtn.FlatAppearance.BorderColor = C_DIV;
        bgClearBtn.Click += (_, _) => { _bgFileBox.Text = ""; _bgTypeLabel.Text = "No background set"; _bgTypeLabel.ForeColor = C_MUTED; };
        scroll.Controls.Add(bgClearBtn);

        _bgTypeLabel = new Label
        {
            Location  = new Point(24, 582),
            Size      = new Size(414, 18),
            Font      = new Font("Segoe UI", 8.5f),
            ForeColor = C_MUTED,
            Text      = "No background set",
        };
        scroll.Controls.Add(_bgTypeLabel);

        // Controls added in dock order: Fill panel last
        Controls.Add(header);
        Controls.Add(footer);
        Controls.Add(scroll);

        AcceptButton = saveBtn;
        CancelButton = cancelBtn;

        Load += (_, _) => LoadSettings();
    }

    // ── Header / footer paint ─────────────────────────────────────────────────

    private static void PaintHeader(object? sender, PaintEventArgs e)
    {
        var panel = (Panel)sender!;
        var g     = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var shimmer = new LinearGradientBrush(
            panel.ClientRectangle,
            Color.FromArgb(55, 255, 255, 255),
            Color.FromArgb(0,  255, 255, 255),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(shimmer, panel.ClientRectangle);

        using var line = new Pen(Color.FromArgb(60, 255, 255, 255));
        g.DrawLine(line, 0, panel.Height - 1, panel.Width, panel.Height - 1);

        using var titleFont = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point);
        using var subFont   = new Font("Segoe UI",  9f, FontStyle.Regular, GraphicsUnit.Point);
        g.DrawString("Photo Screensaver Animated", titleFont, Brushes.White, 22f, 13f);
        g.DrawString("Photos · GIFs · WebPs · Videos — configure below, then click Save.",
            subFont, new SolidBrush(Color.FromArgb(190, 255, 255, 255)), 24f, 46f);
    }

    private static void PaintFooter(object? sender, PaintEventArgs e) =>
        e.Graphics.DrawLine(new Pen(Color.FromArgb(40, 40, 65), 1), 0, 0, ((Panel)sender!).Width, 0);

    // ── Settings load / save ──────────────────────────────────────────────────

    private void LoadSettings()
    {
        _folderBox.Text      = AppSettings.PhotoFolder;
        _intervalSpin.Value  = Math.Clamp(AppSettings.LaunchIntervalSeconds, 1, 60);
        _maxPhotosSpin.Value = Math.Clamp(AppSettings.MaxPhotosOnScreen,     1, 20);
        _rbPrimary.Checked   = AppSettings.MonitorDisplay == MonitorMode.Primary;
        _rbAll.Checked       = !_rbPrimary.Checked;
        _chkRolling.Checked  = AppSettings.RollingMode;
        (_rbPolaroid.Checked, _rbThinBorder.Checked, _rbNoBorder.Checked) = AppSettings.CardBorder switch
        {
            CardBorderStyle.Thin => (false, true,  false),
            CardBorderStyle.None => (false, false, true),
            _                    => (true,  false, false),
        };
        _bgFileBox.Text = AppSettings.BackgroundFile;
        UpdateBgTypeLabel(AppSettings.BackgroundFile);
        RefreshCount(_folderBox.Text);
    }

    private void OnSave()
    {
        AppSettings.PhotoFolder           = _folderBox.Text;
        AppSettings.LaunchIntervalSeconds = (int)_intervalSpin.Value;
        AppSettings.MaxPhotosOnScreen     = (int)_maxPhotosSpin.Value;
        AppSettings.MonitorDisplay        = _rbPrimary.Checked ? MonitorMode.Primary : MonitorMode.All;
        AppSettings.RollingMode           = _chkRolling.Checked;
        AppSettings.CardBorder            = _rbThinBorder.Checked ? CardBorderStyle.Thin
                                          : _rbNoBorder.Checked   ? CardBorderStyle.None
                                          :                         CardBorderStyle.Polaroid;
        AppSettings.BackgroundFile        = _bgFileBox.Text;
    }

    private void OnBrowse(object? s, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description            = "Select the folder containing your media files",
            SelectedPath           = _folderBox.Text,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _folderBox.Text = dlg.SelectedPath;
            RefreshCount(dlg.SelectedPath);
        }
    }

    private void OnBrowseBackground(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Select background media",
            Filter = "Media files|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.m4v;*.flv|All files|*.*",
            FileName = _bgFileBox.Text,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _bgFileBox.Text = dlg.FileName;
            UpdateBgTypeLabel(dlg.FileName);
        }
    }

    private void UpdateBgTypeLabel(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            _bgTypeLabel.Text = "No background set"; _bgTypeLabel.ForeColor = C_MUTED; return;
        }
        if (!File.Exists(path))
        {
            _bgTypeLabel.Text = "File not found"; _bgTypeLabel.ForeColor = C_RED; return;
        }
        var ext  = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
        var kind = ext is ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" or ".m4v" or ".flv"
            ? "Video"
            : ext is ".gif" or ".webp" ? "Animated image" : "Still image";
        _bgTypeLabel.Text      = $"✓  {kind} — {Path.GetFileName(path) ?? ""}";
        _bgTypeLabel.ForeColor = C_GREEN;
    }

    private static readonly string[] AllMediaExts =
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".m4v", ".flv" };

    private void RefreshCount(string folder)
    {
        if (!Directory.Exists(folder))
        {
            _countLabel.Text = "Folder not found"; _countLabel.ForeColor = C_RED; return;
        }
        try
        {
            int n = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Count(f => AllMediaExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

            (_countLabel.Text, _countLabel.ForeColor) = n switch
            {
                0 => ("No supported media found — choose a different folder", C_ORANGE),
                1 => ("✓  1 media file found",                                C_GREEN),
                _ => ($"✓  {n} media files found",                            C_GREEN),
            };
        }
        catch { _countLabel.Text = "Unable to read folder"; _countLabel.ForeColor = C_RED; }
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private static Label SectionLabel(string text, int x, int y) => new Label
    {
        Text = text, Location = new Point(x, y), AutoSize = true,
        ForeColor = C_LABEL, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
    };

    private static Label MutedLabel(string text, int x, int y) => new Label
    {
        Text = text, Location = new Point(x, y), AutoSize = true,
        ForeColor = C_MUTED, Font = new Font("Segoe UI", 8.5f),
    };

    private static Panel Divider(int x, int y, int w) => new Panel
    {
        Location = new Point(x, y), Size = new Size(w, 1), BackColor = C_DIV,
    };

    private static Button FlatBtn(string text, int x, int y, int w, int h, Color bg, Color fg)
    {
        var b = new Button
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg,
            Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9.5f),
        };
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.1f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.05f);
        return b;
    }

    private static NumericUpDown StyledSpin(int x, int y, int min, int max) => new NumericUpDown
    {
        Location = new Point(x, y), Size = new Size(54, 26),
        Minimum = min, Maximum = max, DecimalPlaces = 0,
        BackColor = C_INPUT, ForeColor = C_TEXT,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f), TextAlign = HorizontalAlignment.Center,
    };
}
