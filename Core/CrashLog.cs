using System.Globalization;
using System.Text;

namespace NicPad.Core;

/// <summary>Appends exception details (never document contents) to crash.log, capped at 1 MB.</summary>
public static class CrashLog
{
    private const long MaxBytes = 1024 * 1024;

    public static void Write(string path, Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                File.Delete(path);
            }

            var entry = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append(' ')
                .Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message)
                .AppendLine(ex.StackTrace)
                .AppendLine(new string('-', 60))
                .ToString();
            File.AppendAllText(path, entry, Encoding.UTF8);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
