using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Endpoints;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class IdentityEndpointExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappingPreservesIdentityRoutesMethodsAuthorizationAndConventionsAsync(bool explicitStaticCall)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(IdentityEndpointExtensionsTests).Assembly.GetName().Name,
        });
        await using var app = builder.Build();
        var marker = new object();

        var conventions = explicitStaticCall
            ? IdentityComponentsEndpointRouteBuilderExtensions.MapAdditionalIdentityEndpoints(endpoints: app)
            : app.MapAdditionalIdentityEndpoints();
        conventions.WithMetadata(marker);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        endpoints.Select(endpoint => endpoint.RoutePattern.RawText).Order(StringComparer.Ordinal).ShouldBe(
        [
            "/Account/Logout",
            "/Account/Manage/DownloadPersonalData",
            "/Account/Manage/LinkExternalLogin",
            "/Account/PasskeyCreationOptions",
            "/Account/PasskeyRequestOptions",
            "/Account/PerformExternalLogin",
        ]);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>().ShouldNotBeNull().HttpMethods.ShouldBe([HttpMethods.Post]);
            endpoint.Metadata.ShouldContain(marker);
            var requiresAuthorization = endpoint.RoutePattern.RawText!.StartsWith("/Account/Manage/", StringComparison.Ordinal);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any().ShouldBe(requiresAuthorization);
        }
    }

    [Fact]
    public void MappingNullEndpointsRetainsArgumentGuard() =>
        Should.Throw<ArgumentNullException>(() => IdentityComponentsEndpointRouteBuilderExtensions.MapAdditionalIdentityEndpoints(null!))
            .ShouldBeOfType<ArgumentNullException>().ParamName.ShouldBe("endpoints");
}
