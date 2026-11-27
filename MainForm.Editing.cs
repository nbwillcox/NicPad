using System.Globalization;
using NicPad.Core;
using NicPad.UI;

namespace NicPad;

public partial class MainForm
{
    private FindReplaceDialog? findDialog;
    private FindOptions? lastFind;

    // ---------- Formatting ----------

    internal void ApplyStyle(FontStyle style, string mdPrefix, string mdSuffix)
    {
        if (Current is not { } tab)
        {
            return;
        }

        var editor = tab.Editor;
        if (tab.Kind == DocumentKind.Markdown)
        {
            editor.ApplyEdit(TextOps.Wrap(tab.TextSnapshot, editor.SelectionStart, editor.SelectionLength, mdPrefix, mdSuffix, "text"));
        }
        else
        {
            var current = editor.SelectionFont ?? editor.Font;
            using var font = new Font(current, current.Style ^ style);
            editor.SelectionFont = font;
            tab.HasRichFormatting = true;
            if (tab.Kind == DocumentKind.Plain && tab.FilePath is not null)
            {
                ShowMessage("Formatting can't be stored in plain text. Use File > Save As and pick Rich Text (.rtf) to keep it.");
            }
        }

        editor.Focus();
        UpdateFormattingButtons();
    }

    private void UpdateFormattingButtons()
    {
        if (Current is not { } tab)
        {
            return;
        }

        var rich = tab.Kind != DocumentKind.Markdown;
        var font = rich ? tab.Editor.SelectionFont : null;
        boldButton.Checked = font?.Bold ?? false;
        italicButton.Checked = font?.Italic ?? false;
        underlineButton.Checked = font?.Underline ?? false;
    }

    private void UpdateFontControls()
    {
        if (Current is not { } tab)
        {
            return;
        }

        syncingControls = true;
        try
        {
            fontFamilyCombo.SelectedItem = fontFamilyCombo.Items.Contains(tab.FontFamilyName) ? tab.FontFamilyName : null;
            fontSizeCombo.Text = tab.FontSize.ToString("0.#", CultureInfo.CurrentCulture);
            wordWrapMenuItem.Checked = tab.Editor.WordWrap;
        }
        finally
        {
            syncingControls = false;
        }
    }

    private void CommitFontSize()
    {
        if (syncingControls || Current is not { } tab)
        {
            return;
        }

        if (float.TryParse(fontSizeCombo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size) && float.IsFinite(size))
        {
            ApplyFontToCurrent(null, AppSettings.ClampFontSize(size));
        }

        UpdateFontControls();
    }

    /// <summary>Plain/Markdown: whole document. RTF: just the selection, like a word processor.</summary>
    private void ApplyFontToCurrent(string? family, float? size)
    {
        if (Current is not { } tab)
        {
            return;
        }

        if (tab.Kind == DocumentKind.Rtf || (tab.HasRichFormatting && tab.Editor.SelectionLength > 0))
        {
            var current = tab.Editor.SelectionFont ?? tab.Editor.Font;
            using var font = new Font(family ?? current.FontFamily.Name, size ?? current.Size, current.Style);
            tab.Editor.SelectionFont = font;
            return;
        }

        tab.SetFont(family ?? tab.FontFamilyName, size ?? tab.FontSize);
        ScheduleStatus();
    }

