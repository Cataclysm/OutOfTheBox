using System.Reflection;
using NetArchTest.Rules;

namespace BuildAndTestService.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture dependency rule from design.md: Domain has no outward
/// dependency at all; Application depends only on Domain; Infrastructure and Presentation are
/// independent outer-ring slices that never reference each other; Host (the composition root) is
/// deliberately unconstrained and excluded from every rule here.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("BuildAndTestService.Domain");
    private static readonly Assembly ApplicationAssembly = Assembly.Load("BuildAndTestService.Application");
    private static readonly Assembly InfrastructureAssembly = Assembly.Load("BuildAndTestService.Infrastructure");
    private static readonly Assembly PresentationAssembly = Assembly.Load("BuildAndTestService.Presentation");

    [Fact]
    public void Domain_has_no_dependency_on_other_projects()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "BuildAndTestService.Application",
                "BuildAndTestService.Infrastructure",
                "BuildAndTestService.Presentation",
                "BuildAndTestService.Host")
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
                "BuildAndTestService.Infrastructure",
                "BuildAndTestService.Presentation",
                "BuildAndTestService.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_has_no_dependency_on_presentation()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "BuildAndTestService.Presentation",
                "BuildAndTestService.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Presentation_has_no_dependency_on_infrastructure()
    {
        var result = Types.InAssembly(PresentationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "BuildAndTestService.Infrastructure",
                "BuildAndTestService.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null
            ? "no failing type details available"
            : string.Join(", ", result.FailingTypeNames);
}
