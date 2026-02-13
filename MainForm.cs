using System.Drawing.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;

namespace NicPad;

public class MainForm : Form
{
    private ThemedMenuStrip menuStrip = null!;
    private ThemedToolStrip toolStrip = null!;
    private ThemedTabControl tabControl = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private ToolStripComboBox fontFamilyCombo = null!;
    private ToolStripComboBox fontSizeCombo = null!;
    private ToolStripComboBox themeCombo = null!;
    private ToolStripButton previewButton = null!;
    private ToolStripButton boldButton = null!;
    private ToolStripButton italicButton = null!;
    private ToolStripButton underlineButton = null!;
    private Panel separator = null!;
    private Panel tabPanel = null!;

    private int untitledCount = 0;
    private readonly MarkdownPipeline markdownPipeline;
    private readonly string sessionFilePath;
    private string currentTheme = "System";

    public MainForm()
    {
        markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        // Set up session file path in AppData
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NicPad");
        Directory.CreateDirectory(appDataPath);
        sessionFilePath = Path.Combine(appDataPath, "session.json");

        InitializeComponent();
        SetupKeyboardShortcuts();

        // Try to restore previous session, otherwise start with new document
        if (!RestoreSession())
        {
            NewDocument();
        }
    }

    private void InitializeComponent()
    {
        this.Text = "NicPad";
        this.Size = new Size(1000, 700);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(252, 252, 252);

        // Menu Strip
        menuStrip = new ThemedMenuStrip();
        menuStrip.BackColor = Color.FromArgb(248, 248, 248);
        CreateMenus();

        // Tool Strip
        toolStrip = new ThemedToolStrip();
        toolStrip.BackColor = Color.FromArgb(248, 248, 248);
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Padding = new Padding(5, 2, 5, 2);
        toolStrip.Margin = Padding.Empty;
        CreateToolbar();

        // Tab Control
        tabControl = new ThemedTabControl();
        tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tabControl.Font = new Font("Segoe UI", 9);
        tabControl.Padding = new Point(20, 4);  // Extra padding for close button
        tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabControl.SizeMode = TabSizeMode.Normal;
        tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
        tabControl.MouseClick += TabControl_MouseClick;
        tabControl.DrawItem += TabControl_DrawItem;
        tabControl.Paint += TabControl_Paint;

        // Panel to wrap TabControl
        tabPanel = new Panel();
        tabPanel.Dock = DockStyle.Fill;
        tabPanel.BackColor = Color.FromArgb(248, 248, 248);
        tabPanel.Controls.Add(tabControl);
        tabControl.Dock = DockStyle.Fill;

        // Separator line under toolbar
        separator = new Panel();
        separator.Dock = DockStyle.Top;
        separator.Height = 1;
        separator.BackColor = Color.FromArgb(220, 220, 220);

        // Status Strip
        statusStrip = new StatusStrip();
        statusStrip.BackColor = Color.FromArgb(248, 248, 248);
        statusLabel = new ToolStripStatusLabel("Ready");
        statusStrip.Items.Add(statusLabel);

        // Add controls (order matters for docking)
        this.MainMenuStrip = menuStrip;
        this.Controls.Add(tabPanel);
        this.Controls.Add(separator);
        this.Controls.Add(toolStrip);
        this.Controls.Add(menuStrip);
        this.Controls.Add(statusStrip);

    }

    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var tabPage = tabControl.TabPages[e.Index];
        var tabRect = tabControl.GetTabRect(e.Index);
        var isSelected = (tabControl.SelectedIndex == e.Index);

        // Get theme colors
        Color tabBackColor, tabBackColorSelected, borderColor, textColor, closeColor;
        switch (currentTheme)
        {
            case "Dark":
                tabBackColor = Color.FromArgb(45, 45, 45);
                tabBackColorSelected = Color.FromArgb(30, 30, 30);
                borderColor = Color.FromArgb(60, 60, 60);
                textColor = Color.White;
                closeColor = Color.FromArgb(180, 180, 180);
                break;
            case "Matrix":
                tabBackColor = Color.FromArgb(10, 10, 10);
                tabBackColorSelected = Color.Black;
                borderColor = Color.FromArgb(0, 100, 0);
                textColor = Color.FromArgb(0, 255, 0);
                closeColor = Color.FromArgb(0, 200, 0);
                break;
            case "Light":
                tabBackColor = Color.FromArgb(245, 245, 245);
                tabBackColorSelected = Color.White;
                borderColor = Color.FromArgb(200, 200, 200);
                textColor = Color.FromArgb(50, 50, 50);
                closeColor = Color.FromArgb(120, 120, 120);
                break;
            default: // System
                tabBackColor = SystemColors.Control;
                tabBackColorSelected = SystemColors.Window;
                borderColor = SystemColors.ControlDark;
                textColor = SystemColors.ControlText;
                closeColor = SystemColors.ControlDark;
                break;
        }

        // Background
        using (var brush = new SolidBrush(isSelected ? tabBackColorSelected : tabBackColor))
        {
            e.Graphics.FillRectangle(brush, tabRect);
        }

        // Draw border around tab
        using (var pen = new Pen(borderColor))
        {
            e.Graphics.DrawRectangle(pen, tabRect.X, tabRect.Y, tabRect.Width - 1, tabRect.Height - 1);
        }

        // Draw bottom line for unselected tabs (to show separator)
        if (!isSelected)
        {
            using (var pen = new Pen(borderColor))
            {
                e.Graphics.DrawLine(pen, tabRect.Left, tabRect.Bottom - 1, tabRect.Right, tabRect.Bottom - 1);
            }
        }