    private void ShowFontDialog()
    {
        if (Current is not { } tab)
        {
            return;
        }

        using var current = new Font(tab.FontFamilyName, tab.FontSize);
        using var dialog = new FontDialog { Font = current, ShowEffects = false };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplyFontToCurrent(dialog.Font.FontFamily.Name, dialog.Font.Size);
            UpdateFontControls();
        }
    }

    private void SetDefaultFont()
    {
        if (Current is not { } tab)
        {
            return;
        }

        settings.DefaultFontFamily = tab.FontFamilyName;
        settings.DefaultFontSize = tab.FontSize;
        SaveSettingsQuietly();
        ShowMessage($"New tabs will use {tab.FontFamilyName} {tab.FontSize:0.#} pt.");
    }

    private void SetWordWrap(bool on)
    {
        if (syncingControls)
        {
            return;
        }

        settings.WordWrap = on;
        foreach (var tab in Documents)
        {
            tab.Editor.WordWrap = on;
        }

        SaveSettingsQuietly();
    }

    private void SetRightToLeft(bool on)
    {
        if (Current is not { } tab)
        {
            return;
        }

        tab.SetRightToLeft(on);
    }

    private void SetEncoding(TextEncodingKind kind)
    {
        if (Current is not { } tab || tab.Encoding == kind)
        {
            return;
        }

        if (!TextFileIO.CanEncode(tab.TextSnapshot, kind))
        {
            Dialogs.ShowError(this, $"Can't switch to {TextFileIO.DisplayName(kind)}",
                "This document has characters that encoding can't store (for example emoji or non-Latin text). Keep UTF-8 to save them.");
            return;
        }

        tab.Encoding = kind;
        tab.MarkModified();
        UpdateStatusNow();
        ShowMessage($"{tab.DisplayName} will be saved as {TextFileIO.DisplayName(kind)}.");
    }

    private void SetLineEnding(LineEnding eol)
    {
        if (Current is not { } tab || tab.LineEnding == eol)
        {
            return;
        }

        tab.LineEnding = eol;
        tab.MarkModified();
        UpdateStatusNow();
        ShowMessage($"{tab.DisplayName} will be saved with {TextFileIO.DisplayName(eol)} line endings.");
    }

    private void Zoom(float delta)
    {
        if (Current is not { } tab)
        {
            return;
        }

        var target = delta == 0 ? 1f : Math.Clamp((float)Math.Round(tab.Editor.ZoomFactor + delta, 1), 0.3f, 5f);
        tab.Editor.ZoomFactor = 1f; // RichTextBox ignores setting the same value twice
        tab.Editor.ZoomFactor = target;
        UpdateStatusNow();
    }

    private void InsertDateTime()
    {
        if (Current is { } tab)
        {
            var now = DateTime.Now;
            tab.Editor.SelectedText = string.Format(CultureInfo.CurrentCulture, "{0:t} {0:d}", now);
        }
    }

    // ---------- Line and Markdown edits ----------

    private void ApplyLineEdit(Func<string, int, int, TextEdit?> op)
    {
        if (Current is not { } tab)
        {
            return;
        }

        var e = tab.Editor;
        if (op(tab.TextSnapshot, e.SelectionStart, e.SelectionLength) is { } edit)
        {
            e.ApplyEdit(edit);
            e.ScrollToCaret();
        }
    }

    private void MarkdownEdit(Func<string, int, int, TextEdit> op)
    {
        if (Current is not { Kind: DocumentKind.Markdown } tab)
        {
            ShowMessage("Markdown tools work in .md files. Use File > Save As to save this tab as Markdown.");
            return;
        }

        var e = tab.Editor;
        e.ApplyEdit(op(tab.TextSnapshot, e.SelectionStart, e.SelectionLength));
        e.Focus();
    }

    private void GoToLine()
    {
        if (Current is not { } tab)
        {
            return;
        }

        var text = tab.TextSnapshot;
        var caret = Math.Min(tab.Editor.SelectionStart, text.Length);
        var currentLine = text.AsSpan(0, caret).Count('\n') + 1;
        using var dialog = new GoToLineDialog(currentLine, TextOps.LineCount(text));
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            tab.Editor.Select(TextOps.LineStartIndex(text, dialog.LineNumber), 0);
            tab.Editor.ScrollToCaret();
            tab.Editor.Focus();
        }
    }

    // ---------- Find / Replace ----------

    private void ShowFind(bool replace)
    {
        if (findDialog is null)
        {
            findDialog = new FindReplaceDialog();
            findDialog.ActionRequested += OnFindAction;
        }

        findDialog.ShowFor(this, replace, Current?.Editor.SelectedText);
    }

    private void FindAgain(bool forward)
    {
        if (lastFind is null)
        {
            ShowFind(replace: false);
            return;
        }

        Find(lastFind, forward);
    }

    private void OnFindAction(object? sender, FindAction action)
    {
        if (findDialog is null)
        {
            return;
        }

        var options = findDialog.Options;
        lastFind = options;
        switch (action)
        {
            case FindAction.FindNext:
                Find(options, forward: true);
                break;
            case FindAction.FindPrevious:
                Find(options, forward: false);
                break;
            case FindAction.Replace:
                ReplaceNext(options, findDialog.Replacement);
                break;
            case FindAction.ReplaceAll:
                var count = ReplaceAll(options, findDialog.Replacement);
                findDialog.SetStatus(count == 0 ? $"No matches for \"{options.Query}\"." : $"Replaced {count:N0} match{(count == 1 ? "" : "es")}.");
                break;
        }
    }

    internal bool Find(FindOptions options, bool forward)
    {
        if (Current is not { } tab)
        {
            return false;
        }

        var e = tab.Editor;
        var text = tab.TextSnapshot;
        var start = forward ? e.SelectionStart + e.SelectionLength : e.SelectionStart;
        var hit = TextOps.FindNext(text, options, start, forward);
        if (hit < 0)
        {
            var msg = $"Can't find \"{options.Query}\".";
            ShowMessage(msg);
            findDialog?.SetStatus(msg);
            return false;
        }

        var wrapped = forward ? hit < start : hit >= start;
        e.Select(hit, options.Query.Length);
        e.ScrollToCaret();
        var status = wrapped ? "Search wrapped around the document." : "";
        findDialog?.SetStatus(status);
        if (wrapped)
        {
            ShowMessage(status);
        }

        return true;
    }

    internal void ReplaceNext(FindOptions options, string replacement)
    {
        if (Current is not { } tab)
        {
            return;
        }

        var e = tab.Editor;
        if (e.SelectionLength == options.Query.Length && TextOps.IsMatchAt(tab.TextSnapshot, options, e.SelectionStart))
        {
            e.SelectedText = replacement;
        }

        Find(options, forward: true);
    }

    /// <summary>Plain text: one edit (single undo step). RTF: replaced back to front so formatting elsewhere is kept.</summary>
    internal int ReplaceAll(FindOptions options, string replacement)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (Current is not { } tab)
        {
            return 0;
        }

        var e = tab.Editor;
        var text = tab.TextSnapshot;
        if (tab.Kind == DocumentKind.Rtf || tab.HasRichFormatting)
        {
            var hits = TextOps.AllMatches(text, options).ToList();
            for (var i = hits.Count - 1; i >= 0; i--)
            {
                e.Select(hits[i], options.Query.Length);
                e.SelectedText = replacement;
            }

            ShowMessage($"Replaced {hits.Count:N0} match{(hits.Count == 1 ? "" : "es")}.");
            return hits.Count;
        }

        var (result, count) = TextOps.ReplaceAll(text, options, replacement);
        if (count > 0)
        {
            var caret = e.SelectionStart;
            e.SelectAll();
            e.SelectedText = result;
            e.Select(Math.Min(caret, e.TextLength), 0);
        }

        ShowMessage($"Replaced {count:N0} match{(count == 1 ? "" : "es")}.");
        return count;
    }

    // ---------- Speech ----------

    private void ToggleReadAloud()
    {
        if (speech.IsSpeaking)
        {
            speech.Stop();
            readAloudMenuItem.Text = "&Read Aloud";
            return;
        }

        if (Current is not { } tab)
        {
            return;
        }

        var text = tab.Editor.SelectionLength > 0 ? tab.Editor.SelectedText : tab.TextSnapshot;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowMessage("There's nothing to read. Type or select some text first.");
            return;
        }

        try
        {
            speech.Finished -= OnSpeechFinished;
            speech.Finished += OnSpeechFinished;
            speech.Speak(text, settings.SpeechVoice, settings.SpeechRate);
            readAloudMenuItem.Text = "Stop &Reading";
            ShowMessage(tab.Editor.SelectionLength > 0 ? "Reading the selection aloud. Press Ctrl+Shift+R to stop." : "Reading the document aloud. Press Ctrl+Shift+R to stop.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            Dialogs.ShowError(this, "Read Aloud isn't available", "Windows couldn't start a speech voice: " + ex.Message);
        }
    }

    private void OnSpeechFinished(object? sender, EventArgs e) => readAloudMenuItem.Text = "&Read Aloud";

    private void BuildVoiceMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        var voices = speech.VoiceNames();
        if (voices.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("(no voices installed)") { Enabled = false });
            return;
        }

        foreach (var voice in voices)
        {
            var v = voice;
            menu.DropDownItems.Add(new ToolStripMenuItem(voice.Replace("&", "&&", StringComparison.Ordinal), null, (_, _) =>
            {
                settings.SpeechVoice = v;
                SaveSettingsQuietly();
            })
            {
                Checked = string.Equals(settings.SpeechVoice, voice, StringComparison.Ordinal),
                ForeColor = palette.ChromeText,
                BackColor = palette.Chrome,
            });
        }
    }

    private void SetSpeechRate(int rate)
    {
        settings.SpeechRate = rate;
        SaveSettingsQuietly();
    }

    private void StartVoiceTyping()
    {
        if (Current is not { } tab)
        {
            return;
        }

        tab.Editor.Focus();
        BeginInvoke(NativeMethods.OpenVoiceTyping);
        ShowMessage("Voice typing: speak to type. Press Win+H again to stop.");
    }
}
