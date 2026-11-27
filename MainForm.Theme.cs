using System.Diagnostics;
using NicPad.Core;
using NicPad.UI;

namespace NicPad;

public partial class MainForm
{
    private List<ThemePalette> customThemes = [];
    private ThemePalette palette = Themes.Light;

    private IEnumerable<ThemePalette> AllThemes => Themes.BuiltIn.Concat(customThemes);

    // ---------- Themes ----------

    private void LoadCustomThemes()
    {
        var errors = new List<string>();
        try
        {
            customThemes = Themes.LoadCustom(paths.ThemesDir, errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(ex.Message);
            customThemes = [];
        }

        if (errors.Count > 0)
        {
            ShowMessage($"Skipped {errors.Count} theme file(s): {errors[0]}");
        }

        syncingControls = true;
        try
        {
            themeCombo.Items.Clear();
            themeCombo.Items.AddRange(AllThemes.Select(t => (object)t.Name).ToArray());

            themeMenu.DropDownItems.Clear();
            foreach (var theme in AllThemes)
            {
                var name = theme.Name;
                themeMenu.DropDownItems.Add(new ToolStripMenuItem(name.Replace("&", "&&", StringComparison.Ordinal), null, (_, _) => SelectTheme(name)) { Tag = name });
            }

            themeMenu.DropDownItems.Add(new ToolStripSeparator());
            themeMenu.DropDownItems.Add(new ToolStripMenuItem("&Open Themes Folder", null, (_, _) => OpenThemesFolder()));
            themeMenu.DropDownItems.Add(new ToolStripMenuItem("&Reload Themes", null, (_, _) =>
            {
                LoadCustomThemes();
                ApplyTheme();
                ShowMessage($"Loaded {customThemes.Count} custom theme(s).");
            }));
        }
        finally
        {
            syncingControls = false;
        }
    }

    private void OpenThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(paths.ThemesDir);
            var sample = Path.Combine(paths.ThemesDir, "sample-theme.json.example");
            if (!File.Exists(sample))
            {
                File.WriteAllText(sample, Themes.SampleTheme);
            }

            OpenFolder(paths.ThemesDir);
            ShowMessage("Copy sample-theme.json.example to a new .json file, edit it, then use View > Theme > Reload Themes.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowError(this, "Couldn't open the themes folder", ex.Message);
        }
    }

    private void SelectTheme(string name)
    {
        settings.Theme = name;
        ApplyTheme();
        SaveSettingsQuietly();
    }

