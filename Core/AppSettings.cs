using System.Text;
using System.Text.Json;

namespace NicPad.Core;

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>User preferences (separate from the session so they survive closing every tab).</summary>
public sealed class AppSettings
{
    public const int MaxRecentFiles = 10;
    public const int MaxFileBytes = 256 * 1024;
    public const float MinFontSize = 6f;
    public const float MaxFontSize = 72f;

    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 8 };

    public string Theme { get; set; } = "System";
    public string DefaultFontFamily { get; set; } = "Consolas";
    public float DefaultFontSize { get; set; } = 11f;
    public bool WordWrap { get; set; } = true;
    public bool AllowRemoteImages { get; set; }
    public bool AutoBackup { get; set; } = true;
    public int BackupIntervalSeconds { get; set; } = 30;
    public bool HotExit { get; set; }
    public bool SingleInstance { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public string? SpeechVoice { get; set; }
    public int SpeechRate { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public WindowPlacement? Window { get; set; }

    public static float ClampFontSize(float size) =>
        float.IsFinite(size) ? Math.Clamp(size, MinFontSize, MaxFontSize) : 11f;

    /// <summary>Loads settings; a missing, oversized or corrupt file yields defaults. <paramref name="existed"/> tells first-run apart.</summary>
    public static AppSettings Load(string path, out bool existed)
    {
        existed = File.Exists(path);
        if (!existed)
        {
            return new AppSettings();
        }

        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Normalize();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        TextFileIO.WriteAtomic(path, Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Clamps every value into its valid range; never trust what's on disk.</summary>
    public void Normalize()
    {
        Theme = string.IsNullOrWhiteSpace(Theme) || Theme.Length > 40 ? "System" : Theme;
        DefaultFontFamily = string.IsNullOrWhiteSpace(DefaultFontFamily) || DefaultFontFamily.Length > 100 ? "Consolas" : DefaultFontFamily;
        DefaultFontSize = ClampFontSize(DefaultFontSize);
        BackupIntervalSeconds = Math.Clamp(BackupIntervalSeconds, 5, 3600);
        SpeechRate = Math.Clamp(SpeechRate, -10, 10);
        SpeechVoice = SpeechVoice is { Length: > 200 } ? null : SpeechVoice;

        RecentFiles = (RecentFiles ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 1024 && Path.IsPathFullyQualified(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFiles)
            .ToList();

        if (Window is { } w && (w.Width < 200 || w.Height < 150 || w.Width > 20000 || w.Height > 20000))
        {
            Window = null;
        }
    }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }
}
