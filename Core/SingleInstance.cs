using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace NicPad.Core;

/// <summary>
/// One NicPad window per user + data folder. A second launch sends its file list to the first over a
/// named pipe that only the same Windows user can connect to (<see cref="PipeOptions.CurrentUserOnly"/>).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const int MaxMessageBytes = 256 * 1024;
    public const int MaxFiles = 50;

    private readonly Mutex mutex;
    private readonly CancellationTokenSource cts = new();
    private bool disposed;

    private SingleInstance(string name, Mutex mutex, bool isPrimary)
    {
        Name = name;
        this.mutex = mutex;
        IsPrimary = isPrimary;
    }

    public string Name { get; }
    public bool IsPrimary { get; }

    public static SingleInstance Acquire(string instanceKey)
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var sid = identity.User?.Value ?? "user";
        var name = $"NicPad-{sid}-{process.SessionId}-{instanceKey}";
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out var createdNew);
        return new SingleInstance(name, mutex, createdNew);
    }

    /// <summary>Hands files to the primary instance. Returns false if it doesn't answer, so the caller can start normally.</summary>
    public bool TrySendToPrimary(IReadOnlyList<string> files, Action<int>? onServerProcessId = null, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            if (onServerProcessId is not null && NativePipe.TryGetServerProcessId(client, out var pid))
            {
                onServerProcessId(pid);
            }

            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files.Take(MaxFiles).ToArray()));
            if (payload.Length > MaxMessageBytes)
            {
                return false;
            }

            client.Write(payload);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Starts listening on a background task; <paramref name="onFiles"/> runs on that task.</summary>
    public void Listen(Action<IReadOnlyList<string>> onFiles)
    {
        ArgumentNullException.ThrowIfNull(onFiles);
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);

                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while ((read = await server.ReadAsync(chunk, cts.Token).ConfigureAwait(false)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                        if (buffer.Length > MaxMessageBytes)
                        {
                            break;
                        }
                    }

                    if (buffer.Length <= MaxMessageBytes)
                    {
                        onFiles(ParseMessage(buffer.ToArray()));
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }
        });
    }

    /// <summary>Validates an incoming message: a JSON array of absolute paths.</summary>
    public static IReadOnlyList<string> ParseMessage(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var files = JsonSerializer.Deserialize<string[]>(payload) ?? [];
        return files
            .Where(f => !string.IsNullOrWhiteSpace(f) && f.Length <= 1024 && Path.IsPathFullyQualified(f))
            .Take(MaxFiles)
            .ToList();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cts.Cancel();
        cts.Dispose();
        if (IsPrimary)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        mutex.Dispose();
    }
}