        // Tab text
        var textRect = new Rectangle(tabRect.X + 8, tabRect.Y + 4, tabRect.Width - 28, tabRect.Height - 4);
        TextRenderer.DrawText(e.Graphics, tabPage.Text, tabControl.Font, textRect, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // Close button (X)
        var closeRect = GetCloseButtonRect(tabRect);

        // Draw X
        using (var pen = new Pen(closeColor, 1.5f))
        {
            var padding = 4;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLine(pen, closeRect.Left + padding, closeRect.Top + padding, closeRect.Right - padding, closeRect.Bottom - padding);
            e.Graphics.DrawLine(pen, closeRect.Right - padding, closeRect.Top + padding, closeRect.Left + padding, closeRect.Bottom - padding);
        }
    }

    private Rectangle GetCloseButtonRect(Rectangle tabRect)
    {
        var size = 16;
        return new Rectangle(tabRect.Right - size - 6, tabRect.Top + (tabRect.Height - size) / 2, size, size);
    }

    private void TabControl_Paint(object? sender, PaintEventArgs e)
    {
        // Paint the background of the tab control (area behind tabs including right of last tab)
        Color bgColor = currentTheme switch
        {
            "Dark" => Color.FromArgb(45, 45, 45),
            "Matrix" => Color.FromArgb(10, 10, 10),
            "Light" => Color.FromArgb(248, 248, 248),
            _ => SystemColors.Control
        };

        using (var brush = new SolidBrush(bgColor))
        {
            // Fill everything from the very top of the tab control
            // (covers any native visual styles border above the tabs)
            if (tabControl.TabPages.Count > 0)
            {
                var firstTabRect = tabControl.GetTabRect(0);
                var headerRect = new Rectangle(0, 0, tabControl.Width, firstTabRect.Bottom + 2);
                e.Graphics.FillRectangle(brush, headerRect);

                // Also fill above the tabs (native border area)
                if (firstTabRect.Top > 0)
                {
                    e.Graphics.FillRectangle(brush, 0, 0, tabControl.Width, firstTabRect.Top);
                }
            }
            else
            {
                e.Graphics.FillRectangle(brush, 0, 0, tabControl.Width, 25);
            }
        }
    }

