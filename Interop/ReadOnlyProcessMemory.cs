// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;

namespace Beastdex.Interop;

/// <summary>
/// Copy bounded diagnostic buffers without directly dereferencing an unchecked
/// vector pointer. ReadProcessMemory can report unreadable ranges as failures.
/// This does not make the separate native game API calls immune to game patches.
/// </summary>
internal static class ReadOnlyProcessMemory
{
    public static bool TryCopy(nint address, int length, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        if (length < 0 || length > 4096 || (address == 0 && length != 0))
        {
            error = "Invalid diagnostic memory range.";
            return false;
        }

        if (length == 0)
            return true;

        var buffer = new byte[length];
        if (!ReadProcessMemory(GetCurrentProcess(), address, buffer, (nuint)length, out var copied) ||
            copied != (nuint)length)
        {
            error = $"Could not copy diagnostic memory (Windows error {Marshal.GetLastWin32Error()}).";
            return false;
        }

        bytes = buffer;
        return true;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address,
        [Out] byte[] buffer, nuint size, out nuint bytesRead);
}
