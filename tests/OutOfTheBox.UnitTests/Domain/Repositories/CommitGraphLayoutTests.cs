// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.UnitTests.Domain.Repositories;

/// <summary>
/// Exhaustive coverage of <see cref="CommitGraphLayout.Compute"/> - pure, deterministic, and cheap,
/// so every canonical shape (linear history, a diverging branch, a merge) is checked exactly, not
/// just smoke-tested. Expected lane/connector values below were hand-derived by walking the
/// algorithm's own "active lanes await a parent hash" rule for each fixture.
/// </summary>
public sealed class CommitGraphLayoutTests
{
    [Fact]
    public void Empty_history_produces_no_rows_and_zero_lanes()
    {
        var result = CommitGraphLayout.Compute([]);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.LaneCount);
    }

    [Fact]
    public void Linear_history_stays_on_a_single_lane()
    {
        // C -> B -> A (A is the root, no parents), newest first.
        var commits = new[]
        {
            Commit("C", ["B"]),
            Commit("B", ["A"]),
            Commit("A", []),
        };

        var result = CommitGraphLayout.Compute(commits);

        Assert.Equal(1, result.LaneCount);
        Assert.Equal(3, result.Rows.Count);

        Assert.All(result.Rows, row => Assert.Equal(0, row.Lane));
        Assert.All(result.Rows, row => Assert.Equal([new CommitGraphConnector(0, 0)], row.Connectors));
    }

    [Fact]
    public void Two_branch_tips_converge_at_their_common_ancestor()
    {
        // D and C both have B as their parent (a fork point); B's parent is the root A.
        var commits = new[]
        {
            Commit("D", ["B"]),
            Commit("C", ["B"]),
            Commit("B", ["A"]),
            Commit("A", []),
        };

        var result = CommitGraphLayout.Compute(commits);

        Assert.Equal(2, result.LaneCount);

        var d = result.Rows[0];
        Assert.Equal(0, d.Lane);
        Assert.Equal([new CommitGraphConnector(0, 0)], d.Connectors);

        var c = result.Rows[1];
        Assert.Equal(1, c.Lane);
        Assert.Equal([new CommitGraphConnector(0, 0), new CommitGraphConnector(1, 1)], c.Connectors);

        var b = result.Rows[2];
        Assert.Equal(0, b.Lane);
        Assert.Equal([new CommitGraphConnector(1, 0), new CommitGraphConnector(0, 0)], b.Connectors);

        var a = result.Rows[3];
        Assert.Equal(0, a.Lane);
        Assert.Equal([new CommitGraphConnector(0, 0)], a.Connectors);
    }

    [Fact]
    public void A_merge_commit_branches_out_then_both_parents_reconverge()
    {
        // M merges P1 and P2; both P1 and P2 independently trace back to the same ancestor X.
        var commits = new[]
        {
            Commit("M", ["P1", "P2"]),
            Commit("P1", ["X"]),
            Commit("P2", ["X"]),
            Commit("X", []),
        };

        var result = CommitGraphLayout.Compute(commits);

        Assert.Equal(2, result.LaneCount);

        var m = result.Rows[0];
        Assert.Equal(0, m.Lane);
        Assert.Equal([new CommitGraphConnector(0, 0), new CommitGraphConnector(0, 1)], m.Connectors);

        var p1 = result.Rows[1];
        Assert.Equal(0, p1.Lane);
        Assert.Equal([new CommitGraphConnector(1, 1), new CommitGraphConnector(0, 0)], p1.Connectors);

        var p2 = result.Rows[2];
        Assert.Equal(1, p2.Lane);
        Assert.Equal([new CommitGraphConnector(0, 0), new CommitGraphConnector(1, 1)], p2.Connectors);

        var x = result.Rows[3];
        Assert.Equal(0, x.Lane);
        Assert.Equal([new CommitGraphConnector(1, 0), new CommitGraphConnector(0, 0)], x.Connectors);
    }

    [Fact]
    public void An_isolated_single_commit_gets_a_dot_with_no_connectors()
    {
        var result = CommitGraphLayout.Compute([Commit("A", [])]);

        var row = Assert.Single(result.Rows);
        Assert.Equal(0, row.Lane);
        Assert.Empty(row.Connectors);
        Assert.Equal(1, result.LaneCount);
    }

    private static CommitSummary Commit(string hash, IReadOnlyList<string> parents) =>
        new(hash, hash, parents, "Author", DateTimeOffset.UtcNow, "Subject", []);
}
