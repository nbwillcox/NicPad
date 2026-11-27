using System.Text;
using NicPad.Core;
using NicPad.UI;
using Xunit;

namespace NicPad.Tests;

/// <summary>Drives a real MainForm (off-screen, temp data folder, scripted dialogs).</summary>
public class UiTests
{
    [Fact]
    public void OpeningASecondFileDoesNotMarkTheFirstModified() => Sta.Run(() =>
    {
        // BUG-01
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.txt"), "first");
        File.WriteAllText(dir.File("b.txt"), "second");
        using var h = new FormHarness(dir);

        h.Form.OpenFiles([dir.File("a.txt")]);
        h.Form.OpenFiles([dir.File("b.txt")]);

        Assert.Equal(2, h.Form.Documents.Count);
        Assert.All(h.Form.Documents, d => Assert.False(d.IsModified, d.DisplayName));
        Assert.Equal("b.txt", h.Form.Current!.DisplayName);
    });

    [Fact]
    public void OpeningAFileReplacesTheBlankUntitledTab() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.txt"), "x");
        using var h = new FormHarness(dir);
        Assert.Null(h.Form.Current!.FilePath);

        h.Form.OpenFiles([dir.File("a.txt")]);
        Assert.Single(h.Form.Documents);
    });

    [Theory]
    [InlineData("notes.txt", TextEncodingKind.Utf8, LineEnding.CrLf)]
    [InlineData("notes.txt", TextEncodingKind.Utf8Bom, LineEnding.Lf)]
    [InlineData("notes.md", TextEncodingKind.Utf16LE, LineEnding.CrLf)]
    [InlineData("notes.md", TextEncodingKind.Utf8, LineEnding.Lf)]
    public void SavingPreservesUnicodeEncodingAndLineEndings(string name, TextEncodingKind kind, LineEnding eol)
    {
        // BUG-02 / BUG-09 / LANG-01: byte-for-byte round trip.
        Sta.Run(() =>
        {
            using var dir = new TempDir();
            var path = dir.File(name);
            var original = TextFileIO.Encode("Ünïcödé 😀 日本語\nsecond line\n", kind, eol);
            File.WriteAllBytes(path, original);
            using var h = new FormHarness(dir);

            var tab = h.Form.OpenFileCore(path, quiet: true, problems: null)!;
            Assert.Equal(kind, tab.Encoding);
            Assert.Equal(eol, tab.LineEnding);
            Assert.True(h.Form.SaveTo(tab, path));

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(tab.IsModified);
        });
    }

    [Fact]
    public void EditingMarksOnlyThatTabModified() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.txt"), "a");
        File.WriteAllText(dir.File("b.txt"), "b");
        using var h = new FormHarness(dir);
        h.Form.OpenFiles([dir.File("a.txt"), dir.File("b.txt")]);

        h.Form.Current!.Editor.AppendText(" more");
        var modified = h.Form.Documents.Where(d => d.IsModified).Select(d => d.DisplayName).ToList();
        Assert.Equal(["b.txt"], modified);
        Assert.Equal("b.txt* - NicPad", h.Form.Text);
    });

    [Fact]
    public void ReplaceAllUpdatesTextAsOneUndoStep() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        using var h = new FormHarness(dir);
        var editor = h.Form.Current!.Editor;
        editor.Text = "cat dog cat";
        editor.ClearUndo();

        var count = h.Form.ReplaceAll(new FindOptions("cat", false, false), "bird");
        Assert.Equal(2, count);
        Assert.Equal("bird dog bird", editor.Text);

        editor.Undo();
        Assert.Equal("cat dog cat", editor.Text);
    });

    [Fact]
    public void FindSelectsNextMatchAndWraps() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        using var h = new FormHarness(dir);
        var editor = h.Form.Current!.Editor;
        editor.Text = "one two one";
        editor.Select(0, 0);
        var o = new FindOptions("one", false, false);

        Assert.True(h.Form.Find(o, forward: true));
        Assert.Equal(0, editor.SelectionStart);
        Assert.True(h.Form.Find(o, forward: true));
        Assert.Equal(8, editor.SelectionStart);
        Assert.True(h.Form.Find(o, forward: true));
        Assert.Equal(0, editor.SelectionStart);
        Assert.False(h.Form.Find(o with { Query = "zzz" }, forward: true));
    });

    [Fact]
    public void CrashRecoveryRestoresUnsavedWorkIncludingUntitled() => Sta.Run(() =>
    {
        // FEA-11 / SEC-07
        using var dir = new TempDir();
        var path = dir.File("saved.md");
        File.WriteAllText(path, "on disk");

        using (var first = new FormHarness(dir))
        {
            first.Form.Current!.Editor.Text = "never saved 😀";
            first.Form.OpenFiles([path]);
            first.Form.Current!.Editor.AppendText(" + edits");
            first.Form.WriteBackups();
            // Disposed without closing = simulated crash.
        }

        using var second = new FormHarness(dir, restoreSession: true);
        var docs = second.Form.Documents;
        Assert.Equal(2, docs.Count);
        var untitled = docs.Single(d => d.FilePath is null);
        Assert.Equal("never saved 😀", untitled.Editor.Text);
        Assert.True(untitled.IsModified);
        var saved = docs.Single(d => d.FilePath is not null);
        Assert.Equal("on disk + edits", saved.Editor.Text);
        Assert.True(saved.IsModified);
        Assert.Equal("on disk", File.ReadAllText(path));
    });

    [Fact]
    public void CleanCloseRemembersFilesAndDropsBackups() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var path = dir.File("keep.txt");
        File.WriteAllText(path, "content");

        var first = new FormHarness(dir);
        first.Form.OpenFiles([path]);
        first.Form.Current!.Editor.AppendText("!");
        first.Form.WriteBackups();
        first.Dialogs.SaveAnswer = SaveChoice.DontSave;
        first.Form.Close();
        Assert.Contains(first.Dialogs.Calls, c => c == "save:keep.txt");
        Assert.Empty(Directory.Exists(first.Paths.BackupDir) ? Directory.GetFiles(first.Paths.BackupDir) : []);
        first.Dispose();

        using var second = new FormHarness(dir, restoreSession: true);
        var doc = Assert.Single(second.Form.Documents);
        Assert.Equal(path, doc.FilePath);
        Assert.Equal("content", doc.Editor.Text);
        Assert.False(doc.IsModified);
    });

    [Fact]
    public void CancellingTheSavePromptKeepsTheTabOpen() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        using var h = new FormHarness(dir);
        var tab = h.Form.Current!;
        tab.Editor.Text = "draft";
        h.Dialogs.SaveAnswer = SaveChoice.Cancel;

        Assert.False(h.Form.CloseTab(tab));
        Assert.Single(h.Form.Documents);
    });

    [Fact]
    public void ExternalChangesOfferReload() => Sta.Run(() =>
    {
        // FEA-10
        using var dir = new TempDir();
        var path = dir.File("watched.txt");
        File.WriteAllText(path, "v1");
        using var h = new FormHarness(dir);
        h.Form.OpenFiles([path]);

        File.WriteAllText(path, "version two");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        h.Dialogs.ReloadAnswer = true;
        h.Form.CheckDiskChanges();

        Assert.Contains(h.Dialogs.Calls, c => c == "reload:watched.txt");
        Assert.Equal("version two", h.Form.Current!.Editor.Text);
        Assert.False(h.Form.Current.IsModified);
    });

    [Fact]
    public void SavingFormattingToPlainTextWarnsFirst() => Sta.Run(() =>
    {
        // BUG-03
        using var dir = new TempDir();
        var path = dir.File("plain.txt");
        using var h = new FormHarness(dir);
        var tab = h.Form.Current!;
        tab.Editor.Text = "bold me";
        tab.Editor.SelectAll();
        h.Form.ApplyStyle(FontStyle.Bold, "**", "**");
        Assert.True(tab.HasRichFormatting);

        h.Dialogs.RichAnswer = RichSaveChoice.Cancel;
        Assert.False(h.Form.SaveTo(tab, path));
        Assert.False(File.Exists(path));

        h.Dialogs.RichAnswer = RichSaveChoice.SaveAsRtf;
        h.Dialogs.SavePath = dir.File("plain.rtf");
        Assert.True(h.Form.SaveTo(tab, path));
        Assert.True(File.Exists(dir.File("plain.rtf")));
        Assert.Contains("\\b", File.ReadAllText(dir.File("plain.rtf")), StringComparison.Ordinal);
        Assert.Equal(DocumentKind.Rtf, tab.Kind);
    });

    [Fact]
    public void ReadOnlyFileShowsErrorInsteadOfCrashing() => Sta.Run(() =>
    {
        // SEC-06
        using var dir = new TempDir();
        var path = dir.File("ro.txt");
        File.WriteAllText(path, "locked");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        using var h = new FormHarness(dir);
        var tab = h.Form.OpenFileCore(path, quiet: true, problems: null)!;
        tab.Editor.AppendText(" edit");

        Assert.False(h.Form.SaveTo(tab, path));
        Assert.Contains(h.Dialogs.Calls, c => c.StartsWith("error:Couldn't save", StringComparison.Ordinal));
        Assert.True(tab.IsModified);
        Assert.Equal("locked", File.ReadAllText(path));
    });

    [Fact]
    public void RtfWithEmbeddedObjectsLoadsWithoutThem() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var path = dir.File("doc.rtf");
        File.WriteAllText(path, @"{\rtf1\ansi Safe text{\object\objemb{\*\objclass Package}{\*\objdata 01050000}}}", Encoding.Latin1);
        using var h = new FormHarness(dir);

        var tab = h.Form.OpenFileCore(path, quiet: true, problems: null)!;
        Assert.Equal("Safe text", tab.Editor.Text.Trim());
        Assert.DoesNotContain("objdata", tab.Editor.Rtf!, StringComparison.Ordinal);
    });

    [Fact]
    public void MissingFilesAreReportedNotThrown() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        using var h = new FormHarness(dir);
        h.Form.OpenFiles([dir.File("nope.txt")]);
        Assert.Contains(h.Dialogs.Calls, c => c == "error:Couldn't find nope.txt");
        Assert.Single(h.Form.Documents);
    });

    [Fact]
    public void ReadingDirectionAndWordWrapKeepText() => Sta.Run(() =>
    {
        // LANG-02 / FEA-03: both can recreate the RichTextBox handle.
        using var dir = new TempDir();
        var path = dir.File("rtl.txt");
        File.WriteAllText(path, "שלום עולם\nhello");
        using var h = new FormHarness(dir);
        var tab = h.Form.OpenFileCore(path, quiet: true, problems: null)!;
        Application.DoEvents();

        tab.SetRightToLeft(true);
        Assert.True(tab.IsRightToLeft);
        Assert.Equal("שלום עולם\nhello", tab.Editor.Text);
        Assert.False(tab.IsModified);

        tab.Editor.WordWrap = false;
        tab.Editor.WordWrap = true;
        Assert.Equal("שלום עולם\nhello", tab.Editor.Text);

        tab.SetRightToLeft(false);
        Assert.Equal("שלום עולם\nhello", tab.Editor.Text);
    });

    [Fact]
    public void TabsCanBeReordered() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.txt"), "a");
        File.WriteAllText(dir.File("b.txt"), "b");
        File.WriteAllText(dir.File("c.txt"), "c");
        using var h = new FormHarness(dir);
        h.Form.OpenFiles([dir.File("a.txt"), dir.File("b.txt"), dir.File("c.txt")]);

        var tabs = (ThemedTabControl)h.Form.Current!.Parent!;
        tabs.MoveTab(2, 0);
        Assert.Equal(["c.txt", "a.txt", "b.txt"], h.Form.Documents.Select(d => d.DisplayName).ToArray());
        Assert.Equal("c.txt", h.Form.Current!.DisplayName);
    });
}
