using System.Diagnostics;
using NicPad.Core;
using NicPad.UI;

namespace NicPad;

public partial class MainForm
{
    private ToolStripMenuItem recentMenu = null!;
    private ToolStripMenuItem previewMenuItem = null!;
    private ToolStripMenuItem splitMenuItem = null!;
    private ToolStripMenuItem wordWrapMenuItem = null!;
    private ToolStripMenuItem rtlMenuItem = null!;
    private ToolStripMenuItem encodingMenu = null!;
    private ToolStripMenuItem lineEndingMenu = null!;
    private ToolStripMenuItem markdownMenu = null!;
    private ToolStripMenuItem themeMenu = null!;
    private ToolStripMenuItem readAloudMenuItem = null!;
    private ToolStripComboBox fontFamilyCombo = null!;
    private ToolStripComboBox fontSizeCombo = null!;
    private ToolStripComboBox themeCombo = null!;
    private ToolStripButton previewButton = null!;
    private ToolStripButton splitButton = null!;
    private ToolStripButton boldButton = null!;
    private ToolStripButton italicButton = null!;
    private ToolStripButton underlineButton = null!;
    private ContextMenuStrip? tabMenu;
    private bool syncingControls;

    private static ToolStripMenuItem Item(string text, Action action, Keys keys = Keys.None, string? keyDisplay = null)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => action());
        if (keys != Keys.None)
        {
            item.ShortcutKeys = keys;
        }

        if (keyDisplay is not null)
        {
            item.ShortcutKeyDisplayString = keyDisplay;
        }

        return item;
    }

    private static ToolStripMenuItem Toggle(string text, bool initial, Action<bool> changed, Keys keys = Keys.None)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = initial };
        if (keys != Keys.None)
        {
            item.ShortcutKeys = keys;
        }

        item.CheckedChanged += (_, _) => changed(item.Checked);
        return item;
    }

    private static ToolStripMenuItem SubMenu(string text, params ToolStripItem[] items)
    {
        var menu = new ToolStripMenuItem(text);
        menu.DropDownItems.AddRange(items);
        return menu;
    }

    private void CreateMenus()
    {
        recentMenu = new ToolStripMenuItem("Recent &Files");
        recentMenu.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
        recentMenu.DropDownOpening += (_, _) => BuildRecentMenu();

        var file = SubMenu("&File",
            Item("&New", NewDocument, Keys.Control | Keys.N),
            Item("&Open...", OpenWithDialog, Keys.Control | Keys.O),
            recentMenu,
            Item("Reopen Closed &Tab", ReopenClosedTab, Keys.Control | Keys.Shift | Keys.T),
            new ToolStripSeparator(),
            Item("&Save", () => WithCurrent(t => Save(t)), Keys.Control | Keys.S),
            Item("Save &As...", () => WithCurrent(t => SaveAs(t)), Keys.Control | Keys.Shift | Keys.S),
            Item("Save A&ll", SaveAll),
            Item("&Reload from Disk", () => WithCurrent(ReloadWithPrompt)),
            new ToolStripSeparator(),
            Item("&Export as HTML...", () => WithCurrent(ExportHtml)),
            Item("&Print...", () => WithCurrent(t => Print(t, preview: false))),
            Item("Print Pre&view...", () => WithCurrent(t => Print(t, preview: true))),
            new ToolStripSeparator(),
            Item("&Close Tab", () => WithCurrent(t => CloseTab(t)), Keys.Control | Keys.W),
            Item("Close All Ta&bs", () => CloseTabs(Documents)),
            new ToolStripSeparator(),
            Item("E&xit", Close, Keys.None, "Alt+F4"));

        var edit = SubMenu("&Edit",
            Item("&Undo", () => Current?.Editor.Undo(), Keys.Control | Keys.Z),
            Item("&Redo", () => Current?.Editor.Redo(), Keys.Control | Keys.Y),
            new ToolStripSeparator(),
            Item("Cu&t", () => Current?.Editor.Cut(), Keys.Control | Keys.X),
            Item("&Copy", () => Current?.Editor.Copy(), Keys.Control | Keys.C),
            Item("&Paste", () => Current?.Editor.PasteSmart(), Keys.Control | Keys.V),
            new ToolStripSeparator(),
            Item("&Find...", () => ShowFind(replace: false), Keys.Control | Keys.F),
            Item("Find &Next", () => FindAgain(forward: true), Keys.F3),
            Item("Find Pre&vious", () => FindAgain(forward: false), Keys.Shift | Keys.F3),
            Item("R&eplace...", () => ShowFind(replace: true), Keys.Control | Keys.H),
            Item("&Go To Line...", GoToLine, Keys.Control | Keys.G),
            new ToolStripSeparator(),
            Item("&Duplicate Line", () => ApplyLineEdit((t, s, l) => TextOps.DuplicateLines(t, s, l)), Keys.Control | Keys.D),
            Item("Move Line &Up", () => ApplyLineEdit((t, s, l) => TextOps.MoveLines(t, s, l, up: true)), Keys.Alt | Keys.Up),
            Item("Move Line Do&wn", () => ApplyLineEdit((t, s, l) => TextOps.MoveLines(t, s, l, up: false)), Keys.Alt | Keys.Down),
            Item("Insert &Date/Time", InsertDateTime, Keys.F5),
            new ToolStripSeparator(),
            Item("Select &All", () => Current?.Editor.SelectAll(), Keys.Control | Keys.A));

        markdownMenu = SubMenu("&Markdown",
            Item("Heading &1", () => MarkdownEdit((t, s, l) => TextOps.SetHeading(t, s, l, 1)), Keys.Control | Keys.D1),
            Item("Heading &2", () => MarkdownEdit((t, s, l) => TextOps.SetHeading(t, s, l, 2)), Keys.Control | Keys.D2),
            Item("Heading &3", () => MarkdownEdit((t, s, l) => TextOps.SetHeading(t, s, l, 3)), Keys.Control | Keys.D3),
            new ToolStripSeparator(),
            Item("&Bulleted List", () => MarkdownEdit((t, s, l) => TextOps.ToggleList(t, s, l, ListKind.Bullet)), Keys.Control | Keys.Shift | Keys.D8),
            Item("&Numbered List", () => MarkdownEdit((t, s, l) => TextOps.ToggleList(t, s, l, ListKind.Numbered)), Keys.Control | Keys.Shift | Keys.D7),
            Item("&Task List", () => MarkdownEdit((t, s, l) => TextOps.ToggleList(t, s, l, ListKind.Task)), Keys.Control | Keys.Shift | Keys.D9),
            Item("&Quote", () => MarkdownEdit((t, s, l) => TextOps.ToggleList(t, s, l, ListKind.Quote))),
            new ToolStripSeparator(),
            Item("&Link", () => MarkdownEdit(TextOps.InsertLink), Keys.Control | Keys.K),
            Item("Inline &Code", () => MarkdownEdit((t, s, l) => TextOps.Wrap(t, s, l, "`", "`", "code")), Keys.Control | Keys.Oemtilde, "Ctrl+`"),
            Item("Code Bl&ock", () => MarkdownEdit(TextOps.InsertCodeBlock)),
            Item("T&able", () => MarkdownEdit((t, s, _) => TextOps.InsertBlock(t, s, TextOps.TableTemplate))),
            Item("&Horizontal Rule", () => MarkdownEdit((t, s, _) => TextOps.InsertBlock(t, s, "---"))));

        encodingMenu = new ToolStripMenuItem("&Encoding");
        foreach (var kind in Enum.GetValues<TextEncodingKind>())
        {
            var k = kind;
            encodingMenu.DropDownItems.Add(new ToolStripMenuItem(TextFileIO.DisplayName(k), null, (_, _) => SetEncoding(k)) { Tag = k });
        }

        encodingMenu.DropDownOpening += (_, _) => SyncCheckedTags(encodingMenu, Current?.Encoding);

        lineEndingMenu = SubMenu("Line E&ndings",
            new ToolStripMenuItem("Windows (CRLF)", null, (_, _) => SetLineEnding(LineEnding.CrLf)) { Tag = LineEnding.CrLf },
            new ToolStripMenuItem("Unix / macOS (LF)", null, (_, _) => SetLineEnding(LineEnding.Lf)) { Tag = LineEnding.Lf },
            new ToolStripMenuItem("Classic Mac (CR)", null, (_, _) => SetLineEnding(LineEnding.Cr)) { Tag = LineEnding.Cr });
        lineEndingMenu.DropDownOpening += (_, _) => SyncCheckedTags(lineEndingMenu, Current?.LineEnding);

        wordWrapMenuItem = Toggle("&Word Wrap", settings.WordWrap, SetWordWrap, Keys.Alt | Keys.Z);
        rtlMenuItem = Toggle("&Right-to-Left Reading Order", false, SetRightToLeft);

        var format = SubMenu("F&ormat",
            Item("&Bold", () => ApplyStyle(FontStyle.Bold, "**", "**"), Keys.Control | Keys.B),
            Item("&Italic", () => ApplyStyle(FontStyle.Italic, "*", "*"), Keys.Control | Keys.I),
            Item("&Underline", () => ApplyStyle(FontStyle.Underline, "<u>", "</u>"), Keys.Control | Keys.U),
            markdownMenu,
            new ToolStripSeparator(),
            Item("&Font...", ShowFontDialog),
            Item("Use Current Font as &Default", SetDefaultFont),
            new ToolStripSeparator(),
            wordWrapMenuItem,
            rtlMenuItem,
            new ToolStripSeparator(),
            encodingMenu,
            lineEndingMenu);
        format.DropDownOpening += (_, _) =>
        {
            markdownMenu.Enabled = Current?.Kind == DocumentKind.Markdown;
            rtlMenuItem.Checked = Current?.IsRightToLeft == true;
            encodingMenu.Enabled = Current?.Kind != DocumentKind.Rtf;
            lineEndingMenu.Enabled = Current?.Kind != DocumentKind.Rtf;
        };

        previewMenuItem = Item("&Preview", () => SetPreviewMode(PreviewMode.Full), Keys.Control | Keys.P);
        splitMenuItem = Item("&Split Preview", () => SetPreviewMode(PreviewMode.Split), Keys.Control | Keys.Shift | Keys.V);
        themeMenu = new ToolStripMenuItem("&Theme");

        var view = SubMenu("&View",
            previewMenuItem,
            splitMenuItem,
            new ToolStripSeparator(),
            Item("Zoom &In", () => Zoom(+0.1f), Keys.Control | Keys.Oemplus, "Ctrl++"),
            Item("Zoom &Out", () => Zoom(-0.1f), Keys.Control | Keys.OemMinus, "Ctrl+-"),
            Item("&Reset Zoom", () => Zoom(0), Keys.Control | Keys.D0),
            new ToolStripSeparator(),
            themeMenu,
            Toggle("Tool&bar", settings.ShowToolbar, on =>
            {
                settings.ShowToolbar = on;
                toolStrip.Visible = separator.Visible = on;
                SaveSettingsQuietly();
            }),
            Toggle("Status B&ar", settings.ShowStatusBar, on =>
            {
                settings.ShowStatusBar = on;
                statusStrip.Visible = on;
                SaveSettingsQuietly();
            }),
            Toggle("Always on &Top", settings.AlwaysOnTop, on =>
            {
                settings.AlwaysOnTop = on;
                TopMost = on;
                SaveSettingsQuietly();
            }),
            new ToolStripSeparator(),
            Item("&Next Tab", NextTab, Keys.None, "Ctrl+Tab"),
            Item("Pre&vious Tab", PreviousTab, Keys.None, "Ctrl+Shift+Tab"));
        view.DropDownOpening += (_, _) => UpdatePreviewUi();

        readAloudMenuItem = Item("&Read Aloud", ToggleReadAloud, Keys.Control | Keys.Shift | Keys.R);
        var voiceMenu = new ToolStripMenuItem("Reading &Voice");
        voiceMenu.DropDownOpening += (_, _) => BuildVoiceMenu(voiceMenu);
        voiceMenu.DropDownItems.Add(new ToolStripMenuItem("(loading)") { Enabled = false });
        var speedMenu = SubMenu("Reading &Speed",
            new ToolStripMenuItem("Slow", null, (_, _) => SetSpeechRate(-3)) { Tag = -3 },
            new ToolStripMenuItem("Normal", null, (_, _) => SetSpeechRate(0)) { Tag = 0 },
            new ToolStripMenuItem("Fast", null, (_, _) => SetSpeechRate(3)) { Tag = 3 });
        speedMenu.DropDownOpening += (_, _) => SyncCheckedTags(speedMenu, settings.SpeechRate);

        var tools = SubMenu("&Tools",
            readAloudMenuItem,
            voiceMenu,
            speedMenu,
            Item("Voice &Typing", StartVoiceTyping, Keys.None, "Win+H"),
            new ToolStripSeparator(),
            Toggle("Allow Remote &Images in Preview", settings.AllowRemoteImages, on =>
            {
                settings.AllowRemoteImages = on;
                SaveSettingsQuietly();
                RenderPreview(Current);
            }),
            Toggle("Auto-&Backup Unsaved Changes", settings.AutoBackup, on =>
            {
                settings.AutoBackup = on;
                backupTimer.Enabled = on;
                SaveSettingsQuietly();
            }),
            Toggle("Remember Unsaved Changes on &Exit", settings.HotExit, on =>
            {
                settings.HotExit = on;
                SaveSettingsQuietly();
            }),
            Toggle("Open Files in &One Window", settings.SingleInstance, on =>
            {
                settings.SingleInstance = on;
                SaveSettingsQuietly();
                ShowMessage("Takes effect the next time NicPad starts.");
            }),
            new ToolStripSeparator(),
            Item("Open &Settings Folder", () => OpenFolder(paths.Root)));

        var help = SubMenu("&Help",
            Item("&Keyboard Shortcuts", ShowShortcuts, Keys.F1),
            Item("&About NicPad", ShowAbout));

        menuStrip.Items.AddRange([file, edit, format, view, tools, help]);
    }

    private void CreateToolbar()
    {
        ToolStripButton Button(string text, string tip, Action action)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, AccessibleName = tip };
            b.Click += (_, _) => action();
            return b;
        }

        toolStrip.Items.Add(Button("New", "New (Ctrl+N)", NewDocument));
        toolStrip.Items.Add(Button("Open", "Open (Ctrl+O)", OpenWithDialog));
        toolStrip.Items.Add(Button("Save", "Save (Ctrl+S)", () => WithCurrent(t => Save(t))));
        toolStrip.Items.Add(new ToolStripSeparator());

        boldButton = Button("B", "Bold (Ctrl+B)", () => ApplyStyle(FontStyle.Bold, "**", "**"));
        boldButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        italicButton = Button("I", "Italic (Ctrl+I)", () => ApplyStyle(FontStyle.Italic, "*", "*"));
        italicButton.Font = new Font("Segoe UI", 9, FontStyle.Italic);
        underlineButton = Button("U", "Underline (Ctrl+U)", () => ApplyStyle(FontStyle.Underline, "<u>", "</u>"));
        underlineButton.Font = new Font("Segoe UI", 9, FontStyle.Underline);
        toolStrip.Items.AddRange([boldButton, italicButton, underlineButton, new ToolStripSeparator()]);

        fontFamilyCombo = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
            ToolTipText = "Font",
            AccessibleName = "Font",
        };
        fontFamilyCombo.ComboBox.Width = LogicalToDeviceUnits(150);
        fontFamilyCombo.Items.AddRange(InstalledFonts.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray<object>());
        fontFamilyCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!syncingControls && fontFamilyCombo.SelectedItem is string family)
            {
                ApplyFontToCurrent(family, null);
            }
        };

        fontSizeCombo = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = 50,
            ToolTipText = "Font size (type a value from 6 to 72 and press Enter)",
            AccessibleName = "Font size",
        };
        fontSizeCombo.ComboBox.Width = LogicalToDeviceUnits(55);
        fontSizeCombo.Items.AddRange(["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72"]);
        fontSizeCombo.SelectedIndexChanged += (_, _) => CommitFontSize();
        fontSizeCombo.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitFontSize();
                Current?.Editor.Focus();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        fontSizeCombo.Leave += (_, _) => CommitFontSize();
        toolStrip.Items.AddRange([fontFamilyCombo, fontSizeCombo, new ToolStripSeparator()]);

        previewButton = Button("Preview", "Preview Markdown (Ctrl+P)", () => SetPreviewMode(PreviewMode.Full));
        splitButton = Button("Split", "Side-by-side preview (Ctrl+Shift+V)", () => SetPreviewMode(PreviewMode.Split));
        toolStrip.Items.AddRange([previewButton, splitButton, new ToolStripSeparator()]);

        toolStrip.Items.Add(new ToolStripLabel("Theme:"));
        themeCombo = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            ToolTipText = "Theme",
            AccessibleName = "Theme",
        };
        themeCombo.ComboBox.Width = LogicalToDeviceUnits(110);
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!syncingControls && themeCombo.SelectedItem is string name)
            {
                SelectTheme(name);
            }
        };
        toolStrip.Items.Add(themeCombo);
    }

    private void ShowTabMenu(int index)
    {
        if (index < 0 || index >= tabControl.TabCount || tabControl.TabPages[index] is not DocumentTab tab)
        {
            return;
        }

        tabMenu?.Dispose();
        tabMenu = new ContextMenuStrip { Renderer = menuStrip.Renderer, BackColor = palette.Chrome, ForeColor = palette.ChromeText };
        var right = Documents.Skip(index + 1).ToList();
        var others = Documents.Where(d => d != tab).ToList();
        tabMenu.Items.AddRange(
        [
            Item("&Close", () => CloseTab(tab), Keys.None, "Ctrl+W"),
            Item("Close &Others", () => CloseTabs(others)),
            Item("Close Tabs to the &Right", () => CloseTabs(right)),
            Item("Close &All", () => CloseTabs(Documents)),
            new ToolStripSeparator(),
            Item("Copy Full &Path", () => Clipboard.SetText(tab.FilePath!)),
            Item("Open Containing &Folder", () => RevealInExplorer(tab.FilePath!)),
            Item("Re&load from Disk", () => ReloadWithPrompt(tab)),
        ]);
        foreach (ToolStripItem item in tabMenu.Items)
        {
            item.ForeColor = palette.ChromeText;
            if (item.Text is "Copy Full &Path" or "Open Containing &Folder" or "Re&load from Disk")
            {
                item.Enabled = tab.FilePath is not null;
            }
        }

        tabMenu.Items[1].Enabled = others.Count > 0;
        tabMenu.Items[2].Enabled = right.Count > 0;

        var rect = tabControl.GetTabRect(index);
        tabMenu.Show(tabControl, new Point(rect.Left, rect.Bottom));
    }

    private void BuildRecentMenu()
    {
        recentMenu.DropDownItems.Clear();
        var n = 0;
        foreach (var path in settings.RecentFiles.ToList())
        {
            n++;
            var p = path;
            var label = (n < 10 ? $"&{n} " : "   ") + path.Replace("&", "&&", StringComparison.Ordinal);
            recentMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) =>
            {
                if (!File.Exists(p))
                {
                    settings.RecentFiles.Remove(p);
                    SaveSettingsQuietly();
                    ShowMessage($"{Path.GetFileName(p)} no longer exists, so it was removed from Recent Files.");
                    return;
                }

                OpenFiles([p]);
            })
            { ForeColor = palette.ChromeText, BackColor = palette.Chrome });
        }

        if (n == 0)
        {
            recentMenu.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
            return;
        }

        recentMenu.DropDownItems.Add(new ToolStripSeparator());
        recentMenu.DropDownItems.Add(new ToolStripMenuItem("&Clear Recent Files", null, (_, _) =>
        {
            settings.RecentFiles.Clear();
            SaveSettingsQuietly();
        })
        { ForeColor = palette.ChromeText, BackColor = palette.Chrome });
    }

    private static void SyncCheckedTags(ToolStripMenuItem menu, object? value)
    {
        foreach (ToolStripItem item in menu.DropDownItems)
        {
            if (item is ToolStripMenuItem mi)
            {
                mi.Checked = value is not null && Equals(mi.Tag, value);
            }
        }
    }

    private void WithCurrent(Action<DocumentTab> action)
    {
        if (Current is { } tab)
        {
            action(tab);
        }
    }

    private static void RevealInExplorer(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // Path comes from an open document (already normalized); quotes can't appear in Windows paths.
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = false });
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetFullPath(folder)}\"") { UseShellExecute = false });
    }

    private void ShowShortcuts()
    {
        Dialogs.ShowInfo(this, "Keyboard shortcuts", string.Join('\n',
        [
            "Ctrl+N  New            Ctrl+O  Open           Ctrl+S  Save",
            "Ctrl+Shift+S  Save As  Ctrl+W / Ctrl+F4  Close tab",
            "Ctrl+Shift+T  Reopen closed tab",
            "Ctrl+Tab / Ctrl+Shift+Tab  Next / previous tab",
            "Ctrl+F  Find   F3 / Shift+F3  Next / previous   Ctrl+H  Replace",
            "Ctrl+G  Go to line   F5  Insert date/time",
            "Ctrl+D  Duplicate line   Alt+Up / Alt+Down  Move line",
            "Ctrl+B / I / U  Bold, italic, underline",
            "Ctrl+1/2/3  Heading   Ctrl+K  Link   Ctrl+`  Inline code (Markdown)",
            "Ctrl+Shift+8 / 7 / 9  Bullets, numbers, tasks (Markdown)",
            "Ctrl+P  Preview   Ctrl+Shift+V  Split preview",
            "Ctrl++ / Ctrl+- / Ctrl+0  Zoom   Alt+Z  Word wrap",
            "Ctrl+Shift+R  Read aloud   Win+H  Voice typing",
            "Shift+F10 on a tab  Tab menu",
        ]));
    }

    private void ShowAbout()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?";
        var webView = PreviewEnvironment.RuntimeVersion() ?? "not installed";
        Dialogs.ShowInfo(this, $"NicPad {version}",
            $"A lightweight tabbed text and Markdown editor.\n\n" +
            $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\n" +
            $"WebView2: {webView}\n" +
            $"Settings: {paths.Root}\n\n" +
            "https://github.com/nbwillcox/NicPad");
    }
}
