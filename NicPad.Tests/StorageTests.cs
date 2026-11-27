using System.Text;
using NicPad.Core;
using Xunit;

namespace NicPad.Tests;

public class SettingsTests
{
    [Fact]
    public void NormalizeClampsEverything()
    {
        var s = new AppSettings
        {
            Theme = new string('x', 500),
            DefaultFontFamily = "",
            DefaultFontSize = 9000,
            BackupIntervalSeconds = 0,
            SpeechRate = 99,
            RecentFiles = ["relative.txt", "C:\\a.txt", "c:\\A.TXT", new string('a', 2000)],
            Window = new WindowPlacement { Width = 10, Height = 10 },
        };
        s.Normalize();
        Assert.Equal("System", s.Theme);
        Assert.Equal("Consolas", s.DefaultFontFamily);
        Assert.Equal(72f, s.DefaultFontSize);
        Assert.Equal(5, s.BackupIntervalSeconds);
        Assert.Equal(10, s.SpeechRate);
        Assert.Equal(["C:\\a.txt"], s.RecentFiles);
        Assert.Null(s.Window);
    }

    [Fact]
    public void CorruptOrOversizedFilesFallBackToDefaults()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal("System", AppSettings.Load(path, out var existed).Theme);
        Assert.True(existed);

        File.WriteAllText(path, "{\"Theme\":\"Dark\"," + new string(' ', AppSettings.MaxFileBytes) + "}");
        Assert.Equal("System", AppSettings.Load(path, out _).Theme);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var s = new AppSettings { Theme = "Dark", WordWrap = false, AllowRemoteImages = true };
        s.AddRecentFile("C:\\one.md");
        s.Save(path);

        var loaded = AppSettings.Load(path, out var existed);
        Assert.True(existed);
        Assert.Equal("Dark", loaded.Theme);
        Assert.False(loaded.WordWrap);
        Assert.True(loaded.AllowRemoteImages);
        Assert.Equal(["C:\\one.md"], loaded.RecentFiles);
    }

    [Fact]
    public void RecentFilesAreDeduplicatedAndCapped()
    {
        var s = new AppSettings();
        for (var i = 0; i < 15; i++)
        {
            s.AddRecentFile($"C:\\f{i}.txt");
        }

        s.AddRecentFile("c:\\F3.TXT");
        Assert.Equal(AppSettings.MaxRecentFiles, s.RecentFiles.Count);
        Assert.Equal("c:\\F3.TXT", s.RecentFiles[0]);
        Assert.Single(s.RecentFiles, p => p.Equals("C:\\f3.txt", StringComparison.OrdinalIgnoreCase));
    }
}

public class SessionTests
{
    [Fact]
    public void TamperedEntriesAreDropped()
    {
        var session = new SessionData
        {
            SelectedTabIndex = 99,
            OpenFiles =
            [
                new SessionTab { FilePath = "relative\\path.txt" },
                new SessionTab { FilePath = "C:\\ok.txt", BackupId = "..\\..\\Windows\\win", FontSize = 500, Zoom = float.NaN },
                new SessionTab { FilePath = null, BackupId = new string('a', 32), Title = new string('t', 500) },
            ],
        };
        session.Normalize();

        Assert.Equal(2, session.OpenFiles.Count);
        Assert.Null(session.OpenFiles[0].BackupId);
        Assert.Equal(72f, session.OpenFiles[0].FontSize);
        Assert.Equal(1f, session.OpenFiles[0].Zoom);
        Assert.Equal(new string('a', 32), session.OpenFiles[1].BackupId);
        Assert.Null(session.OpenFiles[1].Title);
        Assert.Equal(0, session.SelectedTabIndex);
    }

    [Fact]
    public void TabCountIsCapped()
    {
        var session = new SessionData { OpenFiles = Enumerable.Range(0, 200).Select(i => new SessionTab { FilePath = $"C:\\{i}.txt" }).ToList() };
        session.Normalize();
        Assert.Equal(SessionData.MaxTabs, session.OpenFiles.Count);
    }

