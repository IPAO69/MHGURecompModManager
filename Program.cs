using System.Diagnostics;
using System.Drawing.Text;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace MHGUModManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const string ManagerMarker = "# Written by the Mods tab";
    private const int RuntimeModLimit = 256;
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MHGU Mod Manager", "settings.json");

    private readonly TextBox _gamePath = new();
    private readonly Label _pathStatus = new();
    private readonly Label _footer = new();
    private readonly ListView _modsList = new();
    private readonly Button _moveUp = new();
    private readonly Button _moveDown = new();
    private readonly Button _remove = new();
    private readonly Button _themeToggle = new();
    private readonly Button _languageToggle = new();
    private readonly Button _launchButton = new();
    private readonly Dictionary<Control, (string Thai, string English)> _localizedControls = new();
    private string _modsDirectory = "";
    private List<ModEntry> _mods = new();
    private bool _darkMode = true;
    private bool _english = true;
    private bool _footerIsStatus;
    private bool _footerStatusOk;

    private static readonly string UiFontName = FindInstalledFont("Segoe UI Variable Text", "Segoe UI", "Tahoma");
    private static readonly string DisplayFontName = FindInstalledFont("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI");

    private ThemePalette Palette => _darkMode ? ThemePalette.Dark : ThemePalette.Light;
    private Color Ink => Palette.Text;
    private Color Muted => Palette.Muted;
    private Color Blue => Palette.Primary;
    private Color Green => Palette.Success;
    private Color Red => Palette.Danger;

    private static string FindInstalledFont(params string[] candidates)
    {
        using var fonts = new InstalledFontCollection();
        var installed = fonts.Families.Select(family => family.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(installed.Contains) ?? "Segoe UI";
    }

    public MainForm()
    {
        Text = "MHGU Mod Manager";
        MinimumSize = new Size(840, 580);
        Size = new Size(1020, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Palette.Background;
        Font = new Font(UiFontName, 10F);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        BuildLayout();
        LoadSavedPath();
        ApplyLanguage();
        ApplyTheme();
        RefreshMods();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22, 18, 22, 16),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        var heading = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = Localized("MHGU Mod Manager", "MHGU Mod Manager"),
            Font = new Font(DisplayFontName, 23F, FontStyle.Bold),
            ForeColor = Ink,
            Tag = "title",
            AutoSize = true,
            Location = new Point(0, 0)
        };
        var subtitle = new Label
        {
            Text = "",
            ForeColor = Muted,
            Tag = "muted",
            Font = new Font(UiFontName, 10.25F),
            AutoSize = true,
            Location = new Point(2, 39)
        };
        Localize(subtitle, "ติดตั้ง ตรวจสอบ และเปิด/ปิดม็อดของ MHGURecomp", "Install, inspect, and toggle MHGURecomp mods");
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        _themeToggle.Text = "";
        StyleButton(_themeToggle, false);
        _themeToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _themeToggle.Size = new Size(104, 36);
        _themeToggle.Click += (_, _) => ToggleTheme();
        heading.Controls.Add(_themeToggle);
        Localize(_themeToggle, "โหมดสว่าง", "Light mode");
        _languageToggle.Text = "";
        _languageToggle.Size = new Size(88, 36);
        StyleButton(_languageToggle, false);
        _languageToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _languageToggle.Click += (_, _) => ToggleLanguage();
        heading.Controls.Add(_languageToggle);
        StyleButton(_launchButton, true);
        _launchButton.Size = new Size(124, 36);
        Localize(_launchButton, "เปิดเกม", "Open game");
        _launchButton.Click += (_, _) => LaunchGame();
        heading.Controls.Add(_launchButton);
        heading.Resize += (_, _) => PositionHeaderButtons(heading);
        PositionHeaderButtons(heading);
        root.Controls.Add(heading, 0, 0);

        var pathCard = Card();
        pathCard.Padding = new Padding(14, 10, 14, 10);
        var pathLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            BackColor = Palette.Surface,
            Tag = "surface"
        };
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        pathLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        pathLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        pathLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var pathLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            ForeColor = Ink,
            Tag = "label",
            Font = new Font(UiFontName, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Localize(pathLabel, "ตำแหน่งติดตั้ง MHGURecomp", "MHGURecomp install folder");
        pathLabel.Tag = "label";
        _gamePath.Dock = DockStyle.Fill;
        _gamePath.Margin = new Padding(0, 3, 8, 3);
        _gamePath.Font = new Font(UiFontName, 10F);
        _gamePath.Tag = "input";
        var browse = MakeButton("", false);
        Localize(browse, "เลือกโฟลเดอร์", "Browse");
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(0, 2, 8, 2);
        browse.Click += (_, _) => ChooseGameFolder();
        var check = MakeButton("", true);
        Localize(check, "ตรวจสอบ", "Check");
        check.Dock = DockStyle.Fill;
        check.Margin = new Padding(0, 2, 0, 2);
        check.Click += (_, _) => CheckGameFolder();
        _pathStatus.Dock = DockStyle.Fill;
        _pathStatus.TextAlign = ContentAlignment.MiddleLeft;
        _pathStatus.ForeColor = Muted;
        _pathStatus.Tag = "path-status";
        _pathStatus.AutoEllipsis = true;
        pathLayout.Controls.Add(pathLabel, 0, 0);
        pathLayout.Controls.Add(_gamePath, 0, 1);
        pathLayout.Controls.Add(browse, 1, 1);
        pathLayout.Controls.Add(check, 2, 1);
        pathLayout.Controls.Add(_pathStatus, 0, 2);
        pathLayout.SetColumnSpan(_pathStatus, 3);
        pathCard.Controls.Add(pathLayout);
        root.Controls.Add(pathCard, 0, 1);

        var modsArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 12, 0, 0),
            BackColor = BackColor
        };
        modsArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        modsArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        modsArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 1, 0, 6),
            BackColor = BackColor
        };
        var importZip = MakeButton("", true);
        Localize(importZip, "ติดตั้ง ZIP / 7Z", "Install ZIP / 7Z");
        importZip.Click += (_, _) => ChooseArchiveFiles();
        var importFolder = MakeButton("", false);
        Localize(importFolder, "เพิ่มโฟลเดอร์", "Add folder");
        importFolder.Click += (_, _) => ChooseModFolder();
        var openMods = MakeButton("", false);
        Localize(openMods, "เปิดโฟลเดอร์ mods", "Open mods folder");
        openMods.Click += (_, _) => OpenModsFolder();
        Localize(_moveUp, "ขึ้น ↑", "Move up ↑");
        StyleButton(_moveUp, false);
        _moveUp.Click += (_, _) => MoveSelected(-1);
        Localize(_moveDown, "ลง ↓", "Move down ↓");
        StyleButton(_moveDown, false);
        _moveDown.Click += (_, _) => MoveSelected(1);
        Localize(_remove, "ลบม็อด", "Remove mod");
        StyleButton(_remove, false, Red);
        _remove.Click += (_, _) => RemoveSelectedMod();
        foreach (var button in new[] { importZip, importFolder, openMods, _moveUp, _moveDown, _remove })
        {
            button.Margin = new Padding(0, 0, 8, 0);
            toolbar.Controls.Add(button);
        }

        var listCard = Card();
        listCard.Padding = new Padding(8);
        _modsList.Dock = DockStyle.Fill;
        _modsList.View = View.Details;
        _modsList.FullRowSelect = true;
        _modsList.GridLines = false;
        _modsList.HideSelection = false;
        _modsList.MultiSelect = false;
        _modsList.OwnerDraw = true;
        _modsList.BorderStyle = BorderStyle.None;
        _modsList.Font = new Font(UiFontName, 10F);
        _modsList.Columns.Add("ชื่อม็อด", 390);
        _modsList.Columns.Add("เปิด / ปิด", 130);
        _modsList.Columns.Add("โครงสร้างที่ตรวจพบ", 260);
        _modsList.SmallImageList = new ImageList { ImageSize = new Size(1, 34), ColorDepth = ColorDepth.Depth32Bit };
        _modsList.BackColor = Palette.Surface;
        _modsList.ForeColor = Palette.Text;
        _modsList.DrawColumnHeader += DrawModColumnHeader;
        _modsList.DrawItem += (_, _) => { };
        _modsList.DrawSubItem += DrawModSubItem;
        _modsList.MouseClick += OnModListMouseClick;
        _modsList.SelectedIndexChanged += (_, _) => UpdateToolbarState();
        listCard.Controls.Add(_modsList);
        modsArea.Controls.Add(toolbar, 0, 0);
        modsArea.Controls.Add(listCard, 0, 1);

        var dropZone = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Drop,
            BorderStyle = BorderStyle.None,
            Tag = "drop",
            AllowDrop = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        var dropLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            ForeColor = Palette.DropText,
            Font = new Font(UiFontName, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = "drop-text",
            AllowDrop = true
        };
        Localize(dropLabel,
            "ลากไฟล์ ZIP / 7Z หรือโฟลเดอร์มาวางที่นี่  ·  ตรวจหา romfs / exefs ให้อัตโนมัติ",
            "Drop a ZIP / 7Z archive or mod folder here  ·  romfs / exefs are checked automatically");
        dropZone.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, dropZone.ClientRectangle,
            Palette.Border, ButtonBorderStyle.Solid);
        dropZone.Controls.Add(dropLabel);
        dropZone.DragEnter += OnDragEnter;
        dropZone.DragDrop += OnDragDrop;
        dropLabel.DragEnter += OnDragEnter;
        dropLabel.DragDrop += OnDragDrop;
        modsArea.Controls.Add(dropZone, 0, 2);
        root.Controls.Add(modsArea, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill };
        _footer.Dock = DockStyle.Fill;
        _footer.ForeColor = Muted;
        _footer.Tag = "footer";
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.Text = "";
        footer.Controls.Add(_footer);
        root.Controls.Add(footer, 0, 3);
    }

    private Panel Card() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Palette.Surface,
        BorderStyle = BorderStyle.None,
        Tag = "surface",
        Margin = new Padding(0)
    };

    private Button MakeButton(string text, bool primary, Color? color = null)
    {
        var button = new Button { Text = text };
        StyleButton(button, primary, color);
        return button;
    }

    private void StyleButton(Button button, bool primary, Color? color = null)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(82, 36);
        button.Padding = new Padding(8, 2, 8, 2);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Palette.Border;
        button.Tag = color.HasValue ? "danger" : (primary ? "primary" : "button");
        button.BackColor = color ?? (primary ? Blue : Palette.Control);
        button.ForeColor = primary || color.HasValue ? Color.White : Ink;
        button.Font = new Font(UiFontName, 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private void LoadSavedPath()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var saved = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(_settingsPath));
                if (saved is not null)
                {
                    _darkMode = saved.DarkMode;
                    _english = saved.LanguageConfigured ? saved.English : true;
                }
                if (!string.IsNullOrWhiteSpace(saved?.GamePath) && Directory.Exists(saved.GamePath))
                {
                    _gamePath.Text = saved.GamePath;
                    return;
                }
            }
        }
        catch { }

        var localGame = @"D:\Windows-MHGURecomp";
        if (File.Exists(Path.Combine(localGame, "mhgurecomp.exe")))
            _gamePath.Text = localGame;
        else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "mhgurecomp.exe")))
            _gamePath.Text = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new ManagerSettings
            {
                GamePath = _gamePath.Text.Trim(),
                DarkMode = _darkMode,
                English = _english,
                LanguageConfigured = true
            }));
        }
        catch (Exception ex)
        {
            SetStatus(T($"บันทึกตำแหน่งเกมไม่ได้: {ex.Message}", $"Could not save game folder: {ex.Message}"), false);
        }
    }

    private void ToggleTheme()
    {
        _darkMode = !_darkMode;
        ApplyTheme();
        SaveSettings();
    }

    private void ToggleLanguage()
    {
        _english = !_english;
        ApplyLanguage();
        ApplyTheme();
        SaveSettings();
    }

    private void ApplyLanguage()
    {
        foreach (var pair in _localizedControls)
            pair.Key.Text = _english ? pair.Value.English : pair.Value.Thai;

        _languageToggle.Text = _english ? "ไทย" : "English";
        _themeToggle.Text = _darkMode ? T("โหมดสว่าง", "Light mode") : T("โหมดมืด", "Dark mode");
        if (_modsList.Columns.Count >= 3)
        {
            _modsList.Columns[0].Text = T("ชื่อม็อด", "Mod name");
            _modsList.Columns[1].Text = T("เปิด / ปิด", "Enabled");
            _modsList.Columns[2].Text = T("โครงสร้างที่ตรวจพบ", "Detected structure");
        }

        if (TryGetGamePaths(out _, out var mods))
            _pathStatus.Text = T($"พบ mhgurecomp.exe  ·  โฟลเดอร์ม็อด: {mods}", $"Found mhgurecomp.exe  ·  Mods folder: {mods}");
        else if (string.IsNullOrWhiteSpace(_gamePath.Text))
            _pathStatus.Text = T("เลือกโฟลเดอร์ที่มี mhgurecomp.exe เพื่อเริ่มใช้งาน", "Choose the folder containing mhgurecomp.exe to get started");
        else
            _pathStatus.Text = T("ไม่พบ mhgurecomp.exe — ตรวจสอบ path อีกครั้ง", "mhgurecomp.exe not found — check the folder path");
        RenderMods();
    }

    private void Localize(Control control, string thai, string english)
    {
        _localizedControls[control] = (thai, english);
        control.Text = _english ? english : thai;
    }

    private string Localized(string thai, string english) => _english ? english : thai;
    private string T(string thai, string english) => _english ? english : thai;

    private void PositionHeaderButtons(Panel heading)
    {
        _themeToggle.Location = new Point(Math.Max(0, heading.ClientSize.Width - _themeToggle.Width), 12);
        _languageToggle.Location = new Point(Math.Max(0, _themeToggle.Left - _languageToggle.Width - 8), 12);
        _launchButton.Location = new Point(Math.Max(0, _languageToggle.Left - _launchButton.Width - 8), 12);
    }

    private void ApplyTheme()
    {
        SuspendLayout();
        BackColor = Palette.Background;
        _themeToggle.Text = _darkMode ? T("โหมดสว่าง", "Light mode") : T("โหมดมืด", "Dark mode");
        ApplyThemeToControl(this);
        _modsList.Invalidate();
        Invalidate(true);
        ResumeLayout(true);
    }

    private void ApplyThemeToControl(Control control)
    {
        var role = control.Tag as string;
        switch (control)
        {
            case Button button:
                button.BackColor = role switch
                {
                    "primary" => Palette.Primary,
                    "danger" => Palette.Danger,
                    _ => Palette.Control
                };
                button.ForeColor = role is "primary" or "danger" ? Color.White : Palette.Text;
                button.FlatAppearance.BorderColor = Palette.Border;
                button.FlatAppearance.MouseOverBackColor = role switch
                {
                    "primary" => Palette.PrimaryHover,
                    "danger" => Palette.DangerHover,
                    _ => Palette.ControlHover
                };
                button.FlatAppearance.MouseDownBackColor = Palette.ControlPressed;
                break;
            case TextBox textBox:
                textBox.BackColor = Palette.Input;
                textBox.ForeColor = Palette.Text;
                break;
            case ListView listView:
                listView.BackColor = Palette.Surface;
                listView.ForeColor = Palette.Text;
                break;
            case Label label:
                label.ForeColor = role switch
                {
                    "title" or "label" => Palette.Text,
                    "muted" => Palette.Muted,
                    "drop-text" => Palette.DropText,
                    "path-status" when label.Text.Contains("ไม่พบ", StringComparison.Ordinal) || label.Text.Contains("not found", StringComparison.OrdinalIgnoreCase) => Palette.Danger,
                    "path-status" when label.Text.StartsWith("พบ", StringComparison.Ordinal) || label.Text.StartsWith("Found", StringComparison.OrdinalIgnoreCase) => Palette.Success,
                    "path-status" => Palette.Muted,
                    "footer" when _footerIsStatus => _footerStatusOk ? Palette.Success : Palette.Danger,
                    "footer" => Palette.Muted,
                    _ => Palette.Text
                };
                break;
            case Panel:
                control.BackColor = role switch
                {
                    "surface" => Palette.Surface,
                    "drop" => Palette.Drop,
                    _ => Palette.Background
                };
                break;
            default:
                control.BackColor = Palette.Background;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyThemeToControl(child);
    }

    private void DrawModColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(Palette.Header);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", _modsList.Font,
            Rectangle.Inflate(e.Bounds, -10, 0), Palette.HeaderText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        using var border = new Pen(Palette.Border);
        e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void ChooseGameFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = T("เลือกโฟลเดอร์ที่มี mhgurecomp.exe", "Choose the folder containing mhgurecomp.exe"), UseDescriptionForTitle = true };
        if (Directory.Exists(_gamePath.Text)) dialog.SelectedPath = _gamePath.Text;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _gamePath.Text = dialog.SelectedPath;
        SaveSettings();
        CheckGameFolder();
    }

    private bool TryGetGamePaths(out string gameRoot, out string modsPath)
    {
        gameRoot = _gamePath.Text.Trim().Trim('"');
        modsPath = "";
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return false;
        gameRoot = Path.GetFullPath(gameRoot);
        if (!File.Exists(Path.Combine(gameRoot, "mhgurecomp.exe"))) return false;
        modsPath = Path.Combine(gameRoot, "mods");
        return true;
    }

    private void CheckGameFolder()
    {
        if (!TryGetGamePaths(out var root, out var mods))
        {
            _modsDirectory = "";
            _mods.Clear();
            RenderMods();
            _pathStatus.ForeColor = Red;
            _pathStatus.Text = T("ไม่พบ mhgurecomp.exe — เลือกโฟลเดอร์ติดตั้งเกมก่อน", "mhgurecomp.exe not found — choose the game install folder");
            SetStatus(T("ตรวจสอบตำแหน่งเกมไม่ผ่าน", "Game folder check failed"), false);
            return;
        }

        _modsDirectory = mods;
        Directory.CreateDirectory(_modsDirectory);
        SaveSettings();
        RefreshMods();
        _pathStatus.ForeColor = Green;
        _pathStatus.Text = T($"พบ mhgurecomp.exe  ·  โฟลเดอร์ม็อด: {mods}", $"Found mhgurecomp.exe  ·  Mods folder: {mods}");
        SetStatus(T("ตรวจสอบตำแหน่งเกมเรียบร้อย", "Game folder checked"), true);
    }

    private void RefreshMods()
    {
        if (!TryGetGamePaths(out _, out var mods))
        {
            _modsDirectory = "";
            _mods.Clear();
            RenderMods();
            if (string.IsNullOrWhiteSpace(_gamePath.Text))
                _pathStatus.Text = T("เลือกโฟลเดอร์ที่มี mhgurecomp.exe เพื่อเริ่มใช้งาน", "Choose the folder containing mhgurecomp.exe to get started");
            else
            {
                _pathStatus.ForeColor = Red;
                _pathStatus.Text = T("ไม่พบ mhgurecomp.exe — ตรวจสอบ path อีกครั้ง", "mhgurecomp.exe not found — check the folder path");
            }
            return;
        }

        _modsDirectory = mods;
        Directory.CreateDirectory(_modsDirectory);
        try
        {
            _mods = ReadModList();
            RenderMods();
            _pathStatus.ForeColor = Green;
            _pathStatus.Text = T($"พบ mhgurecomp.exe  ·  โฟลเดอร์ม็อด: {_modsDirectory}", $"Found mhgurecomp.exe  ·  Mods folder: {_modsDirectory}");
        }
        catch (Exception ex)
        {
            SetStatus(T($"อ่านรายการม็อดไม่ได้: {ex.Message}", $"Could not read mod list: {ex.Message}"), false);
        }
    }

    private List<ModEntry> ReadModList()
    {
        var folderNames = Directory.GetDirectories(_modsDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith(".", StringComparison.Ordinal))
            .Cast<string>()
            .ToList();
        var orderPath = Path.Combine(_modsDirectory, "load_order.txt");
        var lines = File.Exists(orderPath) ? File.ReadAllLines(orderPath) : Array.Empty<string>();
        var managerOrder = lines.Length > 0 && lines[0].TrimStart().StartsWith(ManagerMarker, StringComparison.Ordinal);
        var explicitOrder = new List<ModEntry>();
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var enabled = true;
            if (managerOrder && line.StartsWith("# off: ", StringComparison.Ordinal))
            {
                enabled = false;
                line = line[7..].Trim();
            }
            else if (line.StartsWith('#')) continue;
            if (line.Length == 0 || !folderNames.Contains(line, StringComparer.OrdinalIgnoreCase)) continue;
            if (!states.ContainsKey(line))
            {
                states[line] = enabled;
                explicitOrder.Add(new ModEntry(line, enabled));
            }
        }

        var result = new List<ModEntry>();
        foreach (var entry in explicitOrder)
            result.Add(new ModEntry(entry.Name, entry.Enabled));

        foreach (var name in folderNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            if (states.ContainsKey(name)) continue;
            var on = !File.Exists(orderPath) ? !name.StartsWith('_') : managerOrder;
            result.Add(new ModEntry(name, on));
        }

        return result;
    }

    private void RenderMods()
    {
        _modsList.BeginUpdate();
        _modsList.Items.Clear();
        foreach (var mod in _mods)
        {
            var check = CheckLayout(Path.Combine(_modsDirectory, mod.Name), 3);
            var structure = DescribeLayout(check);
            var item = new ListViewItem(mod.Name) { Tag = mod };
                item.SubItems.Add(mod.Enabled ? T("เปิด", "On") : T("ปิด", "Off"));
            item.SubItems.Add(structure);
            if (!check.HasAny)
            {
                item.ForeColor = Palette.Warning;
                item.ToolTipText = T("ไม่พบ romfs, exefs หรือ nativeNX ในระดับที่เกมค้นหา", "romfs, exefs, or nativeNX was not found in a supported location");
            }
            _modsList.Items.Add(item);
        }
        _modsList.EndUpdate();
        UpdateToolbarState();
        _footerIsStatus = false;
        _footer.Text = _mods.Count == 0
            ? T("ยังไม่มีม็อด · ลาก ZIP / 7Z มาวาง หรือกด “ติดตั้ง ZIP”  ·  สวิตช์จะมีผลเมื่อเริ่มเกมใหม่", "No mods yet · Drop a ZIP / 7Z or click Install ZIP · Changes apply after restarting the game")
            : T($"มี {_mods.Count} ม็อด  ·  เปิด {_mods.Count(m => m.Enabled)}  ·  สวิตช์และลำดับมีผลเมื่อเริ่มเกมใหม่ ผ่าน mods\\load_order.txt", $"{_mods.Count} mods  ·  {_mods.Count(m => m.Enabled)} enabled  ·  Switches and order apply after restarting the game (mods\\load_order.txt)");
    }

    private void DrawModSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var item = e.Item;
        var subItem = e.SubItem;
        if (item is null || subItem is null) return;
        var selected = item.Selected;
        var bounds = e.Bounds;
        using var background = new SolidBrush(selected ? Palette.SelectedRow : Palette.ListRow);
        e.Graphics.FillRectangle(background, bounds);
        using (var border = new Pen(Palette.RowBorder))
            e.Graphics.DrawLine(border, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);

        if (e.ColumnIndex == 1 && item.Tag is ModEntry mod)
        {
            var track = new Rectangle(bounds.Left + 12, bounds.Top + (bounds.Height - 22) / 2, 44, 22);
            using var trackBrush = new SolidBrush(mod.Enabled ? Green : Palette.SwitchOff);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(trackBrush, track);
            var knobX = mod.Enabled ? track.Right - 19 : track.Left + 2;
            using var knobBrush = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(knobBrush, new Rectangle(knobX, track.Top + 2, 18, 18));
            TextRenderer.DrawText(e.Graphics, mod.Enabled ? T("เปิด", "On") : T("ปิด", "Off"), _modsList.Font,
                new Rectangle(track.Right + 8, bounds.Top, bounds.Right - track.Right - 8, bounds.Height),
                selected ? Ink : (mod.Enabled ? Green : Muted),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            return;
        }

        var textColor = selected ? Ink : item.ForeColor;
        TextRenderer.DrawText(e.Graphics, subItem.Text, _modsList.Font,
            Rectangle.Inflate(bounds, -10, 0), textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void OnModListMouseClick(object? sender, MouseEventArgs e)
    {
        var hit = _modsList.HitTest(e.Location);
        if (hit.Item is null || hit.SubItem is null) return;
        if (hit.Item.SubItems.IndexOf(hit.SubItem) != 1) return;
        var index = hit.Item.Index;
        if (index < 0 || index >= _mods.Count) return;
        _mods[index] = _mods[index] with { Enabled = !_mods[index].Enabled };
        SaveLoadOrderAndRefresh(T("บันทึกสวิตช์ม็อดแล้ว", "Mod switch saved"));
    }

    private void UpdateToolbarState()
    {
        var selected = _modsList.SelectedIndices.Count > 0;
        var i = selected ? _modsList.SelectedIndices[0] : -1;
        _moveUp.Enabled = selected && i > 0;
        _moveDown.Enabled = selected && i < _mods.Count - 1;
        _remove.Enabled = selected;
    }

    private void MoveSelected(int delta)
    {
        if (_modsList.SelectedIndices.Count == 0) return;
        var from = _modsList.SelectedIndices[0];
        var to = from + delta;
        if (to < 0 || to >= _mods.Count) return;
        (_mods[from], _mods[to]) = (_mods[to], _mods[from]);
        SaveLoadOrderAndRefresh(T("บันทึกลำดับม็อดแล้ว", "Mod order saved"));
        if (to < _modsList.Items.Count) _modsList.Items[to].Selected = true;
    }

    private void SaveLoadOrderAndRefresh(string message)
    {
        try
        {
            SaveLoadOrder();
            RenderMods();
            SetStatus(message, true);
        }
        catch (Exception ex)
        {
            SetStatus(T($"บันทึกสถานะม็อดไม่ได้: {ex.Message}", $"Could not save mod settings: {ex.Message}"), false);
            MessageBox.Show(this, ex.Message, T("บันทึก load_order.txt ไม่สำเร็จ", "Could not save load_order.txt"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshMods();
        }
    }

    private void SaveLoadOrder()
    {
        var orderPath = Path.Combine(_modsDirectory, "load_order.txt");
        var tempPath = orderPath + ".tmp";
        using (var writer = new StreamWriter(tempPath, false, new System.Text.UTF8Encoding(false)))
        {
            writer.WriteLine(ManagerMarker + " (also fine to edit by hand).");
            writer.WriteLine("# One mod folder per line, first to last: a file in a later mod wins.");
            writer.WriteLine("# A folder added later, that is neither listed nor marked off, loads after the listed ones.");
            foreach (var mod in _mods)
                writer.WriteLine(mod.Enabled ? mod.Name : "# off: " + mod.Name);
        }
        File.Move(tempPath, orderPath, true);
    }

    private void ChooseArchiveFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = T("เลือกไฟล์บีบอัดของม็อด", "Select mod archives"),
            Filter = T("ไฟล์ม็อด (*.zip;*.7z)|*.zip;*.7z|ZIP (*.zip)|*.zip|7-Zip (*.7z)|*.7z|ทุกไฟล์ (*.*)|*.*",
                "Mod archives (*.zip;*.7z)|*.zip;*.7z|ZIP (*.zip)|*.zip|7-Zip (*.7z)|*.7z|All files (*.*)|*.*"),
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ImportItems(dialog.FileNames);
    }

    private void ChooseModFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = T("เลือกโฟลเดอร์ของม็อดที่มี romfs หรือ exefs", "Choose a mod folder containing romfs or exefs"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ImportItems(new[] { dialog.SelectedPath });
    }

    private void ImportItems(IEnumerable<string> items)
    {
        if (!EnsureGameFolder()) return;
        var paths = items.ToArray();
        foreach (var source in paths)
        {
            try
            {
                if (Directory.Exists(source)) ImportFolder(source);
                else if (File.Exists(source) && IsSupportedArchive(source)) ImportArchive(source);
                else SetStatus(T($"ข้ามไฟล์ที่ไม่รองรับ: {Path.GetFileName(source)}", $"Skipped unsupported file: {Path.GetFileName(source)}"), false);
            }
            catch (Exception ex)
            {
                SetStatus(T($"ติดตั้ง {Path.GetFileName(source)} ไม่สำเร็จ", $"Could not install {Path.GetFileName(source)}"), false);
                MessageBox.Show(this, ex.Message, T("ติดตั้งม็อดไม่สำเร็จ", "Mod installation failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        RefreshMods();
    }

    private bool EnsureGameFolder()
    {
        if (TryGetGamePaths(out _, out var mods))
        {
            _modsDirectory = mods;
            Directory.CreateDirectory(mods);
            return true;
        }
        MessageBox.Show(this, T("กรุณาเลือกโฟลเดอร์ MHGURecomp ที่มี mhgurecomp.exe ก่อน", "Choose the MHGURecomp folder containing mhgurecomp.exe first"),
            T("ยังไม่ได้เลือกโฟลเดอร์เกม", "Game folder not selected"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void ImportArchive(string archivePath)
    {
        var modName = SanitizeModName(Path.GetFileNameWithoutExtension(archivePath));
        var tempRoot = Path.Combine(Path.GetTempPath(), "MHGU-Mod-Manager", Guid.NewGuid().ToString("N"));
        var extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractPath);
        try
        {
            ExtractArchiveSafely(archivePath, extractPath);
            var sourceRoot = FindModContentRoot(extractPath);
            if (sourceRoot is null)
                throw new InvalidDataException(T(
                    "ใน archive ไม่พบโฟลเดอร์ romfs หรือ exefs ที่ MHGURecomp ใช้งานได้ (ค้นหาลึกไม่เกิน 3 ระดับ)\n\nโครงสร้างที่รองรับ เช่น romfs/..., exefs/... หรือ atmosphere/contents/<title-id>/{romfs,exefs}.",
                    "No supported romfs or exefs folder was found in the archive (searched up to 3 levels deep).\n\nSupported layouts include romfs/..., exefs/..., or atmosphere/contents/<title-id>/{romfs,exefs}."));
            InstallModTree(sourceRoot, modName);
            SetStatus(T($"ติดตั้ง {modName} จาก archive แล้ว", $"Installed {modName} from archive"), true);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private void ImportFolder(string folder)
    {
        var sourceRoot = FindModContentRoot(folder);
        if (sourceRoot is null)
            throw new InvalidDataException(T("ไม่พบโฟลเดอร์ romfs หรือ exefs ที่เกมค้นหาได้ภายในโฟลเดอร์นี้", "No romfs or exefs folder was found in a location the game can load"));
        var modName = SanitizeModName(new DirectoryInfo(folder).Name);
        InstallModTree(sourceRoot, modName);
        SetStatus(T($"เพิ่มม็อด {modName} แล้ว", $"Added mod {modName}"), true);
    }

    private void InstallModTree(string sourceRoot, string modName)
    {
        Directory.CreateDirectory(_modsDirectory);
        var destination = Path.Combine(_modsDirectory, modName);
        if (Directory.Exists(destination))
        {
            var answer = MessageBox.Show(this,
                T($"มีม็อดชื่อ “{modName}” อยู่แล้ว ต้องการแทนที่หรือไม่?", $"A mod named “{modName}” already exists. Replace it?"),
                T("ยืนยันการแทนที่", "Confirm replacement"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
        }

        var stage = Path.Combine(_modsDirectory, ".mm_staging_" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "MHGU-Mod-Manager", "backup-" + Guid.NewGuid().ToString("N"));
        var hadOld = Directory.Exists(destination);
        Directory.CreateDirectory(stage);
        try
        {
            CopyDirectory(sourceRoot, stage);
            var layout = CheckLayout(stage, 3);
            if (!layout.HasAny) throw new InvalidDataException(T("โฟลเดอร์ม็อดไม่มี romfs, exefs หรือ nativeNX หลังจัดรูปแบบแล้ว", "The mod folder has no romfs, exefs, or nativeNX after installation"));

            if (hadOld)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(destination, backup);
            }
            try
            {
                Directory.Move(stage, destination);
            }
            catch
            {
                if (hadOld && Directory.Exists(backup) && !Directory.Exists(destination))
                    Directory.Move(backup, destination);
                throw;
            }
            if (hadOld) TryDeleteDirectory(backup);

            var current = _mods.FirstOrDefault(m => string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
                _mods.Add(new ModEntry(modName, true));
            else
            {
                var i = _mods.IndexOf(current);
                _mods[i] = current with { Enabled = true };
            }
            SaveLoadOrder();
        }
        finally
        {
            TryDeleteDirectory(stage);
            if (Directory.Exists(backup) && !Directory.Exists(destination))
                Directory.Move(backup, destination);
            else
                TryDeleteDirectory(backup);
        }
    }

    private static string SanitizeModName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim().TrimEnd('.', ' ');
        while (cleaned.StartsWith(".", StringComparison.Ordinal)) cleaned = cleaned[1..];
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "MHGU Mod";
        if (cleaned.Length > 100) cleaned = cleaned[..100].TrimEnd();
        return cleaned;
    }

    private static bool IsSupportedArchive(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);
    }

    private void ExtractArchiveSafely(string archivePath, string target)
    {
        var extension = Path.GetExtension(archivePath);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            ExtractZipSafely(archivePath, target);
        else if (extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            Validate7zArchive(archivePath, target);
            Run7zExtract(archivePath, target);
        }
        else
            throw new InvalidDataException(T("รองรับเฉพาะไฟล์ .zip และ .7z", "Only .zip and .7z archives are supported"));

        ValidateExtractedTree(target);
    }

    private void Validate7zArchive(string archivePath, string target)
    {
        const long maxExpandedBytes = 20L * 1024 * 1024 * 1024;
        const int maxEntries = 200_000;
        const int maxListingChars = 32 * 1024 * 1024;
        var sevenZip = Resolve7zExecutable();
        var start = New7zProcess(sevenZip);
        foreach (var arg in new[] { "l", "-slt", "-ba", "-sccUTF-8", archivePath })
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("7-Zip could not be started");
        process.StandardInput.Close();
        var errorTask = process.StandardError.ReadToEndAsync();
        string? entryPath = null;
        long? entrySize = null;
        var archiveHeader = false;
        var isLink = false;
        var entries = 0;
        long expandedBytes = 0;
        var outputChars = 0;

        void FinishEntry()
        {
            if (entryPath is null) return;
            if (!archiveHeader)
            {
                ValidateArchivePath(entryPath, target);
                if (isLink)
                    throw new InvalidDataException(T("ไม่รองรับ symbolic link หรือ hard link ในไฟล์ 7Z", "Symbolic links and hard links are not supported in 7Z archives"));
                entries++;
                if (entries > maxEntries) throw new InvalidDataException(T("7Z มีไฟล์มากเกินไป", "7Z archive contains too many entries"));
                if (entrySize.HasValue)
                {
                    expandedBytes = checked(expandedBytes + entrySize.Value);
                    if (expandedBytes > maxExpandedBytes)
                        throw new InvalidDataException(T("7Z ขยายได้เกินขนาดสูงสุด 20 GB", "7Z archive expands beyond the 20 GB safety limit"));
                }
            }
            entryPath = null;
            entrySize = null;
            archiveHeader = false;
            isLink = false;
        }

        string? line;
        try
        {
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                outputChars += line.Length;
                if (outputChars > maxListingChars)
                {
                    process.Kill(true);
                    throw new InvalidDataException(T("รายการไฟล์ใน 7Z ใหญ่เกินกำหนด", "7Z file listing exceeds the safety limit"));
                }
                if (line.StartsWith("Path = ", StringComparison.Ordinal))
                {
                    FinishEntry();
                    entryPath = line[7..];
                }
                else if (line.StartsWith("Size = ", StringComparison.Ordinal) &&
                         long.TryParse(line.AsSpan(7), out var size))
                    entrySize = size;
                else if (line.StartsWith("Type = ", StringComparison.Ordinal) ||
                         line.StartsWith("Physical Size = ", StringComparison.Ordinal))
                    archiveHeader = true;
                else if (line.StartsWith("Symbolic Link = ", StringComparison.Ordinal) ||
                         line.StartsWith("Hard Link = ", StringComparison.Ordinal))
                    isLink = true;
                else if (line.Length == 0)
                    FinishEntry();
            }
            FinishEntry();
        }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }

        process.WaitForExit();
        var errorText = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidDataException(T($"ตรวจสอบไฟล์ 7Z ไม่สำเร็จ: {errorText}", $"Could not inspect 7Z archive: {errorText}"));
        if (entries == 0)
            throw new InvalidDataException(T("ไม่พบไฟล์ใน archive 7Z", "No entries were found in the 7Z archive"));
    }

    private void Run7zExtract(string archivePath, string target)
    {
        var start = New7zProcess(Resolve7zExecutable());
        foreach (var arg in new[] { "x", archivePath, "-o" + target, "-y", "-bso0", "-bsp0", "-sccUTF-8" })
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("7-Zip could not be started");
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var errorText = errorTask.GetAwaiter().GetResult();
        _ = outputTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidDataException(T($"แตกไฟล์ 7Z ไม่สำเร็จ: {errorText}", $"Could not extract 7Z archive: {errorText}"));
    }

    private ProcessStartInfo New7zProcess(string executable) => new(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = new UTF8Encoding(false),
        StandardErrorEncoding = new UTF8Encoding(false)
    };

    private string Resolve7zExecutable()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
        };
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(folder.Trim('"'), "7z.exe"));
            candidates.Add(Path.Combine(folder.Trim('"'), "7zz.exe"));
            candidates.Add(Path.Combine(folder.Trim('"'), "7za.exe"));
        }

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null) return found;
        throw new InvalidOperationException(T(
            "การเปิดไฟล์ .7z ต้องติดตั้ง 7-Zip ก่อน จากนั้นเปิด Mod Manager ใหม่อีกครั้ง",
            "Opening .7z archives requires 7-Zip. Install 7-Zip, then restart Mod Manager."));
    }

    private void ValidateArchivePath(string entryPath, string target)
    {
        var normalized = entryPath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains(':') || Path.IsPathRooted(entryPath))
            throw new InvalidDataException(T($"archive มี path ที่ไม่ปลอดภัย: {entryPath}", $"Archive contains an unsafe path: {entryPath}"));
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException(T($"archive มี path ที่ไม่ปลอดภัย: {entryPath}", $"Archive contains an unsafe path: {entryPath}"));
        var root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(target, Path.Combine(segments)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(T($"archive พยายามเขียนออกนอกโฟลเดอร์ชั่วคราว: {entryPath}", $"Archive entry escapes the temporary folder: {entryPath}"));
    }

    private void ValidateExtractedTree(string target)
    {
        const int maxEntries = 200_000;
        var root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(target));
        var count = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                count++;
                if (count > maxEntries) throw new InvalidDataException(T("archive มีไฟล์มากเกินไป", "Archive contains too many entries"));
                var fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(T("archive เขียนไฟล์ออกนอกโฟลเดอร์ชั่วคราว", "Archive wrote outside the temporary folder"));
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(T("ไม่รองรับ symbolic link ใน archive", "Symbolic links are not supported in archives"));
                if ((attributes & FileAttributes.Directory) != 0) queue.Enqueue(path);
            }
        }
    }

    private void ExtractZipSafely(string zipPath, string target)
    {
        const long maxExpandedBytes = 20L * 1024 * 1024 * 1024;
        const int maxEntries = 200_000;
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > maxEntries) throw new InvalidDataException(T("ZIP มีไฟล์มากเกินไป", "ZIP contains too many entries"));
        long expandedBytes = 0;
        var root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > maxExpandedBytes) throw new InvalidDataException(T("ZIP ขยายได้เกินขนาดสูงสุด 20 GB", "ZIP expands beyond the 20 GB safety limit"));
            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Contains(':'))
                throw new InvalidDataException(T($"ZIP มี path ที่ไม่ปลอดภัย: {entry.FullName}", $"ZIP contains an unsafe path: {entry.FullName}"));
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidDataException(T($"ZIP มี path ที่ไม่ปลอดภัย: {entry.FullName}", $"ZIP contains an unsafe path: {entry.FullName}"));
            if (segments.Length == 0) continue;
            var destination = Path.GetFullPath(Path.Combine(target, Path.Combine(segments)));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(T($"ZIP พยายามเขียนออกนอกโฟลเดอร์ชั่วคราว: {entry.FullName}", $"ZIP entry escapes the temporary folder: {entry.FullName}"));

            if (normalized.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private string? FindModContentRoot(string root)
    {
        var selectedName = Path.GetFileName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar));
        if (selectedName.Equals("romfs", StringComparison.OrdinalIgnoreCase) ||
            selectedName.Equals("exefs", StringComparison.OrdinalIgnoreCase) ||
            selectedName.Equals("nativeNX", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar))?.FullName;

        var candidates = new List<(string Path, int Depth, bool Romfs, bool Exefs, bool Native)>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((Path.GetFullPath(root), 0));
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (!Directory.Exists(current)) continue;
            var dirs = SafeGetDirectories(current);
            var romfs = dirs.FirstOrDefault(d => string.Equals(Path.GetFileName(d), "romfs", StringComparison.OrdinalIgnoreCase));
            var exefs = dirs.FirstOrDefault(d => string.Equals(Path.GetFileName(d), "exefs", StringComparison.OrdinalIgnoreCase));
            var native = dirs.FirstOrDefault(d => string.Equals(Path.GetFileName(d), "nativeNX", StringComparison.OrdinalIgnoreCase));
            if (romfs is not null || exefs is not null || native is not null)
                candidates.Add((current, depth, romfs is not null, exefs is not null, native is not null));
            if (depth >= 3) continue;
            foreach (var child in dirs)
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                queue.Enqueue((child, depth + 1));
            }
        }

        if (candidates.Count == 0) return null;
        var sorted = candidates
            .OrderByDescending(c => (c.Romfs ? 1 : 0) + (c.Exefs ? 1 : 0))
            .ThenBy(c => c.Depth)
            .ToList();
        var best = sorted[0];
        var siblings = sorted.Where(c => c.Depth == best.Depth &&
            !IsSameOrChild(c.Path, best.Path) && !IsSameOrChild(best.Path, c.Path)).ToList();
        if (siblings.Count > 0)
            throw new InvalidDataException(T("พบโฟลเดอร์ม็อดหลายชุดในแพ็กเดียว กรุณาเลือก ZIP หรือโฟลเดอร์ของแต่ละม็อดแยกกัน", "This package contains multiple mod folders. Install each mod ZIP or folder separately."));
        return best.Path;
    }

    private static bool IsSameOrChild(string path, string parent)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(fullPath, fullParent, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] SafeGetDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path)
                .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in SafeGetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            var target = Path.Combine(destination, name);
            Directory.CreateDirectory(target);
            CopyDirectory(dir, target);
        }
        string[] files;
        try { files = Directory.GetFiles(source); }
        catch { return; }
        foreach (var file in files)
        {
            try
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch { continue; }
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
    }

    private static ModLayout CheckLayout(string root, int maxDepth)
    {
        var foundRomfs = false;
        var foundExefs = false;
        var foundNative = false;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            var dirs = SafeGetDirectories(current);
            foundRomfs |= dirs.Any(d => string.Equals(Path.GetFileName(d), "romfs", StringComparison.OrdinalIgnoreCase));
            foundExefs |= dirs.Any(d => string.Equals(Path.GetFileName(d), "exefs", StringComparison.OrdinalIgnoreCase));
            foundNative |= dirs.Any(d => string.Equals(Path.GetFileName(d), "nativeNX", StringComparison.OrdinalIgnoreCase));
            if (depth >= maxDepth) continue;
            foreach (var child in dirs)
                if (!Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) queue.Enqueue((child, depth + 1));
        }
        return new ModLayout(foundRomfs, foundExefs, foundNative);
    }

    private string DescribeLayout(ModLayout layout)
    {
        if (!layout.HasAny) return T("ไม่พบโครงสร้าง", "Not found");
        if (layout.Native && !layout.Romfs && !layout.Exefs) return "nativeNX (romfs)";
        var parts = new List<string>();
        if (layout.Romfs) parts.Add("romfs");
        if (layout.Exefs) parts.Add("exefs");
        if (layout.Native) parts.Add("nativeNX");
        return string.Join(" · ", parts);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    private void RemoveSelectedMod()
    {
        if (_modsList.SelectedIndices.Count == 0) return;
        var selected = _mods[_modsList.SelectedIndices[0]];
        var answer = MessageBox.Show(this,
            T($"ย้ายม็อด “{selected.Name}” ไปถังรีไซเคิลหรือไม่?", $"Move mod “{selected.Name}” to the Recycle Bin?"),
            T("ยืนยันการลบม็อด", "Confirm mod removal"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        try
        {
            FileSystem.DeleteDirectory(Path.Combine(_modsDirectory, selected.Name), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            RefreshMods();
            SaveLoadOrderAndRefresh(T($"นำ {selected.Name} ออกแล้ว (ส่งไปถังรีไซเคิล)", $"Removed {selected.Name} (sent to Recycle Bin)"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("นำม็อดออกไม่ได้", "Could not remove mod"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenModsFolder()
    {
        if (!EnsureGameFolder()) return;
        Process.Start(new ProcessStartInfo { FileName = _modsDirectory, UseShellExecute = true });
    }

    private void LaunchGame()
    {
        if (!TryGetGamePaths(out var root, out _))
        {
            MessageBox.Show(this, T("เลือกโฟลเดอร์ MHGURecomp ที่ถูกต้องก่อน", "Choose a valid MHGURecomp folder first"),
                T("ไม่พบเกม", "Game not found"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(root, "mhgurecomp.exe"),
            WorkingDirectory = root,
            UseShellExecute = true
        });
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            e.Effect = paths.Any(p => Directory.Exists(p) || IsSupportedArchive(p))
                ? DragDropEffects.Copy : DragDropEffects.None;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            ImportItems(paths);
    }

    private void SetStatus(string message, bool success)
    {
        _footer.Text = message;
        _footerIsStatus = true;
        _footerStatusOk = success;
        _footer.ForeColor = success ? Green : Red;
    }
}

internal sealed record ThemePalette(
    Color Background, Color Surface, Color Control, Color Input, Color Text, Color Muted,
    Color Primary, Color PrimaryHover, Color Danger, Color DangerHover, Color Success,
    Color Warning, Color Border, Color ControlHover, Color ControlPressed, Color Drop,
    Color DropText, Color Header, Color HeaderText, Color ListRow, Color SelectedRow,
    Color RowBorder, Color SwitchOff)
{
    public static ThemePalette Light { get; } = new(
        Color.FromArgb(244, 247, 251), Color.White, Color.White, Color.White,
        Color.FromArgb(26, 38, 56), Color.FromArgb(100, 116, 139),
        Color.FromArgb(40, 105, 190), Color.FromArgb(32, 91, 170),
        Color.FromArgb(185, 55, 55), Color.FromArgb(160, 43, 43),
        Color.FromArgb(28, 132, 89), Color.FromArgb(167, 89, 35),
        Color.FromArgb(202, 213, 226), Color.FromArgb(239, 244, 250),
        Color.FromArgb(226, 234, 244), Color.FromArgb(236, 243, 252),
        Color.FromArgb(52, 88, 132), Color.FromArgb(233, 239, 247),
        Color.FromArgb(42, 58, 78), Color.White, Color.FromArgb(225, 238, 253),
        Color.FromArgb(238, 242, 247), Color.FromArgb(161, 172, 186));

    public static ThemePalette Dark { get; } = new(
        Color.FromArgb(17, 22, 30), Color.FromArgb(26, 33, 44), Color.FromArgb(36, 45, 59),
        Color.FromArgb(22, 29, 39), Color.FromArgb(231, 237, 245), Color.FromArgb(154, 166, 183),
        Color.FromArgb(55, 119, 202), Color.FromArgb(69, 137, 222),
        Color.FromArgb(180, 67, 78), Color.FromArgb(204, 79, 91),
        Color.FromArgb(55, 166, 119), Color.FromArgb(225, 165, 92),
        Color.FromArgb(54, 66, 83), Color.FromArgb(48, 60, 77),
        Color.FromArgb(57, 72, 91), Color.FromArgb(28, 42, 60),
        Color.FromArgb(177, 205, 239), Color.FromArgb(34, 43, 57),
        Color.FromArgb(198, 210, 226), Color.FromArgb(26, 33, 44),
        Color.FromArgb(42, 61, 84), Color.FromArgb(43, 53, 68), Color.FromArgb(91, 105, 123));
}

internal sealed record ModEntry(string Name, bool Enabled);
internal sealed record ModLayout(bool Romfs, bool Exefs, bool Native)
{
    public bool HasAny => Romfs || Exefs || Native;
}
internal sealed class ManagerSettings
{
    public string GamePath { get; set; } = "";
    public bool DarkMode { get; set; } = true;
    public bool English { get; set; } = true;
    public bool LanguageConfigured { get; set; }
}
