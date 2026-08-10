// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Bunit;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Shared base for every dashboard component test - identical to <see cref="BunitContext"/> except
/// for a longer <see cref="BunitContext.DefaultWaitTimeout"/>. bUnit's own 1-second default is tight
/// enough that a <c>WaitForAssertion</c> occasionally times out - not because the assertion is wrong,
/// but because the full test suite runs dozens of these test classes' Blazor renderers concurrently
/// (xUnit's default cross-class parallelism, uncustomized here), and under that contention a
/// component's render can legitimately take longer than 1 second of wall-clock time to be dispatched
/// and complete. Confirmed via repeated full-suite runs: the same handful of tests across several of
/// these classes fail intermittently, always with a WaitForAssertion timeout, never in isolation
/// (where there's no contention) and never on the same test twice - a scheduling/contention symptom,
/// not a logic bug in any one test. Every dashboard component test class should derive from this
/// instead of <see cref="BunitContext"/> directly.
/// </summary>
public abstract class DashboardComponentTestContext : BunitContext
{
    protected DashboardComponentTestContext() => DefaultWaitTimeout = TimeSpan.FromSeconds(10);
}