    [Fact]
    public void Version1SessionFilesStillLoad()
    {
        using var dir = new TempDir();
        var path = dir.File("session.json");
        File.WriteAllText(path, """
            { "SelectedTabIndex": 0, "Theme": "Matrix",
              "OpenFiles": [ { "FilePath": "C:\\notes.md", "FontFamily": "Consolas", "FontSize": 12 } ] }
            """);
        var session = SessionData.Load(path)!;
        Assert.Equal("Matrix", session.Theme);
        Assert.Equal("C:\\notes.md", session.OpenFiles.Single().FilePath);
        Assert.Equal(12f, session.OpenFiles.Single().FontSize);
    }

    [Fact]
    public void OversizedSessionIsIgnored()
    {
        using var dir = new TempDir();
        var path = dir.File("session.json");
        File.WriteAllText(path, new string(' ', SessionData.MaxFileBytes + 1));
        Assert.Null(SessionData.Load(path));
    }
}

public class BackupStoreTests
{
    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("short")]
    [InlineData(null)]
    public void InvalidIdsAreRejected(string? id)
    {
        Assert.False(BackupStore.IsValidId(id));
    }

    [Fact]
    public void WriteReadDelete()
    {
        using var dir = new TempDir();
        var store = new BackupStore(dir.Path);
        var id = BackupStore.NewId();
        store.Write(id, "unsaved 😀");
        Assert.Equal("unsaved 😀", store.Read(id));
        store.Delete(id);
        Assert.Null(store.Read(id));
        Assert.Throws<ArgumentException>(() => store.Write("../x", "nope"));
    }

    [Fact]
    public void DeleteOrphansKeepsLiveAndRecentBackups()
    {
        using var dir = new TempDir();
        var store = new BackupStore(dir.Path);
        string live = BackupStore.NewId(), oldOrphan = BackupStore.NewId(), newOrphan = BackupStore.NewId();
        store.Write(live, "a");
        store.Write(oldOrphan, "b");
        store.Write(newOrphan, "c");
        File.SetLastWriteTimeUtc(Path.Combine(dir.Path, oldOrphan + ".txt"), DateTime.UtcNow.AddDays(-5));
        File.WriteAllText(Path.Combine(dir.Path, "not-a-backup.txt"), "keep me");

        store.DeleteOrphans([live], TimeSpan.FromDays(2));

        Assert.True(store.Exists(live));
        Assert.False(store.Exists(oldOrphan));
        Assert.True(store.Exists(newOrphan));
        Assert.True(File.Exists(Path.Combine(dir.Path, "not-a-backup.txt")));
    }
}

public class ThemeTests
{
    public static TheoryData<string> Palettes => ["Light", "Dark", "Matrix"];

    [Theory]
    [MemberData(nameof(Palettes))]
    public void BuiltInThemesMeetWcagAA(string name)
    {
        // A11Y-04: 4.5:1 for text, 3:1 for icons.
        var p = Themes.FindBuiltIn(name)!;
        Assert.True(Themes.ContrastRatio(p.EditorText, p.Editor) >= 4.5, "editor text");
        Assert.True(Themes.ContrastRatio(p.ChromeText, p.Chrome) >= 4.5, "menu text");
        Assert.True(Themes.ContrastRatio(p.TabText, p.TabBack) >= 4.5, "tab text");
        Assert.True(Themes.ContrastRatio(p.TabText, p.TabSelected) >= 4.5, "selected tab text");
        Assert.True(Themes.ContrastRatio(p.TabClose, p.TabBack) >= 3.0, "close icon");
        Assert.True(Themes.ContrastRatio(p.Preview.Text, p.Preview.Background) >= 4.5, "preview text");
        Assert.True(Themes.ContrastRatio(p.Preview.Muted, p.Preview.Background) >= 4.5, "preview muted");
        Assert.True(Themes.ContrastRatio(p.Preview.Link, p.Preview.Background) >= 4.5, "preview link");
        Assert.True(Themes.ContrastRatio(p.Preview.Text, p.Preview.Code) >= 4.5, "preview code");
        if (name != "Light")
        {
            Assert.True(Themes.ContrastRatio(p.HoverText, p.HoverBack) >= 4.5, "hover");
        }
    }

