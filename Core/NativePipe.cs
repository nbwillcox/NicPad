using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NicPad.Core;

internal static class NativePipe
{
    public static bool TryGetServerProcessId(NamedPipeClientStream client, out int processId)
    {
        processId = 0;
        if (NativeMethods.GetNamedPipeServerProcessId(client.SafePipeHandle, out var pid))
        {
            processId = (int)pid;
            return true;
        }

        return false;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    }
}
