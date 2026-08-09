// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// One lane transition drawn within a single commit row - a line from lane <see cref="FromLane"/>
/// at the top of that row's height to lane <see cref="ToLane"/> at its bottom. <see cref="FromLane"/>
/// equal to <see cref="ToLane"/> is a straight vertical segment (an unrelated lane simply passing
/// through the row, or a commit's own lineage continuing unchanged); unequal values are a diagonal
/// segment (a branch converging into or diverging from the commit's own lane).
/// </summary>
public sealed record CommitGraphConnector(int FromLane, int ToLane);

/// <summary>
/// One row of the commit graph - a single commit's lane assignment plus the lane line segments drawn
/// within its row.
/// </summary>
/// <param name="Hash">This row's commit hash.</param>
/// <param name="Lane">The lane this commit is drawn in.</param>
/// <param name="Connectors">The lane line segments drawn within this row.</param>
/// <param name="IsNewLane">
/// Whether nothing above this row was awaiting this commit's hash when it was reached - true for a
/// lane's own first appearance (nothing above depends on it, so its self-connector line, if any,
/// should only extend downward from its dot, not up into the row above).
/// </param>
public sealed record CommitGraphRow(string Hash, int Lane, IReadOnlyList<CommitGraphConnector> Connectors, bool IsNewLane);

/// <summary>The computed graph for a page of commit history - every row plus the total number of lanes used, so a renderer knows how wide to make the graph column.</summary>
public sealed record CommitGraphResult(IReadOnlyList<CommitGraphRow> Rows, int LaneCount);

/// <summary>
/// Assigns each commit in an ordered (parents-after-children, e.g. <c>git log --topo-order</c>)
/// list of commits a lane, and computes the connector lines between lanes row to row - the standard
/// "active lanes each await a parent hash" algorithm real git GUI tools use for their commit graphs
/// (per design.md's "Commit graph" decision), not a parser of <c>git log --graph</c>'s own ASCII-art
/// output. Pure computation over already-known data, no IO - belongs in Domain the same way
/// <see cref="PathConfinement.PathConfinementPolicy"/> does.
/// </summary>
public static class CommitGraphLayout
{
    /// <summary>Computes lane assignments and connectors for <paramref name="commits"/>, in the order given.</summary>
    public static CommitGraphResult Compute(IReadOnlyList<CommitSummary> commits)
    {
        var activeLanes = new List<string?>();
        var rows = new List<CommitGraphRow>(commits.Count);
        var maxLaneIndex = -1;

        foreach (var commit in commits)
        {
            var connectors = new List<CommitGraphConnector>();

            // Every lane currently awaiting this commit's hash - normally at most one, but more
            // than one when two branches independently trace back to the same ancestor before that
            // ancestor has been reached (a graph "join" point going backward in time).
            var awaitingLanes = new List<int>();
            for (var i = 0; i < activeLanes.Count; i++)
            {
                if (activeLanes[i] == commit.Hash)
                {
                    awaitingLanes.Add(i);
                }
            }

            var isNewLane = awaitingLanes.Count == 0;
            int lane;
            if (!isNewLane)
            {
                lane = awaitingLanes[0];
            }
            else
            {
                lane = activeLanes.IndexOf(null);
                if (lane < 0)
                {
                    lane = activeLanes.Count;
                    activeLanes.Add(null);
                }
            }

            // Every other active lane either converges into this commit's lane (if it was also
            // awaiting this hash) or passes straight through, untouched by this commit.
            for (var i = 0; i < activeLanes.Count; i++)
            {
                if (i == lane || activeLanes[i] is null)
                {
                    continue;
                }

                if (awaitingLanes.Contains(i))
                {
                    connectors.Add(new CommitGraphConnector(i, lane));
                    activeLanes[i] = null;
                }
                else
                {
                    connectors.Add(new CommitGraphConnector(i, i));
                }
            }

            // The commit's own lane: a line is drawn through this row if it either arrived here
            // (was awaited) or continues past here (has a first parent still to reach) - both, for
            // an ordinary single-parent commit in the middle of a lineage; neither only for a
            // genuinely isolated single commit (no history either side, within the loaded window).
            var firstParent = commit.ParentHashes.Count > 0 ? commit.ParentHashes[0] : null;
            if (!isNewLane || firstParent is not null)
            {
                connectors.Add(new CommitGraphConnector(lane, lane));
            }

            activeLanes[lane] = firstParent;

            // Additional parents (a merge commit) branch out into new or freed lanes, each awaiting
            // its own parent hash from here on.
            for (var parentIndex = 1; parentIndex < commit.ParentHashes.Count; parentIndex++)
            {
                var newLane = activeLanes.IndexOf(null);
                if (newLane < 0)
                {
                    newLane = activeLanes.Count;
                    activeLanes.Add(null);
                }

                activeLanes[newLane] = commit.ParentHashes[parentIndex];
                connectors.Add(new CommitGraphConnector(lane, newLane));
                maxLaneIndex = Math.Max(maxLaneIndex, newLane);
            }

            maxLaneIndex = Math.Max(maxLaneIndex, lane);
            rows.Add(new CommitGraphRow(commit.Hash, lane, connectors, isNewLane));
        }

        return new CommitGraphResult(rows, maxLaneIndex + 1);
    }
}
