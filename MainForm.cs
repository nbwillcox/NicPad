using System.Drawing.Text;
using System.Globalization;
using Microsoft.Win32;
using NicPad.Core;
using NicPad.UI;

namespace NicPad;

/// <summary>Main window: layout, tab management and status. Feature areas live in the MainForm.*.cs partials.</summary>
public partial class MainForm : Form
{
    internal const long LargeFileWarningBytes = 50L * 1024 * 1024;
    internal const long MaxOpenBytes = 512L * 1024 * 1024;

    private static readonly Lazy<HashSet<string>> InstalledFonts = new(() =>
    {
        using var fonts = new InstalledFontCollection();
        return new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
    });

    private readonly AppPaths paths;
    private readonly AppSettings settings;
    private readonly bool settingsExisted;
    private readonly BackupStore backups;
    private readonly MarkdownRenderer renderer = new();
    private readonly SpeechReader speech = new();
    private readonly Stack<string> closedFiles = new();
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer messageTimer = new() { Interval = 6000 };
    private readonly System.Windows.Forms.Timer backupTimer = new();

    private ThemedMenuStrip menuStrip = null!;
    private ThemedToolStrip toolStrip = null!;
    private ThemedTabControl tabControl = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel messageLabel = null!;
    private ToolStripStatusLabel positionLabel = null!;
    private ToolStripStatusLabel statsLabel = null!;
    private ToolStripStatusLabel encodingLabel = null!;
    private ToolStripStatusLabel eolLabel = null!;
    private ToolStripStatusLabel zoomLabel = null!;
    private ToolStripStatusLabel kindLabel = null!;
    private Panel separator = null!;
    private Panel tabPanel = null!;

    private int untitledCount;
    private bool restoring;
    private bool checkingDisk;

    public MainForm()
        : this(AppPaths.Default())
    {
    }

    internal MainForm(AppPaths paths, DialogService? dialogs = null, bool restoreSession = true)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
        settings = AppSettings.Load(paths.SettingsFile, out settingsExisted);
        Dialogs = dialogs ?? new DialogService();
        backups = new BackupStore(paths.BackupDir);

        InitializeComponent();
        LoadCustomThemes();

        var session = restoreSession ? SessionData.Load(paths.SessionFile) : null;
        if (!settingsExisted && session?.Theme is { } legacyTheme)
        {
            settings.Theme = legacyTheme; // v1 kept the theme in session.json
        }

        ApplyTheme();
        ApplyWindowPlacement();

        var problems = new List<string>();
        if (!RestoreSession(session, problems))
        {
            NewDocument();
        }