    private void CreateMenus()
    {
        // File Menu
        var fileMenu = new ToolStripMenuItem("&File");

        var newItem = new ToolStripMenuItem("&New", null, (s, e) => NewDocument());
        newItem.ShortcutKeys = Keys.Control | Keys.N;
        fileMenu.DropDownItems.Add(newItem);

        var openItem = new ToolStripMenuItem("&Open...", null, (s, e) => OpenDocument());
        openItem.ShortcutKeys = Keys.Control | Keys.O;
        fileMenu.DropDownItems.Add(openItem);

        fileMenu.DropDownItems.Add(new ToolStripSeparator());

        var saveItem = new ToolStripMenuItem("&Save", null, (s, e) => SaveDocument());
        saveItem.ShortcutKeys = Keys.Control | Keys.S;
        fileMenu.DropDownItems.Add(saveItem);

        var saveAsItem = new ToolStripMenuItem("Save &As...", null, (s, e) => SaveDocumentAs());
        saveAsItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
        fileMenu.DropDownItems.Add(saveAsItem);

        fileMenu.DropDownItems.Add(new ToolStripSeparator());

        var closeItem = new ToolStripMenuItem("&Close Tab", null, (s, e) => CloseCurrentTab());
        closeItem.ShortcutKeys = Keys.Control | Keys.W;
        fileMenu.DropDownItems.Add(closeItem);

        fileMenu.DropDownItems.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("E&xit", null, (s, e) => this.Close());
        fileMenu.DropDownItems.Add(exitItem);

        // Edit Menu
        var editMenu = new ToolStripMenuItem("&Edit");

        var undoItem = new ToolStripMenuItem("&Undo", null, (s, e) => GetCurrentEditor()?.Undo());
        undoItem.ShortcutKeys = Keys.Control | Keys.Z;
        editMenu.DropDownItems.Add(undoItem);

        var redoItem = new ToolStripMenuItem("&Redo", null, (s, e) => GetCurrentEditor()?.Redo());
        redoItem.ShortcutKeys = Keys.Control | Keys.Y;
        editMenu.DropDownItems.Add(redoItem);

        editMenu.DropDownItems.Add(new ToolStripSeparator());

        var cutItem = new ToolStripMenuItem("Cu&t", null, (s, e) => GetCurrentEditor()?.Cut());
        cutItem.ShortcutKeys = Keys.Control | Keys.X;
        editMenu.DropDownItems.Add(cutItem);

        var copyItem = new ToolStripMenuItem("&Copy", null, (s, e) => GetCurrentEditor()?.Copy());
        copyItem.ShortcutKeys = Keys.Control | Keys.C;
        editMenu.DropDownItems.Add(copyItem);

        var pasteItem = new ToolStripMenuItem("&Paste", null, (s, e) => GetCurrentEditor()?.Paste());
        pasteItem.ShortcutKeys = Keys.Control | Keys.V;
        editMenu.DropDownItems.Add(pasteItem);

        editMenu.DropDownItems.Add(new ToolStripSeparator());

        var selectAllItem = new ToolStripMenuItem("Select &All", null, (s, e) => GetCurrentEditor()?.SelectAll());
        selectAllItem.ShortcutKeys = Keys.Control | Keys.A;
        editMenu.DropDownItems.Add(selectAllItem);

        // Format Menu
        var formatMenu = new ToolStripMenuItem("F&ormat");

        var boldItem = new ToolStripMenuItem("&Bold", null, (s, e) => ApplyBold());
        boldItem.ShortcutKeys = Keys.Control | Keys.B;
        formatMenu.DropDownItems.Add(boldItem);

        var italicItem = new ToolStripMenuItem("&Italic", null, (s, e) => ApplyItalic());
        italicItem.ShortcutKeys = Keys.Control | Keys.I;
        formatMenu.DropDownItems.Add(italicItem);

        var underlineItem = new ToolStripMenuItem("&Underline", null, (s, e) => ApplyUnderline());
        underlineItem.ShortcutKeys = Keys.Control | Keys.U;
        formatMenu.DropDownItems.Add(underlineItem);

        formatMenu.DropDownItems.Add(new ToolStripSeparator());
        formatMenu.DropDownItems.Add(new ToolStripMenuItem("&Font...", null, (s, e) => ShowFontDialog()));

        // View Menu
        var viewMenu = new ToolStripMenuItem("&View");

        var previewItem = new ToolStripMenuItem("Toggle &Preview", null, (s, e) => TogglePreview());
        previewItem.ShortcutKeys = Keys.Control | Keys.P;
        viewMenu.DropDownItems.Add(previewItem);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Next Tab", null, (s, e) => NextTab()));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Previous Tab", null, (s, e) => PreviousTab()));

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, formatMenu, viewMenu });
    }

    private void CreateToolbar()
    {
        // New
        var newButton = new ToolStripButton("New");
        newButton.ToolTipText = "New (Ctrl+N)";
        newButton.Click += (s, e) => NewDocument();

        // Open
        var openButton = new ToolStripButton("Open");
        openButton.ToolTipText = "Open (Ctrl+O)";
        openButton.Click += (s, e) => OpenDocument();

        // Save
        var saveButton = new ToolStripButton("Save");
        saveButton.ToolTipText = "Save (Ctrl+S)";
        saveButton.Click += (s, e) => SaveDocument();

        toolStrip.Items.Add(newButton);
        toolStrip.Items.Add(openButton);
        toolStrip.Items.Add(saveButton);
        toolStrip.Items.Add(new ToolStripSeparator());

        // Bold
        boldButton = new ToolStripButton("B");
        boldButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        boldButton.ToolTipText = "Bold (Ctrl+B)";
        boldButton.Click += (s, e) => ApplyBold();

        // Italic
        italicButton = new ToolStripButton("I");
        italicButton.Font = new Font("Segoe UI", 9, FontStyle.Italic);
        italicButton.ToolTipText = "Italic (Ctrl+I)";
        italicButton.Click += (s, e) => ApplyItalic();

        // Underline
        underlineButton = new ToolStripButton("U");
        underlineButton.Font = new Font("Segoe UI", 9, FontStyle.Underline);
        underlineButton.ToolTipText = "Underline (Ctrl+U)";
        underlineButton.Click += (s, e) => ApplyUnderline();

        toolStrip.Items.Add(boldButton);
        toolStrip.Items.Add(italicButton);
        toolStrip.Items.Add(underlineButton);
        toolStrip.Items.Add(new ToolStripSeparator());

        // Font Family
        fontFamilyCombo = new ToolStripComboBox();
        fontFamilyCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        fontFamilyCombo.Width = 150;
        fontFamilyCombo.ToolTipText = "Font Family";

        using (var fonts = new InstalledFontCollection())
        {
            foreach (var family in fonts.Families)
            {
                fontFamilyCombo.Items.Add(family.Name);
            }
        }

        if (fontFamilyCombo.Items.Contains("Consolas"))
            fontFamilyCombo.SelectedItem = "Consolas";
        else if (fontFamilyCombo.Items.Contains("Courier New"))
            fontFamilyCombo.SelectedItem = "Courier New";
        else if (fontFamilyCombo.Items.Count > 0)
            fontFamilyCombo.SelectedIndex = 0;

        fontFamilyCombo.SelectedIndexChanged += FontFamilyCombo_SelectedIndexChanged;

        // Font Size
        fontSizeCombo = new ToolStripComboBox();
        fontSizeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        fontSizeCombo.Width = 50;
        fontSizeCombo.ToolTipText = "Font Size";

        var sizes = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72" };
        fontSizeCombo.Items.AddRange(sizes);
        fontSizeCombo.SelectedItem = "11";
        fontSizeCombo.SelectedIndexChanged += FontSizeCombo_SelectedIndexChanged;

        toolStrip.Items.Add(fontFamilyCombo);
        toolStrip.Items.Add(fontSizeCombo);
        toolStrip.Items.Add(new ToolStripSeparator());

        // Preview Toggle
        previewButton = new ToolStripButton("Preview");
        previewButton.ToolTipText = "Toggle Preview (Ctrl+P)";
        previewButton.CheckOnClick = true;
        previewButton.Click += (s, e) => TogglePreview();

        toolStrip.Items.Add(previewButton);
        toolStrip.Items.Add(new ToolStripSeparator());

        // Theme dropdown
        toolStrip.Items.Add(new ToolStripLabel("Theme:"));
        themeCombo = new ToolStripComboBox();
        themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        themeCombo.Width = 80;
        themeCombo.Items.AddRange(new[] { "System", "Light", "Dark", "Matrix" });
        themeCombo.SelectedItem = "System";
        themeCombo.SelectedIndexChanged += ThemeCombo_SelectedIndexChanged;
        toolStrip.Items.Add(themeCombo);
    }

    private void ThemeCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        currentTheme = themeCombo.SelectedItem?.ToString() ?? "System";
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        Color backColor, foreColor, menuBackColor, editorBackColor, editorForeColor, separatorColor;
        Color hoverBackColor, hoverForeColor;

        switch (currentTheme)
        {
            case "Dark":
                backColor = Color.FromArgb(30, 30, 30);
                foreColor = Color.White;
                menuBackColor = Color.FromArgb(45, 45, 45);
                editorBackColor = Color.FromArgb(30, 30, 30);
                editorForeColor = Color.White;
                separatorColor = Color.FromArgb(60, 60, 60);
                hoverBackColor = Color.White;
                hoverForeColor = Color.Black;
                break;

            case "Matrix":
                backColor = Color.Black;
                foreColor = Color.FromArgb(0, 255, 0);
                menuBackColor = Color.FromArgb(10, 10, 10);
                editorBackColor = Color.Black;
                editorForeColor = Color.FromArgb(0, 255, 0);
                separatorColor = Color.FromArgb(0, 100, 0);
                hoverBackColor = Color.FromArgb(0, 255, 0);
                hoverForeColor = Color.Black;
                break;

            case "Light":
                backColor = Color.White;
                foreColor = Color.Black;
                menuBackColor = Color.FromArgb(248, 248, 248);
                editorBackColor = Color.White;
                editorForeColor = Color.Black;
                separatorColor = Color.FromArgb(220, 220, 220);
                hoverBackColor = SystemColors.Highlight;
                hoverForeColor = SystemColors.HighlightText;
                break;

            case "System":
            default:
                backColor = SystemColors.Control;
                foreColor = SystemColors.ControlText;
                menuBackColor = SystemColors.Control;
                editorBackColor = SystemColors.Window;
                editorForeColor = SystemColors.WindowText;
                separatorColor = SystemColors.ControlDark;
                hoverBackColor = SystemColors.Highlight;
                hoverForeColor = SystemColors.HighlightText;
                break;
        }

        // Apply custom renderer for hover colors
        var renderer = new ThemedToolStripRenderer
        {
            HoverBackColor = hoverBackColor,
            HoverForeColor = hoverForeColor,
            PressedBackColor = hoverBackColor
        };
        toolStrip.Renderer = renderer;
        menuStrip.Renderer = renderer;

        // Apply to main form
        this.BackColor = backColor;

        // Apply to menu strip
        menuStrip.BackColor = menuBackColor;
        menuStrip.ForeColor = foreColor;
        foreach (ToolStripItem item in menuStrip.Items)
        {
            item.ForeColor = foreColor;
            if (item is ToolStripMenuItem menuItem)
            {
                ApplyThemeToMenuItems(menuItem, menuBackColor, foreColor);
            }
        }

        // Apply to tool strip
        toolStrip.BackColor = menuBackColor;
        toolStrip.ForeColor = foreColor;
        foreach (ToolStripItem item in toolStrip.Items)
        {
            item.ForeColor = foreColor;
            item.BackColor = menuBackColor;

            // Fix combo box colors
            if (item is ToolStripComboBox comboItem)
            {
                comboItem.BackColor = editorBackColor;
                comboItem.ForeColor = editorForeColor;
            }
        }

        // Apply to separator
        separator.BackColor = separatorColor;

        // Apply to tab panel and tab control background (area behind tabs)
        tabPanel.BackColor = menuBackColor;
        tabControl.BackColor = menuBackColor;
        tabControl.HeaderBackColor = menuBackColor;
        tabControl.ForeColor = foreColor;

        // Apply to all editors
        foreach (TabPage tab in tabControl.TabPages)
        {
            tab.BackColor = editorBackColor;
            var editor = GetEditor(tab);
            if (editor != null)
            {
                editor.BackColor = editorBackColor;
                editor.ForeColor = editorForeColor;
            }
        }

        // Apply to status strip
        statusStrip.BackColor = menuBackColor;
        statusStrip.ForeColor = foreColor;
        statusLabel.ForeColor = foreColor;

        // Force redraw of tabs
        tabControl.Invalidate();
    }

    private void ApplyThemeToMenuItems(ToolStripMenuItem menuItem, Color backColor, Color foreColor)
    {
        menuItem.BackColor = backColor;
        menuItem.ForeColor = foreColor;
        foreach (ToolStripItem subItem in menuItem.DropDownItems)
        {
            subItem.ForeColor = foreColor;
            subItem.BackColor = backColor;
            if (subItem is ToolStripMenuItem subMenuItem)
            {
                ApplyThemeToMenuItems(subMenuItem, backColor, foreColor);
            }
        }
    }

    private void SetupKeyboardShortcuts()
    {
        this.KeyPreview = true;
        this.KeyDown += MainForm_KeyDown;
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Ctrl+Tab and Ctrl+Shift+Tab for tab switching
        if (e.Control && e.KeyCode == Keys.Tab)
        {
            if (e.Shift)
                PreviousTab();
            else
                NextTab();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    public void NewDocument()
    {
        untitledCount++;
        var tabPage = CreateTab($"Untitled {untitledCount}", null, ".txt");
        tabControl.SelectedTab = tabPage;
        UpdateStatus();
    }

    private void OpenDocument()
    {
        using var dialog = new OpenFileDialog();
        dialog.Filter = "Text Files (*.txt)|*.txt|Markdown Files (*.md)|*.md|All Files (*.*)|*.*";
        dialog.FilterIndex = 3;

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            OpenFile(dialog.FileName);
        }
    }

    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB limit

    public void OpenFile(string filePath)
    {
        // Validate and normalize the path
        try
        {
            filePath = Path.GetFullPath(filePath);
        }
        catch (Exception)
        {
            MessageBox.Show("Invalid file path.", "NicPad", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!File.Exists(filePath))
        {
            MessageBox.Show($"File not found: {filePath}", "NicPad", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Check file size before reading
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
            var result = MessageBox.Show(
                $"This file is {sizeMB:F1} MB. Opening very large files may cause performance issues. Continue?",
                "NicPad - Large File Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }

        // Check if file is already open
        foreach (TabPage tab in tabControl.TabPages)
        {
            var doc = (DocumentInfo)tab.Tag!;
            if (doc.FilePath != null && doc.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            {
                tabControl.SelectedTab = tab;
                return;
            }
        }

        var extension = Path.GetExtension(filePath).ToLower();
        var tabPage = CreateTab(Path.GetFileName(filePath), filePath, extension);

        var editor = GetEditor(tabPage);
        if (editor != null)
        {
            var content = File.ReadAllText(filePath);
            if (extension == ".txt")
            {
                // For txt files, try to load as RTF first
                try
                {
                    editor.LoadFile(filePath, RichTextBoxStreamType.PlainText);
                }
                catch
                {
                    editor.Text = content;
                }
            }
            else
            {
                editor.Text = content;
            }

            var docInfo = (DocumentInfo)tabPage.Tag!;
            docInfo.IsModified = false;
        }

        tabControl.SelectedTab = tabPage;
        UpdateStatus();
    }

    private void SaveDocument()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        if (doc.FilePath == null)
        {
            SaveDocumentAs();
        }
        else
        {
            SaveToFile(currentTab, doc.FilePath);
        }
    }

    private void SaveDocumentAs()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;

        using var dialog = new SaveFileDialog();
        dialog.Filter = "Text Files (*.txt)|*.txt|Markdown Files (*.md)|*.md|All Files (*.*)|*.*";
        dialog.FilterIndex = doc.Extension == ".md" ? 2 : 1;

        if (doc.FilePath != null)
        {
            dialog.FileName = Path.GetFileName(doc.FilePath);
            dialog.InitialDirectory = Path.GetDirectoryName(doc.FilePath);
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            SaveToFile(currentTab, dialog.FileName);
            doc.FilePath = dialog.FileName;
            doc.Extension = Path.GetExtension(dialog.FileName).ToLower();
            currentTab.Text = Path.GetFileName(dialog.FileName);
            UpdatePreviewVisibility();
        }
    }

    private void SaveToFile(TabPage tab, string filePath)
    {
        var editor = GetEditor(tab);
        if (editor == null) return;

        var doc = (DocumentInfo)tab.Tag!;
        var extension = Path.GetExtension(filePath).ToLower();

        if (extension == ".txt")
        {
            editor.SaveFile(filePath, RichTextBoxStreamType.PlainText);
        }
        else
        {
            File.WriteAllText(filePath, editor.Text);
        }

        doc.IsModified = false;
        UpdateTabTitle(tab);
        UpdateStatus();
    }

    private void CloseCurrentTab()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        if (doc.IsModified)
        {
            var result = MessageBox.Show(
                $"Do you want to save changes to {currentTab.Text.TrimEnd('*')}?",
                "NicPad",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                SaveDocument();
                if (doc.IsModified) return; // Save was cancelled
            }
            else if (result == DialogResult.Cancel)
            {
                return;
            }
        }

        tabControl.TabPages.Remove(currentTab);

        if (tabControl.TabPages.Count == 0)
        {
            NewDocument();
        }
    }

    private (Color back, Color fore) GetEditorColors()
    {
        return currentTheme switch
        {
            "Dark" => (Color.FromArgb(30, 30, 30), Color.White),
            "Matrix" => (Color.Black, Color.FromArgb(0, 255, 0)),
            "Light" => (Color.White, Color.Black),
            _ => (SystemColors.Window, SystemColors.WindowText)
        };
    }

    private TabPage CreateTab(string title, string? filePath, string extension)
    {
        var (editorBack, editorFore) = GetEditorColors();

        var tabPage = new TabPage(title);
        tabPage.BackColor = editorBack;

        var docInfo = new DocumentInfo
        {
            FilePath = filePath,
            Extension = extension,
            IsModified = false,
            FontFamily = fontFamilyCombo.SelectedItem?.ToString() ?? "Consolas",
            FontSize = ClampFontSize(float.TryParse(fontSizeCombo.SelectedItem?.ToString(), out float parsedSize) ? parsedSize : 11f),
            IsPreviewMode = false
        };
        tabPage.Tag = docInfo;

        // Create split container for editor and preview
        var container = new Panel();
        container.Dock = DockStyle.Fill;

        // Create editor
        var editor = new RichTextBox();
        editor.Name = "Editor";
        editor.Dock = DockStyle.Fill;
        editor.Font = new Font(docInfo.FontFamily, docInfo.FontSize);
        editor.BorderStyle = BorderStyle.None;
        editor.AcceptsTab = true;
        editor.BackColor = editorBack;
        editor.ForeColor = editorFore;
        editor.TextChanged += Editor_TextChanged;
        editor.SelectionChanged += Editor_SelectionChanged;

        // Create preview panel (WebBrowser for markdown rendering)
        var preview = new WebBrowser();
        preview.Name = "Preview";
        preview.Dock = DockStyle.Fill;
        preview.Visible = false;
        preview.ScriptErrorsSuppressed = true;
        preview.IsWebBrowserContextMenuEnabled = false;
        preview.AllowNavigation = false;
        preview.WebBrowserShortcutsEnabled = false;

        container.Controls.Add(editor);
        container.Controls.Add(preview);
        tabPage.Controls.Add(container);

        tabControl.TabPages.Add(tabPage);
        return tabPage;
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        if (!doc.IsModified)
        {
            doc.IsModified = true;
            UpdateTabTitle(currentTab);
        }

        // Update preview if in preview mode
        if (doc.IsPreviewMode && doc.Extension == ".md")
        {
            UpdatePreview();
        }
    }

    private void Editor_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateFormattingButtons();
    }

    private void UpdateFormattingButtons()
    {
        var editor = GetCurrentEditor();
        if (editor == null) return;

        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;

        if (doc.Extension == ".txt")
        {
            boldButton.Checked = editor.SelectionFont?.Bold ?? false;
            italicButton.Checked = editor.SelectionFont?.Italic ?? false;
            underlineButton.Checked = editor.SelectionFont?.Underline ?? false;
        }
    }

    private void UpdateTabTitle(TabPage tab)
    {
        var doc = (DocumentInfo)tab.Tag!;
        var title = doc.FilePath != null ? Path.GetFileName(doc.FilePath) : tab.Text.TrimEnd('*');
        tab.Text = doc.IsModified ? title + "*" : title;
    }

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateFontControls();
        UpdatePreviewVisibility();
        UpdateStatus();
    }

    private void TabControl_MouseClick(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < tabControl.TabPages.Count; i++)
        {
            var tabRect = tabControl.GetTabRect(i);
            if (tabRect.Contains(e.Location))
            {
                // Check if close button was clicked
                var closeRect = GetCloseButtonRect(tabRect);
                if (e.Button == MouseButtons.Left && closeRect.Contains(e.Location))
                {
                    var tab = tabControl.TabPages[i];
                    tabControl.SelectedTab = tab;
                    CloseCurrentTab();
                    return;
                }

                // Middle-click anywhere on tab to close
                if (e.Button == MouseButtons.Middle)
                {
                    var tab = tabControl.TabPages[i];
                    tabControl.SelectedTab = tab;
                    CloseCurrentTab();
                    return;
                }
                break;
            }
        }
    }

    private void UpdateFontControls()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;

        fontFamilyCombo.SelectedIndexChanged -= FontFamilyCombo_SelectedIndexChanged;
        fontSizeCombo.SelectedIndexChanged -= FontSizeCombo_SelectedIndexChanged;

        fontFamilyCombo.SelectedItem = doc.FontFamily;
        fontSizeCombo.SelectedItem = ((int)doc.FontSize).ToString();

        fontFamilyCombo.SelectedIndexChanged += FontFamilyCombo_SelectedIndexChanged;
        fontSizeCombo.SelectedIndexChanged += FontSizeCombo_SelectedIndexChanged;
    }

    private void UpdatePreviewVisibility()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;

        // Enable preview button only for markdown files
        previewButton.Enabled = doc.Extension == ".md";
        previewButton.Checked = doc.IsPreviewMode && doc.Extension == ".md";

        var editor = GetEditor(currentTab);
        var preview = GetPreview(currentTab);

        if (editor != null && preview != null)
        {
            if (doc.IsPreviewMode && doc.Extension == ".md")
            {
                editor.Visible = false;
                preview.Visible = true;
                UpdatePreview();
            }
            else
            {
                editor.Visible = true;
                preview.Visible = false;
            }
        }
    }

    private void FontFamilyCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        doc.FontFamily = fontFamilyCombo.SelectedItem?.ToString() ?? "Consolas";

        var editor = GetEditor(currentTab);
        if (editor != null)
        {
            editor.Font = new Font(doc.FontFamily, doc.FontSize);
        }
    }

    private void FontSizeCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        if (float.TryParse(fontSizeCombo.SelectedItem?.ToString(), out float size))
        {
            doc.FontSize = size;
            var editor = GetEditor(currentTab);
            if (editor != null)
            {
                editor.Font = new Font(doc.FontFamily, doc.FontSize);
            }
        }
    }

    private void ShowFontDialog()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        var editor = GetEditor(currentTab);
        if (editor == null) return;

        using var dialog = new FontDialog();
        dialog.Font = new Font(doc.FontFamily, doc.FontSize);

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            doc.FontFamily = dialog.Font.FontFamily.Name;
            doc.FontSize = dialog.Font.Size;
            editor.Font = new Font(doc.FontFamily, doc.FontSize);
            UpdateFontControls();
        }
    }

    private void ApplyBold()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        var editor = GetEditor(currentTab);
        if (editor == null) return;

        if (doc.Extension == ".md")
        {
            // Insert markdown syntax
            WrapSelection(editor, "**", "**");
        }
        else
        {
            // Apply rich text formatting
            ToggleFontStyle(editor, FontStyle.Bold);
        }
    }

    private void ApplyItalic()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        var editor = GetEditor(currentTab);
        if (editor == null) return;

        if (doc.Extension == ".md")
        {
            WrapSelection(editor, "*", "*");
        }
        else
        {
            ToggleFontStyle(editor, FontStyle.Italic);
        }
    }

    private void ApplyUnderline()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        var editor = GetEditor(currentTab);
        if (editor == null) return;

        if (doc.Extension == ".md")
        {
            // Underline in markdown (using HTML tag)
            WrapSelection(editor, "<u>", "</u>");
        }
        else
        {
            ToggleFontStyle(editor, FontStyle.Underline);
        }
    }

    private void WrapSelection(RichTextBox editor, string prefix, string suffix)
    {
        var selectedText = editor.SelectedText;
        var selectionStart = editor.SelectionStart;

        if (string.IsNullOrEmpty(selectedText))
        {
            editor.SelectedText = prefix + suffix;
            editor.SelectionStart = selectionStart + prefix.Length;
        }
        else
        {
            editor.SelectedText = prefix + selectedText + suffix;
            editor.SelectionStart = selectionStart + prefix.Length;
            editor.SelectionLength = selectedText.Length;
        }

        editor.Focus();
    }

    private void ToggleFontStyle(RichTextBox editor, FontStyle style)
    {
        if (editor.SelectionLength == 0)
        {
            var currentFont = editor.SelectionFont ?? editor.Font;
            var newStyle = currentFont.Style ^ style;
            editor.SelectionFont = new Font(currentFont, newStyle);
        }
        else
        {
            var currentFont = editor.SelectionFont ?? editor.Font;
            var newStyle = currentFont.Style ^ style;
            editor.SelectionFont = new Font(currentFont, newStyle);
        }

        editor.Focus();
        UpdateFormattingButtons();
    }

    private void TogglePreview()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var doc = (DocumentInfo)currentTab.Tag!;
        if (doc.Extension != ".md") return;

        doc.IsPreviewMode = !doc.IsPreviewMode;
        previewButton.Checked = doc.IsPreviewMode;
        UpdatePreviewVisibility();
    }

    private void UpdatePreview()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null) return;

        var editor = GetEditor(currentTab);
        var preview = GetPreview(currentTab);
        if (editor == null || preview == null) return;

        var markdown = editor.Text;
        var html = SanitizeHtml(Markdown.ToHtml(markdown, markdownPipeline));

        var fullHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{
            font-family: 'Segoe UI', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 20px;
            max-width: 800px;
            margin: 0 auto;
            color: #333;
            background: #fff;
        }}
        h1, h2, h3, h4, h5, h6 {{
            color: #222;
            margin-top: 1.5em;
            margin-bottom: 0.5em;
        }}
        code {{
            background: #f4f4f4;
            padding: 2px 6px;
            border-radius: 3px;
            font-family: Consolas, monospace;
        }}
        pre {{
            background: #f4f4f4;
            padding: 15px;
            border-radius: 5px;
            overflow-x: auto;
        }}
        pre code {{
            padding: 0;
            background: none;
        }}
        blockquote {{
            border-left: 4px solid #ddd;
            margin-left: 0;
            padding-left: 20px;
            color: #666;
        }}
        a {{
            color: #0066cc;
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
        }}
        th, td {{
            border: 1px solid #ddd;
            padding: 8px;
            text-align: left;
        }}
        th {{
            background: #f4f4f4;
        }}
        img {{
            max-width: 100%;
        }}
        u {{
            text-decoration: underline;
        }}
    </style>
