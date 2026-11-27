using System.Text;
using System.Text.Json;

namespace NicPad.Core;

public enum PreviewMode
{
    Off,
    Full,
    Split,
}

public sealed class SessionTab
{
    /// <summary>Null for an Untitled document.</summary>
    public string? FilePath { get; set; }
    public string? Title { get; set; }
    public string FontFamily { get; set; } = "Consolas";
    public float FontSize { get; set; } = 11f;
    public string? BackupId { get; set; }
    public bool BackupIsRtf { get; set; }
    public TextEncodingKind Encoding { get; set; } = TextEncodingKind.Utf8;
    public LineEnding LineEnding { get; set; } = LineEnding.CrLf;
    public PreviewMode Preview { get; set; }
    public bool RightToLeft { get; set; }
    public float Zoom { get; set; } = 1f;
    public int Caret { get; set; }
}

/// <summary>Open tabs at exit (v1 files had only FilePath/FontFamily/FontSize, which still load).</summary>
public sealed class SessionData
{
    public const int MaxTabs = 50;
    public const int MaxFileBytes = 1024 * 1024;

    public int Version { get; set; } = 2;
    public int SelectedTabIndex { get; set; }

    /// <summary>v1 stored the theme here; read for migration only.</summary>
    public string? Theme { get; set; }

    public List<SessionTab> OpenFiles { get; set; } = [];

    public static SessionData? Load(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFileBytes)
            {
                return null;
            }

            var session = JsonSerializer.Deserialize<SessionData>(File.ReadAllText(path), AppSettings.JsonOptions);
            session?.Normalize();
            return session;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Normalize();
        TextFileIO.WriteAtomic(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, AppSettings.JsonOptions)));
    }

    /// <summary>Drops anything malformed. Backup ids are checked strictly because they become file names.</summary>
    public void Normalize()
    {
        OpenFiles = (OpenFiles ?? [])
            .Where(t => t is not null)
            .Where(t => t.FilePath is null || (t.FilePath.Length <= 1024 && Path.IsPathFullyQualified(t.FilePath)))
            .Take(MaxTabs)
            .ToList();

        foreach (var tab in OpenFiles)
        {
            tab.FontFamily = string.IsNullOrWhiteSpace(tab.FontFamily) || tab.FontFamily.Length > 100 ? "Consolas" : tab.FontFamily;
            tab.FontSize = AppSettings.ClampFontSize(tab.FontSize);
            tab.Title = tab.Title is { Length: > 0 and <= 100 } ? tab.Title : null;
            tab.BackupId = BackupStore.IsValidId(tab.BackupId) ? tab.BackupId : null;
            tab.Zoom = float.IsFinite(tab.Zoom) ? Math.Clamp(tab.Zoom, 0.25f, 5f) : 1f;
            tab.Caret = Math.Max(0, tab.Caret);
            if (!Enum.IsDefined(tab.Encoding))
            {
                tab.Encoding = TextEncodingKind.Utf8;
            }

            if (!Enum.IsDefined(tab.LineEnding))
            {
                tab.LineEnding = LineEnding.CrLf;
            }

            if (!Enum.IsDefined(tab.Preview))
            {
                tab.Preview = PreviewMode.Off;
            }
        }

        if (SelectedTabIndex < 0 || SelectedTabIndex >= OpenFiles.Count)
        {
            SelectedTabIndex = 0;
        }
    }
}
