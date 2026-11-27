using NicPad.Core;

namespace NicPad.UI;

/// <summary>RichTextBox that pastes plain text in plain-text documents and reports Ctrl+wheel zoom.</summary>
internal sealed class EditorBox : RichTextBox
{
    private const int WmMouseWheel = 0x020A;

    public EditorBox()
    {
        BorderStyle = BorderStyle.None;
        AcceptsTab = true;
        DetectUrls = false;
        HideSelection = false;
        ScrollBars = RichTextBoxScrollBars.Both;
        MaxLength = int.MaxValue;
        AllowDrop = true;
        EnableAutoDragDrop = false;
    }

    public event EventHandler? ZoomChanged;

    /// <summary>When true, pasted content drops fonts/colors, and RichEdit's paragraph-format shortcuts are disabled.</summary>
    public bool PlainTextOnly { get; set; } = true;

    public void PasteSmart()
    {
        if (!PlainTextOnly)
        {
            Paste();
            return;
        }

        if (Clipboard.ContainsText(TextDataFormat.UnicodeText) || Clipboard.ContainsText())
        {
            SelectedText = TextFileIO.NormalizeToLf(Clipboard.GetText(TextDataFormat.UnicodeText));
        }
    }

    public void ApplyEdit(TextEdit edit)
    {
        Select(edit.Start, edit.Length);
        SelectedText = edit.Replacement;
        Select(edit.SelectionStart, edit.SelectionLength);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.V) or (Keys.Shift | Keys.Insert))
        {
            PasteSmart();
            return true;
        }

        // RichEdit's built-in alignment / line-spacing shortcuts silently reformat plain text.
        if (PlainTextOnly && keyData is (Keys.Control | Keys.E) or (Keys.Control | Keys.R) or (Keys.Control | Keys.L)
            or (Keys.Control | Keys.J) or (Keys.Control | Keys.D5) or (Keys.Control | Keys.Shift | Keys.A))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmMouseWheel && (ModifierKeys & Keys.Control) == Keys.Control)
        {
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