</head>
<body>
{html}
</body>
</html>";

        preview.AllowNavigation = true;  // Temporarily allow to load content
        preview.DocumentText = fullHtml;
        preview.AllowNavigation = false; // Re-disable after loading
    }

    private static string SanitizeHtml(string html)
    {
        // Remove <script> tags and their content
        html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        // Remove <iframe>, <object>, <embed>, <applet>, <form> tags
        html = Regex.Replace(html, @"<(iframe|object|embed|applet|form|meta|link)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<(iframe|object|embed|applet|form|meta|link)\b[^>]*/?>", "", RegexOptions.IgnoreCase);
        // Remove event handler attributes (onclick, onerror, onload, etc.)
        html = Regex.Replace(html, @"\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", "", RegexOptions.IgnoreCase);
        // Remove javascript: and vbscript: URLs in href/src attributes
        html = Regex.Replace(html, @"(href|src|action)\s*=\s*(?:""javascript:[^""]*""|'javascript:[^']*')", "$1=\"\"", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"(href|src|action)\s*=\s*(?:""vbscript:[^""]*""|'vbscript:[^']*')", "$1=\"\"", RegexOptions.IgnoreCase);
        // Remove style attributes that could contain expressions
        html = Regex.Replace(html, @"style\s*=\s*""[^""]*expression\s*\([^""]*""", "", RegexOptions.IgnoreCase);
        // Remove data: URLs in src attributes (can embed scripts)
        html = Regex.Replace(html, @"src\s*=\s*""data:[^""]*""", "src=\"\"", RegexOptions.IgnoreCase);
        return html;
    }

    private void NextTab()
    {
        if (tabControl.TabPages.Count <= 1) return;
        var nextIndex = (tabControl.SelectedIndex + 1) % tabControl.TabPages.Count;
        tabControl.SelectedIndex = nextIndex;
    }

    private void PreviousTab()
    {
        if (tabControl.TabPages.Count <= 1) return;
        var prevIndex = tabControl.SelectedIndex - 1;
        if (prevIndex < 0) prevIndex = tabControl.TabPages.Count - 1;
        tabControl.SelectedIndex = prevIndex;
    }

    private void UpdateStatus()
    {
        var currentTab = tabControl.SelectedTab;
        if (currentTab == null)
        {
            statusLabel.Text = "Ready";
            return;
        }

        var doc = (DocumentInfo)currentTab.Tag!;
        var editor = GetEditor(currentTab);

        var fileName = doc.FilePath ?? currentTab.Text.TrimEnd('*');
        var fileType = doc.Extension.ToUpper().TrimStart('.');
        var lines = editor?.Lines.Length ?? 0;

        statusLabel.Text = $"{fileName} | {fileType} | Lines: {lines}";
    }

    private RichTextBox? GetCurrentEditor()
    {
        var currentTab = tabControl.SelectedTab;
        return currentTab != null ? GetEditor(currentTab) : null;
    }

    private RichTextBox? GetEditor(TabPage tab)
    {
        var container = tab.Controls.OfType<Panel>().FirstOrDefault();
        return container?.Controls.OfType<RichTextBox>().FirstOrDefault(c => c.Name == "Editor");
    }

    private WebBrowser? GetPreview(TabPage tab)
    {
        var container = tab.Controls.OfType<Panel>().FirstOrDefault();
        return container?.Controls.OfType<WebBrowser>().FirstOrDefault(c => c.Name == "Preview");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Check for unsaved changes
        foreach (TabPage tab in tabControl.TabPages)
        {
            var doc = (DocumentInfo)tab.Tag!;
            if (doc.IsModified)
            {
                var result = MessageBox.Show(
                    $"Do you want to save changes to {tab.Text.TrimEnd('*')}?",
                    "NicPad",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    tabControl.SelectedTab = tab;
                    SaveDocument();
                    if (doc.IsModified)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
                else if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        // Save session before closing
        SaveSession();

        base.OnFormClosing(e);
    }

    private void SaveSession()
    {
        try
        {
            var session = new SessionData
            {
                SelectedTabIndex = tabControl.SelectedIndex,
                Theme = currentTheme,
                OpenFiles = new List<OpenFileInfo>()
            };

            foreach (TabPage tab in tabControl.TabPages)
            {
                var doc = (DocumentInfo)tab.Tag!;
                // Only save files that have been saved to disk
                if (doc.FilePath != null && File.Exists(doc.FilePath))
                {
                    session.OpenFiles.Add(new OpenFileInfo
                    {
                        FilePath = doc.FilePath,
                        FontFamily = doc.FontFamily,
                        FontSize = ClampFontSize(doc.FontSize)
                    });
                }
            }

            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sessionFilePath, json);
        }
        catch (UnauthorizedAccessException)
        {
            // User doesn't have write permissions to AppData - not critical
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
        }
    }

    private static readonly string[] ValidThemes = { "System", "Light", "Dark", "Matrix" };

    private static float ClampFontSize(float size)
    {
        return Math.Clamp(size, 6f, 72f);
    }

    private static bool IsValidFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || fontFamily.Length > 100)
            return false;
        try
        {
            using var testFont = new Font(fontFamily, 10f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool RestoreSession()
    {
        try
        {
            if (!File.Exists(sessionFilePath))
                return false;

            // Limit session file size to prevent memory exhaustion (1 MB max)
            var sessionFileInfo = new FileInfo(sessionFilePath);
            if (sessionFileInfo.Length > 1024 * 1024)
            {
                System.Diagnostics.Debug.WriteLine("Session file too large, skipping restore.");
                return false;
            }

            var json = File.ReadAllText(sessionFilePath);
            var session = JsonSerializer.Deserialize<SessionData>(json);

            if (session == null)
                return false;

            // Validate and restore theme
            if (!string.IsNullOrEmpty(session.Theme) && ValidThemes.Contains(session.Theme))
            {
                currentTheme = session.Theme;
                themeCombo.SelectedItem = currentTheme;
                ApplyTheme();
            }

            if (session.OpenFiles == null || session.OpenFiles.Count == 0)
                return false;

            // Limit number of restored files to prevent abuse
            var filesToRestore = session.OpenFiles.Take(50);

            foreach (var fileInfo in filesToRestore)
            {
                // Validate file path
                if (string.IsNullOrWhiteSpace(fileInfo.FilePath) || fileInfo.FilePath.Length > 1024)
                    continue;

                try
                {
                    var fullPath = Path.GetFullPath(fileInfo.FilePath);
                    if (!File.Exists(fullPath))
                        continue;

                    OpenFile(fullPath);

                    // Restore font settings with validation
                    var tab = tabControl.TabPages[tabControl.TabPages.Count - 1];
                    var doc = (DocumentInfo)tab.Tag!;

                    var fontFamily = IsValidFontFamily(fileInfo.FontFamily) ? fileInfo.FontFamily : "Consolas";
                    var fontSize = ClampFontSize(fileInfo.FontSize);

                    doc.FontFamily = fontFamily;
                    doc.FontSize = fontSize;

                    var editor = GetEditor(tab);
                    if (editor != null)
                    {
                        editor.Font = new Font(doc.FontFamily, doc.FontSize);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to restore file '{fileInfo.FilePath}': {ex.Message}");
                    continue;
                }
            }

            // Restore selected tab
            if (session.SelectedTabIndex >= 0 && session.SelectedTabIndex < tabControl.TabPages.Count)
            {
                tabControl.SelectedIndex = session.SelectedTabIndex;
            }

            UpdateFontControls();
            return tabControl.TabPages.Count > 0;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Session file is corrupted: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read session file: {ex.Message}");
            return false;
        }
    }
}

public class DocumentInfo
{
    public string? FilePath { get; set; }
    public string Extension { get; set; } = ".txt";
    public bool IsModified { get; set; }
    public string FontFamily { get; set; } = "Consolas";
    public float FontSize { get; set; } = 11f;
    public bool IsPreviewMode { get; set; }
}

public class SessionData
{
    public int SelectedTabIndex { get; set; }
    public string Theme { get; set; } = "System";
    public List<OpenFileInfo> OpenFiles { get; set; } = new();
}

public class OpenFileInfo
{
    public string FilePath { get; set; } = "";
    public string FontFamily { get; set; } = "Consolas";
    public float FontSize { get; set; } = 11f;
}

public class ThemedToolStripRenderer : ToolStripProfessionalRenderer
{
    public Color HoverBackColor { get; set; } = SystemColors.Highlight;
    public Color HoverForeColor { get; set; } = SystemColors.HighlightText;
    public Color PressedBackColor { get; set; } = SystemColors.HotTrack;

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Don't draw the default bottom border/gradient line
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        // Paint a solid background instead of the default gradient
        using (var brush = new SolidBrush(e.ToolStrip.BackColor))
        {
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is ToolStripButton button)
        {
            if (button.Selected || button.Pressed)
            {
                var rect = new Rectangle(Point.Empty, e.Item.Size);
                var color = button.Pressed ? PressedBackColor : HoverBackColor;
                using (var brush = new SolidBrush(color))
                {
                    e.Graphics.FillRectangle(brush, rect);
                }
                return;
            }
        }
        base.OnRenderButtonBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Change text color on hover for buttons
        if (e.Item is ToolStripButton button && (button.Selected || button.Pressed))
        {
            e.TextColor = HoverForeColor;
        }
        // Change text color on hover for menu items
        else if (e.Item is ToolStripMenuItem menuItem && menuItem.Selected)
        {
            e.TextColor = HoverForeColor;
        }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected)
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            using (var brush = new SolidBrush(HoverBackColor))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
            return;
        }
        base.OnRenderMenuItemBackground(e);
    }
}

