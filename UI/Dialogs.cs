namespace NicPad.UI;

public enum SaveChoice
{
    Save,
    DontSave,
    Cancel,
}

public enum RichSaveChoice
{
    SaveAsRtf,
    SavePlain,
    Cancel,
}

/// <summary>
/// Every prompt the app shows, worded as "what happened + what you can do".
/// Virtual so UI tests can script the answers.
/// </summary>
public class DialogService
{
    public virtual SaveChoice AskSaveChanges(IWin32Window owner, string documentName)
    {
        var save = new TaskDialogButton("&Save");
        var dontSave = new TaskDialogButton("Do&n't Save");
        var page = new TaskDialogPage
        {
            Caption = "NicPad",
            Heading = $"Save changes to {documentName}?",
            Text = "Your changes will be lost if you don't save them.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { save, dontSave, TaskDialogButton.Cancel },
            DefaultButton = save,
        };
        var result = TaskDialog.ShowDialog(owner, page);
        return result == save ? SaveChoice.Save : result == dontSave ? SaveChoice.DontSave : SaveChoice.Cancel;
    }

    public virtual RichSaveChoice AskRichFormattingLoss(IWin32Window owner, string documentName)
    {
        var rtf = new TaskDialogButton("Save as &RTF...");
        var plain = new TaskDialogButton("Save as &Plain Text");
        var page = new TaskDialogPage
        {
            Caption = "NicPad",
            Heading = $"{documentName} has bold, italic or underline formatting",
            Text = "Plain text files can't store formatting, so it would be lost. Save as an RTF document to keep it.",
            Icon = TaskDialogIcon.Information,
            Buttons = { rtf, plain, TaskDialogButton.Cancel },
            DefaultButton = rtf,
        };
        var result = TaskDialog.ShowDialog(owner, page);
        return result == rtf ? RichSaveChoice.SaveAsRtf : result == plain ? RichSaveChoice.SavePlain : RichSaveChoice.Cancel;
    }

    public virtual bool ConfirmOpenLink(IWin32Window owner, string url)
    {
        var open = new TaskDialogButton("&Open Link");
        var page = new TaskDialogPage
        {
            Caption = "NicPad",
            Heading = "Open this link in your browser?",
            Text = url,
            Icon = TaskDialogIcon.Information,
            Buttons = { open, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };
        return TaskDialog.ShowDialog(owner, page) == open;
    }

    public virtual bool ConfirmReload(IWin32Window owner, string documentName, bool hasLocalChanges)
    {
        var reload = new TaskDialogButton("&Reload");
        var keep = new TaskDialogButton("&Keep My Version");
        var page = new TaskDialogPage
        {
            Caption = "NicPad",
            Heading = $"{documentName} was changed by another program",
            Text = hasLocalChanges
                ? "Reloading replaces your unsaved changes with the version on disk."
                : "Reload it to see the latest version?",
            Icon = hasLocalChanges ? TaskDialogIcon.Warning : TaskDialogIcon.Information,
            Buttons = { reload, keep },
            DefaultButton = hasLocalChanges ? keep : reload,
        };
        return TaskDialog.ShowDialog(owner, page) == reload;
    }

    public virtual bool Confirm(IWin32Window owner, string heading, string text, string confirmLabel)
    {
        var yes = new TaskDialogButton(confirmLabel);
        var page = new TaskDialogPage
        {
            Caption = "NicPad",
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Warning,
            Buttons = { yes, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };
        return TaskDialog.ShowDialog(owner, page) == yes;
    }

    public virtual void ShowError(IWin32Window owner, string heading, string text)
    {
        TaskDialog.ShowDialog(owner, new TaskDialogPage
        {
            Caption = "NicPad",
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Error,
            Buttons = { TaskDialogButton.OK },
        });
    }

    public virtual void ShowInfo(IWin32Window owner, string heading, string text)
    {
        TaskDialog.ShowDialog(owner, new TaskDialogPage
        {
            Caption = "NicPad",
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Information,
            Buttons = { TaskDialogButton.OK },
        });
    }

    public virtual string? AskOpenPath(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = FileFilters.Open,
            FilterIndex = 1,
            Multiselect = true,
            Title = "Open",
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? string.Join('\n', dialog.FileNames) : null;
    }

    public virtual string? AskSavePath(IWin32Window owner, string? currentPath, string suggestedName, string extension)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = FileFilters.Save,
            FilterIndex = FileFilters.SaveIndexFor(extension),
            FileName = suggestedName,
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = extension.TrimStart('.'),
        };
        if (currentPath is not null)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
        }

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}

public static class FileFilters
{
    public const string Open =
        "All Files (*.*)|*.*|Text Files (*.txt)|*.txt|Markdown Files (*.md;*.markdown)|*.md;*.markdown|Rich Text (*.rtf)|*.rtf";

    public const string Save =
        "Text File (*.txt)|*.txt|Markdown (*.md)|*.md|Rich Text Document (*.rtf)|*.rtf|All Files (*.*)|*.*";

    public static int SaveIndexFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".md" or ".markdown" => 2,
        ".rtf" => 3,
        ".txt" => 1,
        _ => 4,
    };
}
