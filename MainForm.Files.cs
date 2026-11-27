using System.Drawing.Printing;
using System.Security;
using System.Text;
using NicPad.Core;
using NicPad.UI;

namespace NicPad;

public partial class MainForm
{
    // ---------- Open ----------

    public void OpenFile(string filePath) => OpenFiles([filePath]);

    public void OpenFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        DocumentTab? last = null;
        foreach (var file in files.Take(SessionData.MaxTabs))
        {
            last = OpenFileCore(file, quiet: false, problems: null) ?? last;
        }

        if (last is not null)
        {
            tabControl.SelectedTab = last;
            UpdateActiveTabUi();
            SaveSettingsQuietly();
        }
    }

    /// <summary>Files handed over by a second launch (already validated as absolute paths).</summary>
    internal void OpenFromAnotherInstance(IReadOnlyList<string> files)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        OpenFiles(files.Where(File.Exists));
        Activate();
    }

    private void OpenWithDialog()
    {
        if (Dialogs.AskOpenPath(this) is { } selection)
        {
            OpenFiles(selection.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    private void ReopenClosedTab()
    {
        while (closedFiles.Count > 0)
        {
            var path = closedFiles.Pop();
            if (File.Exists(path) && FindOpenTab(path) is null)
            {
                OpenFiles([path]);
                return;
            }
        }

        ShowMessage("No recently closed files to reopen.");
    }

    private DocumentTab? FindOpenTab(string fullPath) =>
        Documents.FirstOrDefault(d => d.FilePath is not null && string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Opens one file. <paramref name="quiet"/> (session restore) never shows a dialog; problems go to <paramref name="problems"/>.
    /// </summary>
    internal DocumentTab? OpenFileCore(string filePath, bool quiet, List<string>? problems)
    {
        void Report(string heading, string detail)
        {
            if (quiet || problems is not null)
            {
                problems?.Add($"{heading}. {detail}");
                ShowMessage(heading);
            }
            else
            {
                Dialogs.ShowError(this, heading, detail);
            }
        }

        string full;
        try
        {
            full = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            Report("That isn't a valid file path", filePath);
            return null;
        }

        var name = Path.GetFileName(full);
        if (FindOpenTab(full) is { } existing)
        {
            tabControl.SelectedTab = existing;
            return existing;
        }

        if (!File.Exists(full))
        {
            Report($"Couldn't find {name}", $"{full} doesn't exist. It may have been moved, renamed or deleted.");
            return null;
        }

        var size = new FileInfo(full).Length;
        if (size > MaxOpenBytes)
        {
            Report($"{name} is too large to open", $"NicPad can open files up to {MaxOpenBytes / (1024 * 1024)} MB. This one is {size / (1024 * 1024)} MB.");
            return null;
        }

        if (size > LargeFileWarningBytes && !quiet
            && !Dialogs.Confirm(this, $"{name} is {size / (1024.0 * 1024.0):F1} MB",
                "Very large files can make NicPad slow to respond while they load.", "&Open Anyway"))
        {
            return null;
        }

        var isRtf = string.Equals(Path.GetExtension(full), ".rtf", StringComparison.OrdinalIgnoreCase);
        LoadedText? loaded = null;
        string? rtf = null;
        var strippedObjects = 0;
        try
        {
            if (isRtf)
            {
                rtf = RtfSafety.StripEmbeddedObjects(Encoding.Latin1.GetString(File.ReadAllBytes(full)), out strippedObjects);
            }
            else
            {
                loaded = TextFileIO.ReadFile(full, MaxOpenBytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"Couldn't open {name}", ex is UnauthorizedAccessException
                ? "You don't have permission to read it."
                : ex.Message + " If another program is using it, close that program and try again.");
            return null;
        }

        if (loaded is { LooksBinary: true } && !quiet
            && !Dialogs.Confirm(this, $"{name} looks like a binary file",
                "It contains characters that aren't text. Editing and saving it in a text editor can damage it.", "&Open Anyway"))
        {
            return null;
        }

        var reused = ReusableBlankTab();
        var tab = reused ?? CreateTab(NextUntitledName());
        tab.SetFilePath(full);
        if (!LoadIntoTab(tab, loaded, rtf, name, quiet))
        {
            if (reused is not null)
            {
                tab.SetFilePath(null);
            }
            else
            {
                tabControl.TabPages.Remove(tab);
                tab.Dispose();
            }

            return null;
        }

        RecordDiskState(tab);
        if (!restoring)
        {
            settings.AddRecentFile(full);
        }
        if (strippedObjects > 0)
        {
            ShowMessage($"Removed {strippedObjects} embedded object(s) from {name} for safety. Text and formatting are kept.");
        }

        if (!restoring)
        {
            tabControl.SelectedTab = tab;
        }

        return tab;
    }

    private bool LoadIntoTab(DocumentTab tab, LoadedText? loaded, string? rtf, string name, bool quiet)
    {
        try
        {
            tab.WithoutChangeTracking(() =>
            {
                if (rtf is not null)
                {
                    using var ms = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
                    tab.Editor.LoadFile(ms, RichTextBoxStreamType.RichText);
                    tab.Encoding = TextEncodingKind.Utf8;
                    tab.LineEnding = LineEnding.CrLf;
                }
                else if (loaded is not null)
                {
                    tab.Editor.Text = loaded.Text;
                    tab.Encoding = loaded.Encoding;
                    tab.LineEnding = loaded.LineEnding;
                }

                tab.Editor.Select(0, 0);
                tab.Editor.ClearUndo();
            });
        }
        catch (ArgumentException ex)
        {
            if (!quiet)
            {
                Dialogs.ShowError(this, $"Couldn't open {name}", "It isn't a valid RTF document. " + ex.Message);
            }

            return false;
        }

        tab.HasRichFormatting = false;
        tab.MarkSaved();
        return true;
    }

    private void ReloadWithPrompt(DocumentTab tab)
    {
        if (tab.FilePath is null)
        {
            return;
        }

        if (tab.IsModified && !Dialogs.Confirm(this, $"Reload {tab.DisplayName}?", "Your unsaved changes will be replaced by the version on disk.", "&Reload"))
        {
            return;
        }

        ReloadFromDisk(tab);
    }

    internal void ReloadFromDisk(DocumentTab tab)
    {
        if (tab.FilePath is not { } path)
        {
            return;
        }

        try
        {
            var caret = tab.Editor.SelectionStart;
            if (tab.Kind == DocumentKind.Rtf)
            {
                var rtf = RtfSafety.StripEmbeddedObjects(Encoding.Latin1.GetString(File.ReadAllBytes(path)), out _);
                LoadIntoTab(tab, null, rtf, tab.DisplayName, quiet: false);
            }
            else
            {
                LoadIntoTab(tab, TextFileIO.ReadFile(path, MaxOpenBytes), null, tab.DisplayName, quiet: false);
            }

            tab.Editor.Select(Math.Min(caret, tab.Editor.TextLength), 0);
            RecordDiskState(tab);
            backups.Delete(tab.BackupId);
            UpdateActiveTabUi();
            ShowMessage($"Reloaded {tab.DisplayName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowError(this, $"Couldn't reload {tab.DisplayName}", ex.Message);
        }
    }

    // ---------- Save ----------

    internal bool Save(DocumentTab tab) => tab.FilePath is null ? SaveAs(tab) : SaveTo(tab, tab.FilePath);

    internal bool SaveAs(DocumentTab tab)
    {
        var extension = tab.FilePath is not null ? tab.Extension : (tab.HasRichFormatting ? ".rtf" : ".txt");
        var suggested = tab.FilePath is not null ? Path.GetFileName(tab.FilePath) : tab.UntitledName + extension;
        var path = Dialogs.AskSavePath(this, tab.FilePath, suggested, extension);
        return path is not null && SaveTo(tab, path);
    }

    private void SaveAll()
    {
        foreach (var tab in Documents.Where(d => d.IsModified))
        {
            if (!Save(tab))
            {
                return;
            }
        }
    }

    internal bool SaveTo(DocumentTab tab, string path)
    {
        path = Path.GetFullPath(path);
        var toRtf = string.Equals(Path.GetExtension(path), ".rtf", StringComparison.OrdinalIgnoreCase);

        if (!toRtf && (tab.HasRichFormatting || tab.Kind == DocumentKind.Rtf))
        {
            switch (Dialogs.AskRichFormattingLoss(this, tab.DisplayName))
            {
                case RichSaveChoice.SaveAsRtf:
                    var rtfPath = Dialogs.AskSavePath(this, path, Path.ChangeExtension(Path.GetFileName(path), ".rtf"), ".rtf");
                    if (rtfPath is null)
                    {
                        return false;
                    }

                    path = Path.GetFullPath(rtfPath);
                    toRtf = string.Equals(Path.GetExtension(path), ".rtf", StringComparison.OrdinalIgnoreCase);
                    break;
                case RichSaveChoice.SavePlain:
                    tab.HasRichFormatting = false;
                    break;
                default:
                    return false;
            }
        }

        byte[] bytes;
        if (toRtf)
        {
            using var ms = new MemoryStream();
            tab.Editor.SaveFile(ms, RichTextBoxStreamType.RichText);
            bytes = ms.ToArray();
        }
        else
        {
            var text = tab.Editor.Text;
            if (!TextFileIO.CanEncode(text, tab.Encoding))
            {
                if (!Dialogs.Confirm(this, $"Some characters can't be saved as {TextFileIO.DisplayName(tab.Encoding)}",
                        "They would be replaced with question marks. Save as UTF-8 instead to keep them?", "Save as &UTF-8"))
                {
                    return false;
                }

                tab.Encoding = TextEncodingKind.Utf8;
            }

            bytes = TextFileIO.Encode(text, tab.Encoding, tab.LineEnding);
        }

        try
        {
            TextFileIO.WriteAtomic(path, bytes);
        }
        catch (UnauthorizedAccessException)
        {
            Dialogs.ShowError(this, $"Couldn't save {Path.GetFileName(path)}",
                "The file is read-only or you don't have permission to change it. Use File > Save As to save a copy somewhere else.");
            return false;
        }
        catch (IOException ex)
        {
            Dialogs.ShowError(this, $"Couldn't save {Path.GetFileName(path)}",
                ex.Message + " If another program has the file open, close it and try again, or use File > Save As.");
            return false;
        }

        if (!string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            tab.SetFilePath(path);
            settings.AddRecentFile(path);
        }

        if (toRtf)
        {
            tab.HasRichFormatting = false;
        }

        RecordDiskState(tab);
        tab.MarkSaved();
        backups.Delete(tab.BackupId);
        tab.BackupVersion = -1;
        UpdateActiveTabUi();
        ShowMessage($"Saved {tab.DisplayName}.");
        return true;
    }

    private void ExportHtml(DocumentTab tab)
    {
        if (tab.Kind != DocumentKind.Markdown)
        {
            ShowMessage("Export as HTML works for Markdown (.md) files.");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "HTML page (*.html)|*.html",
            FileName = Path.ChangeExtension(tab.DisplayName, ".html"),
            OverwritePrompt = true,
        };
        if (tab.FilePath is not null)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(tab.FilePath);
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var html = renderer.RenderDocument(tab.Editor.Text, Themes.Light.Preview, isDark: false, settings.AllowRemoteImages, tab.DisplayName);
            TextFileIO.WriteAtomic(dialog.FileName, new UTF8Encoding(false).GetBytes(html));
            ShowMessage($"Exported {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowError(this, "Couldn't export the HTML file", ex.Message);
        }
    }

    private void Print(DocumentTab tab, bool preview)
    {
        var text = TextFileIO.NormalizeToLf(tab.Editor.Text);
        var offset = 0;
        using var font = new Font(tab.FontFamilyName, tab.FontSize);
        using var doc = new PrintDocument { DocumentName = tab.DisplayName };
        doc.BeginPrint += (_, _) => offset = 0;
        doc.PrintPage += (_, e) =>
        {
            var area = e.MarginBounds;
            using var format = new StringFormat(StringFormatFlags.LineLimit);
            var chunk = text.Substring(offset, Math.Min(20000, text.Length - offset));
            var g = e.Graphics!;
            g.MeasureString(chunk, font, area.Size, format, out int chars, out int _);
            chars = Math.Max(1, chars);
            g.DrawString(chunk[..Math.Min(chars, chunk.Length)], font, Brushes.Black, area, format);
            offset += chars;
            e.HasMorePages = offset < text.Length;
        };

        if (preview)
        {
            using var dialog = new PrintPreviewDialog { Document = doc, Width = 900, Height = 700, ShowIcon = false };
            dialog.ShowDialog(this);
        }
        else
        {
            using var dialog = new PrintDialog { Document = doc, UseEXDialog = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                doc.Print();
            }
        }
    }

    // ---------- External changes ----------

    private static void RecordDiskState(DocumentTab tab)
    {
        if (tab.FilePath is { } path && File.Exists(path))
        {
            var info = new FileInfo(path);
            tab.DiskWriteTimeUtc = info.LastWriteTimeUtc;
            tab.DiskLength = info.Length;
        }
    }

    internal void CheckDiskChanges()
    {
        if (checkingDisk || restoring || IsDisposed)
        {
            return;
        }

        checkingDisk = true;
        try
        {
            foreach (var tab in Documents)
            {
                if (tab.FilePath is not { } path || tab.DiskLength < 0)
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    info.Refresh();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!info.Exists)
                {
                    tab.DiskLength = -1;
                    tab.MarkModified();
                    ShowMessage($"{tab.DisplayName} was deleted or moved. Save it to keep your copy.");
                    continue;
                }

                if (info.LastWriteTimeUtc == tab.DiskWriteTimeUtc && info.Length == tab.DiskLength)
                {
                    continue;
                }

                tab.DiskWriteTimeUtc = info.LastWriteTimeUtc;
                tab.DiskLength = info.Length;
                tabControl.SelectedTab = tab;
                if (Dialogs.ConfirmReload(this, tab.DisplayName, tab.IsModified))
                {
                    ReloadFromDisk(tab);
                }
            }
        }
        finally
        {
            checkingDisk = false;
        }
    }

    // ---------- Session, backups, crash recovery ----------

    private bool RestoreSession(SessionData? session, List<string> problems)
    {
        if (session is null || session.OpenFiles.Count == 0)
        {
            return false;
        }

        var restoredUnsaved = 0;
        restoring = true;
        try
        {
            foreach (var entry in session.OpenFiles)
            {
                string? backupText = null;
                if (entry.BackupId is { } id)
                {
                    try
                    {
                        backupText = backups.Read(id);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        problems.Add($"Couldn't read the backup for {entry.Title ?? entry.FilePath}: {ex.Message}");
                    }
                }

                DocumentTab? tab = null;
                if (entry.FilePath is { } path && File.Exists(path))
                {
                    tab = OpenFileCore(path, quiet: true, problems);
                    if (tab is null && backupText is null)
                    {
                        continue;
                    }
                }
                else if (entry.FilePath is not null && backupText is null)
                {
                    problems.Add($"{Path.GetFileName(entry.FilePath)} no longer exists");
                    continue;
                }

                if (tab is null)
                {
                    if (backupText is null)
                    {
                        continue;
                    }

                    var title = entry.Title ?? NextUntitledName();
                    NoteUntitledName(title);
                    tab = CreateTab(title);
                    if (entry.FilePath is not null)
                    {
                        tab.SetFilePath(entry.FilePath);
                        tab.DiskLength = -1;
                    }
                }

                tab.SetFont(IsInstalledFont(entry.FontFamily) ? entry.FontFamily : settings.DefaultFontFamily, entry.FontSize);

                if (backupText is not null)
                {
                    tab.BackupId = entry.BackupId!;
                    tab.WithoutChangeTracking(() =>
                    {
                        if (entry.BackupIsRtf)
                        {
                            tab.Editor.Rtf = RtfSafety.StripEmbeddedObjects(backupText, out _);
                        }
                        else
                        {
                            tab.Editor.Text = backupText;
                        }
                    });
                    tab.Encoding = entry.Encoding;
                    tab.LineEnding = entry.LineEnding;
                    tab.BackupVersion = tab.ContentVersion;
                    tab.MarkModified();
                    restoredUnsaved++;
                }

                if (entry.RightToLeft)
                {
                    tab.SetRightToLeft(true);
                }

                if (Math.Abs(entry.Zoom - 1f) > 0.01f)
                {
                    tab.Editor.ZoomFactor = entry.Zoom;
                }

                tab.Editor.Select(Math.Min(entry.Caret, tab.Editor.TextLength), 0);
                tab.PreviewMode = entry.Preview;
            }
        }
        finally
        {
            restoring = false;
        }

        if (tabControl.TabCount == 0)
        {
            return false;
        }

        tabControl.SelectedIndex = Math.Clamp(session.SelectedTabIndex, 0, tabControl.TabCount - 1);
        UpdateActiveTabUi();
        if (restoredUnsaved > 0)
        {
            problems.Insert(0, restoredUnsaved == 1
                ? "Restored unsaved changes in 1 tab."
                : $"Restored unsaved changes in {restoredUnsaved} tabs.");
        }

        return true;
    }

    /// <summary>Writes backups of changed, unsaved tabs plus a session that points at them.</summary>
    internal void WriteBackups()
    {
        if (!settings.AutoBackup || IsDisposed)
        {
            return;
        }

        try
        {
            foreach (var tab in Documents.Where(d => d.IsModified && d.BackupVersion != d.ContentVersion))
            {
                WriteBackup(tab);
            }

            SaveSession(keepUnsaved: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Couldn't write the crash-recovery backup: " + ex.Message);
        }
    }

    private void WriteBackup(DocumentTab tab)
    {
        backups.Write(tab.BackupId, (tab.Kind == DocumentKind.Rtf || tab.HasRichFormatting ? tab.Editor.Rtf : tab.Editor.Text) ?? "");
        tab.BackupVersion = tab.ContentVersion;
    }

    internal void SaveSession(bool keepUnsaved)
    {
        var session = new SessionData { SelectedTabIndex = Math.Max(0, tabControl.SelectedIndex) };
        foreach (var tab in Documents)
        {
            var hasBackup = keepUnsaved && tab.IsModified;
            if (hasBackup && tab.BackupVersion != tab.ContentVersion)
            {
                WriteBackup(tab);
            }

            if (!hasBackup && (tab.FilePath is null || !File.Exists(tab.FilePath)))
            {
                continue;
            }

            session.OpenFiles.Add(new SessionTab
            {
                FilePath = tab.FilePath,
                Title = tab.FilePath is null ? tab.UntitledName : null,
                FontFamily = tab.FontFamilyName,
                FontSize = tab.FontSize,
                BackupId = hasBackup ? tab.BackupId : null,
                BackupIsRtf = hasBackup && (tab.Kind == DocumentKind.Rtf || tab.HasRichFormatting),
                Encoding = tab.Encoding,
                LineEnding = tab.LineEnding,
                Preview = tab.PreviewMode,
                RightToLeft = tab.IsRightToLeft,
                Zoom = tab.Editor.ZoomFactor,
                Caret = tab.Editor.SelectionStart,
            });
        }

        try
        {
            session.Save(paths.SessionFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Couldn't save the session: " + ex.Message);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var keepUnsaved = settings.HotExit || e.CloseReason == CloseReason.WindowsShutDown;
        if (!keepUnsaved)
        {
            foreach (var tab in Documents.Where(d => d.IsModified))
            {
                if (!ConfirmSaveOrDiscard(tab))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        backupTimer.Stop();
        speech.Stop();
        SaveSession(keepUnsaved);
        if (!keepUnsaved)
        {
            foreach (var tab in Documents)
            {
                backups.Delete(tab.BackupId);
            }
        }

        CaptureWindowPlacement();
        SaveSettingsQuietly();
        base.OnFormClosing(e);
    }

    /// <summary>Last-chance handler for unexpected UI-thread exceptions: back up everything, log, tell the user.</summary>
    internal void HandleCrash(Exception ex)
    {
        CrashLog.Write(paths.CrashLogFile, ex);
        var backedUp = false;
        try
        {
            foreach (var tab in Documents.Where(d => d.IsModified))
            {
                WriteBackup(tab);
            }

            SaveSession(keepUnsaved: true);
            backedUp = true;
        }
#pragma warning disable CA1031 // Emergency path: a second failure must not hide the first.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        Dialogs.ShowError(this, "NicPad hit an unexpected problem",
            (backedUp
                ? "Your unsaved work was backed up and will be restored the next time NicPad starts. "
                : "NicPad couldn't back up your work. Save your files now if you can. ")
            + $"Details were written to {paths.CrashLogFile}.\n\n{ex.GetType().Name}: {ex.Message}");
    }
}