    [Fact]
    public void SampleThemeParses()
    {
        var t = Themes.Parse(Themes.SampleTheme);
        Assert.Equal("Solarized Dark", t.Name);
        Assert.True(t.IsDark);
        Assert.Equal(Themes.Hex("#002b36"), t.Editor);
        Assert.Equal(Themes.Hex("#2aa198"), t.Preview.Link);
    }

    [Theory]
    [InlineData("""{ "name": "X", "colors": { "editor": "red" } }""")]
    [InlineData("""{ "name": "X", "colors": { "editr": "#000000" } }""")]
    [InlineData("""{ "name": "<script>", "colors": {} }""")]
    [InlineData("""{ "colors": {} }""")]
    [InlineData("""{ "name": "X", "colors": { "editor": 5 } }""")]
    public void InvalidThemesAreRejected(string json)
    {
        Assert.ThrowsAny<FormatException>(() => Themes.Parse(json));
    }

    [Fact]
    public void LoadCustomSkipsBadFilesAndDuplicates()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.json"), """{ "name": "Mine", "base": "Dark" }""");
        File.WriteAllText(dir.File("b.json"), """{ "name": "Mine" }""");
        File.WriteAllText(dir.File("c.json"), """{ "name": "Dark" }""");
        File.WriteAllText(dir.File("d.json"), "{ broken");
        File.WriteAllText(dir.File("e.json"), new string(' ', Themes.MaxThemeFileBytes + 1));

        var errors = new List<string>();
        var themes = Themes.LoadCustom(dir.Path, errors);
        Assert.Equal("Mine", Assert.Single(themes).Name);
        Assert.Equal(4, errors.Count);
    }
}

public class RtfSafetyTests
{
    [Fact]
    public void EmbeddedObjectsAreRemovedAndTextKept()
    {
        const string rtf = @"{\rtf1\ansi Hello {\object\objemb{\*\objclass Package}{\*\objdata 0105000002000000{nested}}{\result {\pict abc}}} World \{literal\}}";
        var cleaned = RtfSafety.StripEmbeddedObjects(rtf, out var removed);
        Assert.Equal(1, removed);
        Assert.DoesNotContain("objdata", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("Package", cleaned, StringComparison.Ordinal);
        Assert.Contains("Hello", cleaned, StringComparison.Ordinal);
        Assert.Contains(@"World \{literal\}}", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void BinaryDataWithBracesIsSkippedCorrectly()
    {
        var rtf = "{\\rtf1 A {\\object {\\*\\objdata \\bin4 }}}{} B} C}";
        var cleaned = RtfSafety.StripEmbeddedObjects(rtf, out var removed);
        Assert.Equal(1, removed);
        Assert.Equal("{\\rtf1 A  C}", cleaned);
    }

    [Fact]
    public void CleanRtfIsUnchanged()
    {
        const string rtf = @"{\rtf1\ansi {\b bold} {\pict\pngblip 89504e47}}";
        Assert.Same(rtf, RtfSafety.StripEmbeddedObjects(rtf, out var removed));
        Assert.Equal(0, removed);
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void MessagesAreValidated()
    {
        var payload = Encoding.UTF8.GetBytes("""["C:\\ok.txt", "relative.txt", "", "\\\\server\\share\\x.txt"]""");
        Assert.Equal(["C:\\ok.txt", "\\\\server\\share\\x.txt"], SingleInstance.ParseMessage(payload));
    }

    [Fact]
    public async Task SecondInstanceHandsFilesToFirst()
    {
        var key = Guid.NewGuid().ToString("N")[..16];
        using var primary = SingleInstance.Acquire(key);
        Assert.True(primary.IsPrimary);

        using var received = new ManualResetEventSlim();
        IReadOnlyList<string>? files = null;
        primary.Listen(list =>
        {
            files = list;
            received.Set();
        });

        var secondary = await Task.Run(
            () =>
            {
                using var other = SingleInstance.Acquire(key);
                Assert.False(other.IsPrimary);
                return other.TrySendToPrimary(["C:\\from-second.txt"]);
            },
            TestContext.Current.CancellationToken);

        Assert.True(secondary);
        Assert.True(received.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(["C:\\from-second.txt"], files);
    }
}
