using System.Text;

namespace NicPad.Core;

/// <summary>
/// Crash-recovery copies of unsaved buffers, one UTF-8 file per tab, named by a random 32-hex id.
/// Ids are validated before they're turned into paths, so a tampered session can't point outside the folder.
/// </summary>
public sealed class BackupStore
{
    public const long MaxBackupBytes = 256L * 1024 * 1024;

    private readonly string folder;

    public BackupStore(string folder)
    {
        this.folder = Path.GetFullPath(folder);
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static bool IsValidId(string? id) =>
        id is { Length: 32 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public void Write(string id, string text)
    {
        TextFileIO.WriteAtomic(PathFor(id), Encoding.UTF8.GetBytes(text ?? ""));
    }

    public string? Read(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path) || new FileInfo(path).Length > MaxBackupBytes)
        {
            return null;
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    public bool Exists(string id) => IsValidId(id) && File.Exists(PathFor(id));

    public void Delete(string id)
    {
        if (!IsValidId(id))
        {
            return;
        }

        try
        {
            File.Delete(PathFor(id));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes orphaned backups older than <paramref name="minAge"/> (only files matching the backup naming pattern).
    /// The age guard protects backups that belong to a second window started with --new-window.
    /// </summary>
    public void DeleteOrphans(IEnumerable<string> keep, TimeSpan minAge)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var keepSet = new HashSet<string>(keep, StringComparer.Ordinal);
        var cutoff = DateTime.UtcNow - minAge;
        foreach (var file in Directory.EnumerateFiles(folder, "*.txt"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (IsValidId(id) && !keepSet.Contains(id) && File.GetLastWriteTimeUtc(file) < cutoff)
            {
                Delete(id);
            }
        }
    }

    private string PathFor(string id)
    {
        if (!IsValidId(id))
        {
            throw new ArgumentException("Invalid backup id.", nameof(id));
        }

        return Path.Combine(folder, id + ".txt");
    }
}
