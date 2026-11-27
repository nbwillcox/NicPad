using System.Security.Cryptography;
using System.Text;

namespace NicPad.Core;

/// <summary>
/// Per-user storage locations. Set NICPAD_DATA_DIR to redirect everything
/// (used by tests and smoke runs so the real session is never touched).
/// </summary>
public sealed class AppPaths
{
    public const string DataDirVariable = "NICPAD_DATA_DIR";

    public AppPaths(string root, string? webViewDataDir = null)
    {
        Root = Path.GetFullPath(root);
        WebViewDataDir = webViewDataDir ?? Path.Combine(Root, "WebView2");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string WebViewDataDir { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string SessionFile => Path.Combine(Root, "session.json");
    public string BackupDir => Path.Combine(Root, "backups");
    public string ThemesDir => Path.Combine(Root, "themes");
    public string CrashLogFile => Path.Combine(Root, "crash.log");

    /// <summary>Short stable hash of the data root, so instances with different data dirs don't share an IPC channel.</summary>
    public string InstanceKey =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Root.ToUpperInvariant())))[..16];

    public static AppPaths Default()
    {
        var overrideDir = Environment.GetEnvironmentVariable(DataDirVariable);
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return new AppPaths(overrideDir);
        }

        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NicPad");
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NicPad", "WebView2");
        return new AppPaths(roaming, local);
    }
}