public class ThemedToolStrip : ToolStrip
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Paint over the bottom border line that the renderer draws
        using (var brush = new SolidBrush(BackColor))
        {
            e.Graphics.FillRectangle(brush, 0, Height - 2, Width, 2);
        }
    }
}

public class ThemedMenuStrip : MenuStrip
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Paint over the bottom border line that the renderer draws
        using (var brush = new SolidBrush(BackColor))
        {
            e.Graphics.FillRectangle(brush, 0, Height - 2, Width, 2);
        }
    }
}

public class ThemedTabControl : TabControl
{
    private const int WM_PAINT = 0x000F;

    // Explicit theme color - don't rely on BackColor which the native control resets
    public Color HeaderBackColor { get; set; } = SystemColors.Control;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PAINT)
        {
            base.WndProc(ref m);
            BeginInvoke(new Action(PaintHeaderBackground));
            return;
        }
        base.WndProc(ref m);
    }

    private void PaintHeaderBackground()
    {
        if (TabPages.Count == 0 || !IsHandleCreated || IsDisposed) return;
        try
        {
            using var g = CreateGraphics();
            using var brush = new SolidBrush(HeaderBackColor);
            var firstTab = GetTabRect(0);
            int headerHeight = firstTab.Bottom + 4;

            // Fill the entire tab header area with theme color
            g.FillRectangle(brush, 0, 0, Width, headerHeight);

            // Redraw each tab on top
            for (int i = 0; i < TabPages.Count; i++)
            {
                var args = new DrawItemEventArgs(g, Font, GetTabRect(i), i,
                    i == SelectedIndex ? DrawItemState.Selected : DrawItemState.Default,
                    ForeColor, BackColor);
                OnDrawItem(args);
            }
        }
        catch (ObjectDisposedException)
        {
            // Control was disposed between the BeginInvoke and execution - safe to ignore
        }
    }
}
