// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <summary>One process as reported by a Toolhelp32 snapshot walk.</summary>
public readonly record struct ProcessInfo(int ProcessId, int ParentProcessId, string Name);

/// <summary>
/// Spawned-process tree discovery via the Win32 Toolhelp32 snapshot API, per design.md's
/// "Spawned-process tree discovery" decision: one call enumerating every process on the host (there's
/// no per-parent query cheap enough to call once per tracked root), then an in-memory walk down from
/// each tracked run's root PID - this is the same tree <c>Process.Kill(entireProcessTree: true)</c>
/// already tears down internally for the timeout/cancel paths.
/// </summary>
/// <remarks>
/// Originally implemented via WMI's <c>Win32_Process</c> (<c>System.Management</c>/COM interop) -
/// switched to a direct P/Invoke because <see cref="HostResourceSampler"/> calls
/// <see cref="GetAllProcessesAsync"/> every <c>ResourceSamplerIntervalSeconds</c> (default 3s) for
/// the full duration of every in-flight run. WMI/COM's native marshaling layer does not release
/// memory back to the process as promptly as a plain P/Invoke call does, even with every
/// <c>ManagementObjectSearcher</c>/<c>ManagementObjectCollection</c> properly disposed - observed as
/// significant working-set growth on this service's own process (not the managed heap - COM/WMI's
/// native buffers, which <c>Process.WorkingSet64</c> counts too) after a sustained run of several
/// long builds/tests/publishes in a row, each keeping a run tracked (and so this ticking) for
/// minutes at a stretch.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ProcessTree
{
    private const uint Th32csSnapprocess = 0x00000002;
    private const int MaxPath = 260;

    // Struct-embedded fixed-size string marshaling (SzExeFile below) is a classic DllImport
    // scenario the LibraryImport source generator does not support as cleanly - DllImport is the
    // correct, long-established choice here, not a style regression.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessId;
        public UIntPtr Th32DefaultHeapId;
        public uint Th32ModuleId;
        public uint CntThreads;
        public uint Th32ParentProcessId;
        public int PcPriClassBase;
        public uint DwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string SzExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Queries every process currently on the host, keyed by process id.</summary>
    public static Task<IReadOnlyDictionary<int, ProcessInfo>> GetAllProcessesAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var result = new Dictionary<int, ProcessInfo>();

            var snapshot = CreateToolhelp32Snapshot(Th32csSnapprocess, 0);
            if (snapshot == new IntPtr(-1))
            {
                throw new InvalidOperationException(
                    $"CreateToolhelp32Snapshot failed with Win32 error {Marshal.GetLastWin32Error()}.");
            }

            try
            {
                var entry = new ProcessEntry32W { DwSize = (uint)Marshal.SizeOf<ProcessEntry32W>() };

                if (!Process32FirstW(snapshot, ref entry))
                {
                    // An empty/failed-to-start snapshot is not an error worth failing the whole
                    // sample over - the caller already treats "no processes found" the same as
                    // "nothing to report" (see HostResourceSampler.SampleTrackedRunsAsync).
                    return (IReadOnlyDictionary<int, ProcessInfo>)result;
                }

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var processId = (int)entry.Th32ProcessId;
                    result[processId] = new ProcessInfo(processId, (int)entry.Th32ParentProcessId, entry.SzExeFile);
                }
                while (Process32NextW(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return (IReadOnlyDictionary<int, ProcessInfo>)result;
        }, cancellationToken);

    /// <summary>
    /// For each id in <paramref name="rootProcessIds"/>, walks <paramref name="allProcesses"/> to
    /// find every descendant (including the root itself, if still alive) - a root PID that's
    /// already exited by the time this runs is simply omitted from its own tree, not an error.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> DiscoverDescendants(
        IReadOnlyDictionary<int, ProcessInfo> allProcesses,
        IReadOnlyCollection<int> rootProcessIds)
    {
        var childrenByParent = new Dictionary<int, List<int>>();
        foreach (var process in allProcesses.Values)
        {
            if (!childrenByParent.TryGetValue(process.ParentProcessId, out var children))
            {
                children = [];
                childrenByParent[process.ParentProcessId] = children;
            }

            children.Add(process.ProcessId);
        }

        var result = new Dictionary<int, IReadOnlyList<int>>();

        foreach (var rootProcessId in rootProcessIds)
        {
            var tree = new List<int>();
            if (allProcesses.ContainsKey(rootProcessId))
            {
                tree.Add(rootProcessId);
            }

            var queue = new Queue<int>();
            queue.Enqueue(rootProcessId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!childrenByParent.TryGetValue(current, out var children))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    tree.Add(child);
                    queue.Enqueue(child);
                }
            }

            result[rootProcessId] = tree;
        }

        return result;
    }
}
