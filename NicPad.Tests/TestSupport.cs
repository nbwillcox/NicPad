using System.Runtime.ExceptionServices;
using NicPad.Core;
using NicPad.UI;

namespace NicPad.Tests;

/// <summary>A throwaway folder deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nicpad-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class Sta
{
    /// <summary>WinForms controls need a single-threaded apartment.</summary>
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}

/// <summary>Answers every prompt from a script and records what was asked.</summary>
internal sealed class ScriptedDialogs : DialogService
{
    public List<string> Calls { get; } = [];
    public SaveChoice SaveAnswer { get; set; } = SaveChoice.DontSave;
    public RichSaveChoice RichAnswer { get; set; } = RichSaveChoice.SavePlain;
    public bool ReloadAnswer { get; set; } = true;
    public bool ConfirmAnswer { get; set; } = true;
    public string? SavePath { get; set; }

    public override SaveChoice AskSaveChanges(IWin32Window owner, string documentName)
    {
        Calls.Add("save:" + documentName);
        return SaveAnswer;
    }

    public override RichSaveChoice AskRichFormattingLoss(IWin32Window owner, string documentName)
    {
        Calls.Add("rich:" + documentName);
        return RichAnswer;
    }

    public override bool ConfirmOpenLink(IWin32Window owner, string url)
    {
        Calls.Add("link:" + url);
        return false;
    }

    public override bool ConfirmReload(IWin32Window owner, string documentName, bool hasLocalChanges)
    {
        Calls.Add("reload:" + documentName);
        return ReloadAnswer;
    }

    public override bool Confirm(IWin32Window owner, string heading, string text, string confirmLabel)
    {
        Calls.Add("confirm:" + heading);
        return ConfirmAnswer;
    }

    public override void ShowError(IWin32Window owner, string heading, string text) => Calls.Add("error:" + heading);

    public override void ShowInfo(IWin32Window owner, string heading, string text) => Calls.Add("info:" + heading);

    public override string? AskOpenPath(IWin32Window owner) => null;

    public override string? AskSavePath(IWin32Window owner, string? currentPath, string suggestedName, string extension)
    {
        Calls.Add("savepath:" + suggestedName);
        return SavePath;
    }
}

/// <summary>A MainForm wired to a temp data folder, shown off-screen.</summary>
internal sealed class FormHarness : IDisposable
{
    public FormHarness(TempDir data, ScriptedDialogs? dialogs = null, bool restoreSession = false)
    {
        Dialogs = dialogs ?? new ScriptedDialogs();
        Paths = new AppPaths(System.IO.Path.Combine(data.Path, "appdata"));
        Form = new MainForm(Paths, Dialogs, restoreSession)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false,
        };
        Form.Show();
        Application.DoEvents();
    }

    public MainForm Form { get; }
    public ScriptedDialogs Dialogs { get; }
    public AppPaths Paths { get; }

    public void Dispose()
    {
        Form.Dispose();
    }
}

internal static class TestSetup
{
    /// <summary>UI exceptions fail the test instead of opening WinForms' error dialog on the desktop.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() => Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
}
