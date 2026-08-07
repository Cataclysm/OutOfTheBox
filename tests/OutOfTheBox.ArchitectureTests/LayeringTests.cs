// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Reflection;
using NetArchTest.Rules;

namespace OutOfTheBox.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture dependency rule from design.md: Domain has no outward
/// dependency at all; Application depends only on Domain; Infrastructure and Presentation are
/// independent outer-ring slices that never reference each other; Host (the composition root) is
/// deliberately unconstrained and excluded from every rule here.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("OutOfTheBox.Domain");
    private static readonly Assembly ApplicationAssembly = Assembly.Load("OutOfTheBox.Application");
    private static readonly Assembly InfrastructureAssembly = Assembly.Load("OutOfTheBox.Infrastructure");
    private static readonly Assembly PresentationAssembly = Assembly.Load("OutOfTheBox.Presentation");

    [Fact]
    public void Domain_has_no_dependency_on_other_projects()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "OutOfTheBox.Application",
                "OutOfTheBox.Infrastructure",
                "OutOfTheBox.Presentation",
                "OutOfTheBox.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_has_no_dependency_on_framework_namespaces()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "System.Management",
                "System.Diagnostics.PerformanceCounter")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_has_no_dependency_on_infrastructure_or_presentation()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "OutOfTheBox.Infrastructure",
                "OutOfTheBox.Presentation",
                "OutOfTheBox.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_has_no_dependency_on_presentation()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "OutOfTheBox.Presentation",
                "OutOfTheBox.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Presentation_has_no_dependency_on_infrastructure()
    {
        var result = Types.InAssembly(PresentationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "OutOfTheBox.Infrastructure",
                "OutOfTheBox.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null
            ? "no failing type details available"
            : string.Join(", ", result.FailingTypeNames);
}
