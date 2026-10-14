using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Slices;
using ArchUnitNET.Loader;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using YHAB.Data;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace YHAB.UnitTests.Architecture;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class ServerDependencyTests
{
    private const string FeatureNamespaces = @"^YHAB\.Features\.[^.]+(\.|$)";
    private const string ContractNamespaces = @"^YHAB\.Features\.[^.]+\.Contracts(\.|$)";
    private static readonly System.Reflection.Assembly _server = typeof(ApplicationDbContext).Assembly;
    private static readonly ArchUnitNET.Domain.Architecture _architecture = new ArchLoader()
        .LoadAssemblies(_server)
        .Build();

    [Fact]
    public void DatabaseAccessStaysInServicesDataAndStartup()
    {
        var consumers = Types().That().ResideInAssembly(_server)
            .And().DoNotResideInNamespaceMatching(
                @"^YHAB\.(Data|Migrations|Features\.[^.]+\.(Services|Data))(\.|$)")
            .And().AreNot(_server.EntryPoint!.DeclaringType!);

        AssertNoDatabaseDependencies(consumers,
            "Database access belongs in feature Services/Data, shared Data, migrations, or Program startup/readiness wiring.");
    }

    [Fact]
    public void ComponentsDoNotAccessDatabase()
    {
        var components = Types().That().ResideInAssembly(_server)
            .And().AreAssignableTo(typeof(IComponent));

        AssertNoDatabaseDependencies(components,
            "Components call application services; moving a component into Services does not permit direct database access.");
    }

    [Fact]
    public void EndpointsDoNotAccessDatabase()
    {
        var endpoints = Types().That().ResideInNamespaceMatching(@"^YHAB\.Features\.[^.]+\.Endpoints(\.|$)");

        AssertNoDatabaseDependencies(endpoints,
            "Feature HTTP endpoints delegate database work to application services.");
    }

    [Fact]
    public void FeaturesOnlyDependOnOtherFeaturesThroughContracts()
    {
        var features = Types().That().ResideInNamespaceMatching(FeatureNamespaces)
            .GetObjects(_architecture)
            .Select(type => type.Namespace.FullName.Split('.')[2])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        features.ShouldNotBeEmpty("The server must contain feature types for these rules to protect.");

        var implementations = Types().That().ResideInNamespaceMatching(FeatureNamespaces)
            .And().DoNotResideInNamespaceMatching(ContractNamespaces);
        foreach (var feature in features)
        {
            var featureNamespace = $@"^YHAB\.Features\.{Regex.Escape(feature)}(\.|$)";
            var subjects = Types().That().ResideInNamespaceMatching(featureNamespace);
            var otherImplementations = Types().That().Are(implementations)
                .And().DoNotResideInNamespaceMatching(featureNamespace);
            AssertNoDependencies(subjects, otherImplementations,
                $"{feature} may use another feature only through its Contracts namespace.");

            // Contracts are introduced only for real consumers; none are required for isolated features.
            var contracts = Types().That().ResideInNamespaceMatching(
                $@"^YHAB\.Features\.{Regex.Escape(feature)}\.Contracts(\.|$)");
            if (contracts.GetObjects(_architecture).Any())
            {
                AssertNoDependencies(contracts, implementations,
                    $"{feature} contracts must not expose or depend on any feature's implementation, including their own.");
            }
        }
    }

    [Fact]
    public void ServerFeaturesDoNotFormDependencyCycles()
    {
        // The built-in pattern matcher retains nested namespaces in this package version.
        // Assign the entire feature to one slice so Models/Services cycles stay internal.
        var creator = new SliceRuleCreator();
        creator.SetSliceAssignment(new SliceAssignment(
            type => type.Namespace.FullName.StartsWith("YHAB.Features.", StringComparison.Ordinal)
                ? SliceIdentifier.Of(type.Namespace.FullName.Split('.')[2])
                : SliceIdentifier.Ignore(),
            "top-level server features"));
        var features = new GivenSlices(creator);
        features.GetObjects(_architecture).ShouldNotBeEmpty("Top-level server features must be discovered.");

        var rule = features.Should().BeFreeOfCycles();
        rule.Evaluate(_architecture)
            .Where(result => !result.Passed)
            .Select(result => result.Description)
            .ShouldBeEmpty("Dependencies between top-level server features, including contracts, must be acyclic; internal namespace cycles are allowed.");
    }

    private static void AssertNoDatabaseDependencies(IObjectProvider<IType> subjects, string reason)
    {
        var database = Types(includeReferenced: true).That().ResideInNamespaceMatching(
                @"^(Microsoft\.EntityFrameworkCore|Npgsql|System\.Data|Microsoft\.Data\.SqlClient)(\.|$)")
            .Or().AreAssignableTo(typeof(DbContext))
            // Identity's intermediate base class is a referenced stub in the server-only model.
            .Or().Are(typeof(ApplicationDbContext));
        AssertNoDependencies(subjects, database, reason);
    }

    private static void AssertNoDependencies(IObjectProvider<IType> subjects, IObjectProvider<IType> forbidden, string reason)
    {
        subjects.GetObjects(_architecture).ShouldNotBeEmpty(reason);
        var rule = Types().That().Are(subjects).Should().NotDependOnAny(forbidden).Because(reason);
        rule.Evaluate(_architecture)
            .Where(result => !result.Passed)
            .Select(result => result.Description)
            .ShouldBeEmpty(reason);
    }
}
