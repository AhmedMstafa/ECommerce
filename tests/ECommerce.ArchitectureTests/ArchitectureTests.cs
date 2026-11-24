using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace ECommerce.ArchitectureTests;

public class ArchitectureTests
{
    private const string DomainNamespace = "ECommerce.Domain";
    private const string UseCasesNamespace = "ECommerce.UseCases";
    private const string InfrastructureNamespace = "ECommerce.Infrastructure";
    private const string ApiNamespace = "ECommerce.API";

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_OtherProjects()
    {
        var assembly = typeof(Domain.Entities.Product).Assembly;

        var otherProjects = new[]
        {
            UseCasesNamespace,
            InfrastructureNamespace,
            ApiNamespace
        };

        var testResult = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(otherProjects)
            .GetResult();

        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void UseCases_Should_Not_HaveDependencyOn_InfrastructureOrApi()
    {
        var assembly = typeof(UseCases.DependancyInjection).Assembly;

        var forbiddenProjects = new[]
        {
            InfrastructureNamespace,
            ApiNamespace
        };

        var testResult = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenProjects)
            .GetResult();

        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_Should_Not_HaveDependencyOn_Api()
    {
        var assembly = typeof(Infrastructure.DependancyInjection).Assembly;

        var testResult = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        testResult.IsSuccessful.Should().BeTrue();
    }
}
