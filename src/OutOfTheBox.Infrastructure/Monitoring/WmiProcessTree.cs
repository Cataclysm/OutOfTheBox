// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <summary>One process as reported by a <c>Win32_Process</c> WMI query.</summary>
public readonly record struct WmiProcessInfo(int ProcessId, int ParentProcessId, string Name, DateTime CreationTime);

/// <summary>
/// Spawned-process tree discovery via WMI <c>Win32_Process</c>, per design.md's "Spawned-process
/// tree discovery" decision: one query enumerating every process on the host (there's no
/// per-parent WMI query cheap enough to call once per tracked root), then an in-memory walk down
/// from each tracked run's root PID - this is the same tree <c>Process.Kill(entireProcessTree: true)</c>
/// already tears down internally for the timeout/cancel paths.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WmiProcessTree
{
    /// <summary>Queries every process currently on the host, keyed by process id.</summary>
    public static Task<IReadOnlyDictionary<int, WmiProcessInfo>> GetAllProcessesAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var result = new Dictionary<int, WmiProcessInfo>();

            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, CreationDate FROM Win32_Process");
            using var results = searcher.Get();

            foreach (var raw in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var process = raw;
                var processId = Convert.ToInt32(process["ProcessId"], CultureInfo.InvariantCulture);
                var parentProcessId = process["ParentProcessId"] is null ? 0 : Convert.ToInt32(process["ParentProcessId"], CultureInfo.InvariantCulture);
                var name = process["Name"] as string ?? string.Empty;
                var creationTime = process["CreationDate"] is string creationDate
                    ? ManagementDateTimeConverter.ToDateTime(creationDate)
                    : DateTime.MinValue;

                result[processId] = new WmiProcessInfo(processId, parentProcessId, name, creationTime);
            }

            return (IReadOnlyDictionary<int, WmiProcessInfo>)result;
        }, cancellationToken);

    /// <summary>
    /// For each id in <paramref name="rootProcessIds"/>, walks <paramref name="allProcesses"/> to
    /// find every descendant (including the root itself, if still alive) - a root PID that's
    /// already exited by the time this runs is simply omitted from its own tree, not an error.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> DiscoverDescendants(
        IReadOnlyDictionary<int, WmiProcessInfo> allProcesses,
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
