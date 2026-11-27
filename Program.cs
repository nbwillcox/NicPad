using NicPad.Core;
using NicPad.UI;

namespace NicPad;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var paths = AppPaths.Default();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(paths.CrashLogFile, e.ExceptionObject as Exception);

        var settings = AppSettings.Load(paths.SettingsFile, out _);
        var files = ResolveFiles(args);
        var newWindow = args.Any(a => string.Equals(a, "--new-window", StringComparison.OrdinalIgnoreCase));

        using var instance = settings.SingleInstance && !newWindow ? SingleInstance.Acquire(paths.InstanceKey) : null;
        if (instance is { IsPrimary: false } && instance.TrySendToPrimary(files, NativeMethods.AllowForeground))
        {
            return;
        }

        using var form = new MainForm(paths);
        Application.ThreadException += (_, e) => form.HandleCrash(e.Exception);

        if (instance is { IsPrimary: true })
        {
            form.Shown += (_, _) => instance.Listen(list =>
            {
                try
                {
                    form.BeginInvoke(() => form.OpenFromAnotherInstance(list));
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    // Window is closing.
                }
            });
        }

        if (files.Count > 0)
        {
            form.OpenFiles(files);
        }

        Application.Run(form);
    }

    /// <summary>Command-line files: absolute, existing paths only; switches (--x) are skipped.</summary>
    private static List<string> ResolveFiles(string[] args)
    {
        var result = new List<string>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(arg);
                if (File.Exists(full))
                {
                    result.Add(full);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
            {
                // Not a usable path; skip it.
            }
        }

        return result;
    }
}