        backups.DeleteOrphans(Documents.Select(d => d.BackupId), TimeSpan.FromDays(2));
        if (problems.Count > 0)
        {
            ShowMessage(problems.Count == 1 ? problems[0] : $"{problems.Count} files from your last session couldn't be reopened: {string.Join("; ", problems.Take(3))}");
        }

        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            RenderPreview(Current);
        };
        statusTimer.Tick += (_, _) =>
        {
            statusTimer.Stop();
            UpdateStatusNow();
        };
        messageTimer.Tick += (_, _) =>
        {
            messageTimer.Stop();
            messageLabel.Text = Current?.FilePath ?? "";
        };
        backupTimer.Interval = settings.BackupIntervalSeconds * 1000;
        backupTimer.Tick += (_, _) => WriteBackups();
        backupTimer.Enabled = settings.AutoBackup;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    internal DialogService Dialogs { get; set; }

    internal DocumentTab? Current => tabControl.SelectedTab as DocumentTab;

    internal IReadOnlyList<DocumentTab> Documents => tabControl.TabPages.OfType<DocumentTab>().ToList();

    internal AppSettings Settings => settings;

    internal static bool IsInstalledFont(string? name) =>
        !string.IsNullOrWhiteSpace(name) && InstalledFonts.Value.Contains(name);

    public void NewDocument()
    {
        var tab = CreateTab(NextUntitledName());
        tabControl.SelectedTab = tab;
        UpdateActiveTabUi();
        tab.Editor.Focus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetDarkTitleBar(Handle, palette.IsDark);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!checkingDisk && IsHandleCreated)
        {
            BeginInvoke(CheckDiskChanges);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            previewTimer.Dispose();
            statusTimer.Dispose();
            messageTimer.Dispose();
            backupTimer.Dispose();
            speech.Dispose();
            findDialog?.Dispose();
            tabMenu?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "NicPad";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Size = new Size(1000, 700);
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        menuStrip = new ThemedMenuStrip();
        CreateMenus();

        toolStrip = new ThemedToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(5, 2, 5, 2),
            Margin = Padding.Empty,
            AccessibleName = "Toolbar",
        };
        CreateToolbar();

        tabControl = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9),
            Padding = new Point(20, 4),
            AllowDrop = true,
            ShowToolTips = true,
            AccessibleName = "Open documents",
        };
        tabControl.SelectedIndexChanged += (_, _) => UpdateActiveTabUi();
        tabControl.CloseRequested += (_, index) =>
        {
            if (tabControl.TabPages[index] is DocumentTab tab)
            {
                CloseTab(tab);
            }
        };
        tabControl.ContextMenuRequested += (_, index) => ShowTabMenu(index);
        tabControl.TabsReordered += (_, _) => UpdateActiveTabUi();
        tabControl.DragEnter += OnDragEnter;
        tabControl.DragDrop += OnDragDrop;

        tabPanel = new Panel { Dock = DockStyle.Fill };
        tabPanel.Controls.Add(tabControl);

        separator = new Panel { Dock = DockStyle.Top, Height = 1 };

        statusStrip = new StatusStrip { ShowItemToolTips = true, AccessibleName = "Status bar" };
        messageLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        positionLabel = StatusCell("Ln 1, Col 1", "Caret position");
        statsLabel = StatusCell("0 words", "Word and character count");
        encodingLabel = StatusCell("UTF-8", "Encoding (Format > Encoding to change)");
        eolLabel = StatusCell("CRLF", "Line endings (Format > Line Endings to change)");
        zoomLabel = StatusCell("100%", "Zoom (Ctrl+wheel, Ctrl++ / Ctrl+-)");
        kindLabel = StatusCell("TXT", "File type");
        statusStrip.Items.AddRange([messageLabel, positionLabel, statsLabel, encodingLabel, eolLabel, zoomLabel, kindLabel]);

        MainMenuStrip = menuStrip;
        Controls.Add(tabPanel);
        Controls.Add(separator);
        Controls.Add(toolStrip);
        Controls.Add(menuStrip);
        Controls.Add(statusStrip);

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        KeyDown += OnFormKeyDown;

        toolStrip.Visible = separator.Visible = settings.ShowToolbar;
        statusStrip.Visible = settings.ShowStatusBar;
        TopMost = settings.AlwaysOnTop;
        ResumeLayout(false);
        PerformLayout();
    }

    private static ToolStripStatusLabel StatusCell(string text, string tip) => new(text)
    {
        BorderSides = ToolStripStatusLabelBorderSides.Left,
        BorderStyle = Border3DStyle.Etched,
        Padding = new Padding(6, 0, 6, 0),
        ToolTipText = tip,
        AccessibleName = tip,
    };

    private DocumentTab CreateTab(string untitledName)
    {
        var family = IsInstalledFont(settings.DefaultFontFamily) ? settings.DefaultFontFamily : "Consolas";
        var tab = new DocumentTab(untitledName, family, settings.DefaultFontSize);
        tab.ApplyPalette(palette);
        tab.Editor.WordWrap = settings.WordWrap;
        tab.ModifiedChanged += (_, _) =>
        {
            tab.UpdateText();
            if (tab == Current)
            {
                UpdateTitle();
            }

            tabControl.Invalidate();
        };
        tab.ContentChanged += (_, _) =>
        {
            if (tab != Current || restoring)
            {
                return;
            }

            if (tab.PreviewMode != PreviewMode.Off)
            {
                previewTimer.Stop();
                previewTimer.Start();
            }

            ScheduleStatus();
        };
        tab.Editor.SelectionChanged += (_, _) =>
        {
            if (tab == Current && !restoring)
            {
                UpdateFormattingButtons();
                ScheduleStatus();
            }
        };
        tab.Editor.ZoomChanged += (_, _) => ScheduleStatus();
        tab.Editor.DragEnter += OnDragEnter;
        tab.Editor.DragDrop += OnDragDrop;
        tabControl.TabPages.Add(tab);
        return tab;
    }

    private string NextUntitledName() => $"Untitled {++untitledCount}";

    private void NoteUntitledName(string name)
    {
        if (name.StartsWith("Untitled ", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(9), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            untitledCount = Math.Max(untitledCount, n);
        }
    }

    /// <summary>A lone, empty, never-edited Untitled tab gets replaced by the next opened file.</summary>
    private DocumentTab? ReusableBlankTab() =>
        tabControl.TabCount == 1 && Current is { FilePath: null, IsModified: false } t && t.Editor.TextLength == 0 ? t : null;

    internal bool CloseTab(DocumentTab tab)
    {
        if (tab.IsModified && !ConfirmSaveOrDiscard(tab))
        {
            return false;
        }

        if (tab.FilePath is not null)
        {
            closedFiles.Push(tab.FilePath);
        }

        backups.Delete(tab.BackupId);
        var index = tabControl.TabPages.IndexOf(tab);
        tabControl.TabPages.Remove(tab);
        tab.Dispose();

        if (tabControl.TabCount == 0)
        {
            NewDocument();
        }
        else
        {
            tabControl.SelectedIndex = Math.Min(index, tabControl.TabCount - 1);
        }

        UpdateActiveTabUi();
        return true;
    }

    private bool CloseTabs(IEnumerable<DocumentTab> tabs)
    {
        foreach (var tab in tabs.ToList())
        {
            if (!CloseTab(tab))
            {
                return false;
            }
        }

        return true;
    }

    private bool ConfirmSaveOrDiscard(DocumentTab tab)
    {
        tabControl.SelectedTab = tab;
        return Dialogs.AskSaveChanges(this, tab.DisplayName) switch
        {
            SaveChoice.Save => Save(tab),
            SaveChoice.DontSave => true,
            _ => false,
        };
    }

    private void NextTab()
    {
        if (tabControl.TabCount > 1)
        {
            tabControl.SelectedIndex = (tabControl.SelectedIndex + 1) % tabControl.TabCount;
        }
    }

    private void PreviousTab()
    {
        if (tabControl.TabCount > 1)
        {
            tabControl.SelectedIndex = (tabControl.SelectedIndex - 1 + tabControl.TabCount) % tabControl.TabCount;
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && (e.KeyCode == Keys.Tab || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp))
        {
            if (e.Shift || e.KeyCode == Keys.PageUp)
            {
                PreviousTab();
            }
            else
            {
                NextTab();
            }

            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.F4)
        {
            if (Current is { } tab)
            {
                CloseTab(tab);
            }

            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Add)
        {
            Zoom(+0.1f);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Subtract)
        {
            Zoom(-0.1f);
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    /// <summary>Everything that depends on which tab is active.</summary>
    private void UpdateActiveTabUi()
    {
        if (restoring)
        {
            return;
        }

        UpdateFontControls();
        UpdatePreviewUi();
        UpdateFormattingButtons();
        UpdateTitle();
        UpdateStatusNow();
        if (Current is { } tab)
        {
            messageLabel.Text = tab.FilePath ?? "";
            if (tab.PreviewMode != PreviewMode.Off)
            {
                RenderPreview(tab);
            }
        }
    }

    private void UpdateTitle()
    {
        Text = Current is { } tab ? $"{tab.DisplayName}{(tab.IsModified ? "*" : "")} - NicPad" : "NicPad";
    }

    private void ScheduleStatus()
    {
        statusTimer.Stop();
        statusTimer.Start();
    }

    internal void UpdateStatusNow()
    {
        if (Current is not { } tab)
        {
            return;
        }

        var text = tab.TextSnapshot;
        var caret = Math.Min(tab.Editor.SelectionStart, text.Length);
        var line = text.AsSpan(0, caret).Count('\n') + 1;
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var column = caret - lineStart + 1;
        var selection = tab.Editor.SelectionLength;

        positionLabel.Text = selection > 0
            ? string.Format(CultureInfo.CurrentCulture, "Ln {0:N0}, Col {1:N0} ({2:N0} selected)", line, column, selection)
            : string.Format(CultureInfo.CurrentCulture, "Ln {0:N0}, Col {1:N0}", line, column);
        statsLabel.Text = string.Format(CultureInfo.CurrentCulture, "{0:N0} lines, {1:N0} words, {2:N0} chars",
            TextOps.LineCount(text), TextOps.WordCount(text), text.Length);
        encodingLabel.Text = tab.Kind == DocumentKind.Rtf ? "RTF" : TextFileIO.DisplayName(tab.Encoding);
        eolLabel.Text = TextFileIO.DisplayName(tab.LineEnding);
        zoomLabel.Text = string.Format(CultureInfo.CurrentCulture, "{0:0}%", tab.Editor.ZoomFactor * 100);
        kindLabel.Text = tab.Kind switch
        {
            DocumentKind.Markdown => "Markdown",
            DocumentKind.Rtf => "Rich Text",
            _ => tab.Extension.TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "TXT",
        };
    }

    internal void ShowMessage(string text)
    {
        messageLabel.Text = text;
        messageTimer.Stop();
        messageTimer.Start();
    }

    private void ApplyWindowPlacement()
    {
        if (settings.Window is not { } w)
        {
            return;
        }

        var bounds = new Rectangle(w.X, w.Y, w.Width, w.Height);
        var visible = Screen.AllScreens.Any(s =>
        {
            var overlap = Rectangle.Intersect(s.WorkingArea, bounds);
            return overlap.Width >= 100 && overlap.Height >= 50;
        });
        if (!visible)
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (w.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void CaptureWindowPlacement()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.Window = new WindowPlacement
        {
            X = b.X,
            Y = b.Y,
            Width = b.Width,
            Height = b.Height,
            Maximized = WindowState == FormWindowState.Maximized,
        };
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            settings.Save(paths.SettingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Couldn't save settings: " + ex.Message);
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color or UserPreferenceCategory.General
            && IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(ApplyTheme);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            // Defer so the drag source (Explorer) isn't blocked by any dialog we show.
            BeginInvoke(() => OpenFiles(files));
        }
    }
}
