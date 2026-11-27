using NicPad.Core;

namespace NicPad.UI;

public enum DocumentKind
{
    Plain,
    Markdown,
    Rtf,
}

/// <summary>One open document: editor, optional preview, and everything needed to save it faithfully.</summary>
internal sealed class DocumentTab : TabPage
{
    private readonly SplitContainer split;
    private Font? ownedFont;
    private int suppressChanges;
    private PreviewMode previewMode;
    private string? snapshot;
    private int snapshotVersion = -1;

    public DocumentTab(string untitledName, string fontFamily, float fontSize)
    {
        UntitledName = untitledName;
        FontFamilyName = fontFamily;
        FontSize = fontSize;

        Editor = new EditorBox { Name = "Editor", Dock = DockStyle.Fill, AccessibleName = "Document text" };
        Editor.TextChanged += (_, _) =>
        {
            ContentVersion++;
            if (suppressChanges > 0)
            {
                return;
            }

            if (!IsModified)
            {
                IsModified = true;
                ModifiedChanged?.Invoke(this, EventArgs.Empty);
            }

            ContentChanged?.Invoke(this, EventArgs.Empty);
        };

        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Panel2Collapsed = true,
            SplitterWidth = 4,
            TabStop = false,
        };
        split.Panel1.Controls.Add(Editor);
        Controls.Add(split);
        ApplyFont();
        UpdateText();
    }

    /// <summary>Raised once when the document goes from saved to modified (and on explicit resets).</summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>Raised on every user edit.</summary>
    public event EventHandler? ContentChanged;

    public EditorBox Editor { get; }
    public PreviewHost? Preview { get; private set; }
    public string BackupId { get; set; } = BackupStore.NewId();
    public string? FilePath { get; private set; }
    public string UntitledName { get; set; }
    public TextEncodingKind Encoding { get; set; } = TextEncodingKind.Utf8;
    public LineEnding LineEnding { get; set; } = LineEnding.CrLf;
    public bool IsModified { get; private set; }

    /// <summary>Set once the user applies bold/italic/underline, so a plain-text save can warn about losing it.</summary>
    public bool HasRichFormatting { get; set; }

    /// <summary>Bumped on every text change (including programmatic ones).</summary>
    public int ContentVersion { get; private set; }

    /// <summary>ContentVersion at the last crash-recovery backup.</summary>
    public int BackupVersion { get; set; } = -1;

    /// <summary>Editor text, cached per <see cref="ContentVersion"/> so caret moves don't copy the whole document.</summary>
    public string TextSnapshot
    {
        get
        {
            if (snapshot is null || snapshotVersion != ContentVersion)
            {
                snapshot = Editor.Text;
                snapshotVersion = ContentVersion;
            }

            return snapshot;
        }
    }

    public string FontFamilyName { get; private set; }
    public float FontSize { get; private set; }
    public DateTime DiskWriteTimeUtc { get; set; }
    public long DiskLength { get; set; }
    public bool IsCheckingDisk { get; set; }

    public string Extension => FilePath is null ? ".txt" : System.IO.Path.GetExtension(FilePath).ToLowerInvariant();

    public DocumentKind Kind => Extension switch
    {
        ".md" or ".markdown" or ".mdown" or ".mkd" => DocumentKind.Markdown,
        ".rtf" => DocumentKind.Rtf,
        _ => DocumentKind.Plain,
    };

    public string DisplayName => FilePath is null ? UntitledName : System.IO.Path.GetFileName(FilePath);

    public PreviewMode PreviewMode
    {
        get => Kind == DocumentKind.Markdown ? previewMode : PreviewMode.Off;
        set
        {
            previewMode = value;
            ApplyLayout();
        }
    }

    public void SetFilePath(string? path)
    {
        FilePath = path;
        Editor.PlainTextOnly = Kind != DocumentKind.Rtf;
        UpdateText();
        ApplyLayout();
    }

    public void MarkSaved()
    {
        IsModified = false;
        UpdateText();
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkModified()
    {
        IsModified = true;
        UpdateText();
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateText()
    {
        Text = DisplayName + (IsModified ? "*" : "");
        ToolTipText = FilePath ?? UntitledName;
    }

    /// <summary>Runs a programmatic change (load, reload, restore) without marking the document modified.</summary>
    public void WithoutChangeTracking(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        suppressChanges++;
        try
        {
            action();
        }
        finally
        {
            suppressChanges--;
        }
    }

    public bool IsRightToLeft => Editor.RightToLeft == RightToLeft.Yes;

    /// <summary>
    /// Changing RightToLeft recreates the RichTextBox handle, which drops its contents; carry them across.
    /// </summary>
    public void SetRightToLeft(bool on)
    {
        if (IsRightToLeft == on)
        {
            return;
        }

        WithoutChangeTracking(() =>
        {
            var rtf = Editor.Rtf;
            var caret = Editor.SelectionStart;
            var wasModified = IsModified;
            Editor.RightToLeft = on ? RightToLeft.Yes : RightToLeft.No;
            if (!string.IsNullOrEmpty(rtf))
            {
                Editor.Rtf = rtf;
            }

            Editor.Select(Math.Min(caret, Editor.TextLength), 0);
            IsModified = wasModified;
        });
    }

    public void SetFont(string family, float size)
    {
        FontFamilyName = family;
        FontSize = AppSettings.ClampFontSize(size);
        ApplyFont();
    }

    public PreviewHost EnsurePreview(string userDataFolder, ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (Preview is null)
        {
            Preview = new PreviewHost(userDataFolder, palette.Preview.Background);
            Preview.SetBackground(palette.Preview.Background, palette.Preview.Text);
            split.Panel2.Controls.Add(Preview);
        }

        return Preview;
    }

    public void ApplyPalette(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        BackColor = palette.Editor;
        split.BackColor = palette.Separator;
        split.Panel1.BackColor = palette.Editor;
        split.Panel2.BackColor = palette.Preview.Background;
        WithoutChangeTracking(() =>
        {
            Editor.BackColor = palette.Editor;
            Editor.ForeColor = palette.EditorText;
        });
        Preview?.SetBackground(palette.Preview.Background, palette.Preview.Text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Preview?.Dispose();
            ownedFont?.Dispose();
            ownedFont = null;
        }

        base.Dispose(disposing);
    }

    private void ApplyFont()
    {
        var zoom = Editor.ZoomFactor;
        var font = new Font(FontFamilyName, FontSize);
        WithoutChangeTracking(() => Editor.Font = font);

        // Control.Font ignores a font equal to the current one and keeps the old object,
        // so only dispose the previous font if the control actually switched to the new one.
        if (ReferenceEquals(Editor.Font, font))
        {
            ownedFont?.Dispose();
            ownedFont = font;
        }
        else
        {
            font.Dispose();
        }

        if (Math.Abs(zoom - 1f) > 0.001f)
        {
            // RichTextBox resets zoom when its font changes.
            Editor.ZoomFactor = 1f;
            Editor.ZoomFactor = zoom;
        }
    }

    private void ApplyLayout()
    {
        switch (PreviewMode)
        {
            case PreviewMode.Full:
                split.Panel2Collapsed = false;
                split.Panel1Collapsed = true;
                break;
            case PreviewMode.Split:
                split.Panel1Collapsed = false;
                split.Panel2Collapsed = false;
                if (split.Width > 0)
                {
                    split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Width / 2);
                }

                break;
            default:
                split.Panel1Collapsed = false;
                split.Panel2Collapsed = true;
                break;
        }
    }
}