    private void ApplyTheme()
    {
        var requested = AllThemes.FirstOrDefault(t => string.Equals(t.Name, settings.Theme, StringComparison.OrdinalIgnoreCase)) ?? Themes.System;
        if (requested.Name == "System")
        {
            requested = Themes.System; // re-read SystemColors
        }

        // Windows High Contrast wins over any custom colors.
        palette = SystemInformation.HighContrast ? Themes.System : requested;
        var p = palette;

        var renderer = new ThemedToolStripRenderer
        {
            HoverBackColor = p.HoverBack,
            HoverForeColor = p.HoverText,
            PressedBackColor = p.HoverBack,
        };
        toolStrip.Renderer = renderer;
        menuStrip.Renderer = renderer;
        statusStrip.Renderer = renderer;

        BackColor = p.Window;
        menuStrip.BackColor = p.Chrome;
        menuStrip.ForeColor = p.ChromeText;
        foreach (ToolStripItem item in menuStrip.Items)
        {
            ApplyThemeToItem(item, p);
        }

        toolStrip.BackColor = p.Chrome;
        toolStrip.ForeColor = p.ChromeText;
        foreach (ToolStripItem item in toolStrip.Items)
        {
            item.ForeColor = p.ChromeText;
            item.BackColor = p.Chrome;
            if (item is ToolStripComboBox combo)
            {
                combo.BackColor = p.Editor;
                combo.ForeColor = p.EditorText;
            }
        }

        separator.BackColor = p.Separator;
        tabPanel.BackColor = p.Chrome;
        tabControl.BackColor = p.Chrome;
        tabControl.ForeColor = p.ChromeText;
        tabControl.Palette = p;
        foreach (var tab in Documents)
        {
            tab.ApplyPalette(p);
        }

        statusStrip.BackColor = p.Chrome;
        statusStrip.ForeColor = p.ChromeText;
        foreach (ToolStripItem item in statusStrip.Items)
        {
            item.ForeColor = p.ChromeText;
        }

        if (IsHandleCreated)
        {
            NativeMethods.SetDarkTitleBar(Handle, p.IsDark);
        }

        syncingControls = true;
        try
        {
            themeCombo.SelectedItem = themeCombo.Items.Contains(requested.Name) ? requested.Name : null;
            foreach (ToolStripItem item in themeMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem mi && mi.Tag is string name)
                {
                    mi.Checked = string.Equals(name, requested.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        finally
        {
            syncingControls = false;
        }

        if (SystemInformation.HighContrast && requested.Name != "System")
        {
            ShowMessage("High Contrast is on in Windows, so NicPad is using system colors.");
        }

        tabControl.Invalidate();
        RenderPreview(Current);
    }

    private static void ApplyThemeToItem(ToolStripItem item, ThemePalette p)
    {
        item.ForeColor = p.ChromeText;
        item.BackColor = p.Chrome;
        if (item is ToolStripMenuItem menuItem)
        {
            menuItem.DropDown.BackColor = p.Chrome;
            foreach (ToolStripItem child in menuItem.DropDownItems)
            {
                ApplyThemeToItem(child, p);
            }
        }
    }

    // ---------- Preview ----------

    private void SetPreviewMode(PreviewMode mode)
    {
        if (Current is not { } tab)
        {
            return;
        }

        if (tab.Kind != DocumentKind.Markdown)
        {
            ShowMessage("Preview is for Markdown (.md) files. Use File > Save As to save this tab as Markdown.");
            UpdatePreviewUi();
            return;
        }

        tab.PreviewMode = tab.PreviewMode == mode ? PreviewMode.Off : mode;
        UpdatePreviewUi();
        if (tab.PreviewMode == PreviewMode.Off)
        {
            tab.Editor.Focus();
        }
        else
        {
            RenderPreview(tab);
        }
    }

    private void UpdatePreviewUi()
    {
        var tab = Current;
        var isMarkdown = tab?.Kind == DocumentKind.Markdown;
        previewButton.Enabled = splitButton.Enabled = previewMenuItem.Enabled = splitMenuItem.Enabled = isMarkdown;
        previewButton.Checked = previewMenuItem.Checked = isMarkdown && tab!.PreviewMode == PreviewMode.Full;
        splitButton.Checked = splitMenuItem.Checked = isMarkdown && tab!.PreviewMode == PreviewMode.Split;
    }

    private void RenderPreview(DocumentTab? tab)
    {
        if (tab is null || tab.PreviewMode == PreviewMode.Off || IsDisposed)
        {
            return;
        }

        var host = tab.Preview;
        if (host is null)
        {
            host = tab.EnsurePreview(paths.WebViewDataDir, palette);
            host.LinkClicked += OnPreviewLinkClicked;
        }

        host.AllowRemoteImages = settings.AllowRemoteImages;
        host.SetBackground(palette.Preview.Background, palette.Preview.Text);
        var html = renderer.RenderDocument(tab.TextSnapshot, palette.Preview, palette.IsDark, settings.AllowRemoteImages, tab.DisplayName);
        host.ShowHtml(html);
    }

    private void OnPreviewLinkClicked(object? sender, string url)
    {
        if (!UrlPolicy.CanOpenExternally(url) || !Dialogs.ConfirmOpenLink(this, url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Dialogs.ShowError(this, "Couldn't open the link", ex.Message);
        }
    }
}
