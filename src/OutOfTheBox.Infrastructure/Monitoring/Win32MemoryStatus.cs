// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <summary>
/// Host RAM via <c>GlobalMemoryStatusEx</c>, per design.md's "Host RAM" decision - a single Win32
/// call returning total/available physical memory directly, cheaper and more immediate than a
/// <c>PerformanceCounter</c> (no discard-first-sample quirk).
/// </summary>
[SupportedOSPlatform("windows")]
public static class Win32MemoryStatus
{
    /// <summary>Returns (TotalPhysicalBytes, AvailablePhysicalBytes) for the host.</summary>
    public static (long TotalBytes, long AvailableBytes) GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException($"GlobalMemoryStatusEx failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        return ((long)status.ullTotalPhys, (long)status.ullAvailPhys);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
