using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
using Shouldly;
using Xunit;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UnitTests.Features.Budgeting;

/// <summary>
/// Guards the hand-maintained polymorphic registrations on <see cref="PlanCommand"/>. A missing
/// <see cref="JsonDerivedTypeAttribute"/> passes the build and fails at run time: the serializer
/// throws for an unregistered command and the deserializer rejects its discriminator.
/// </summary>
[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class PlanCommandSerializationContractTests
{
    [Fact]
    public void EveryConcreteCommandIsRegisteredForPolymorphicSerialization()
    {
        var concrete = typeof(PlanCommand).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(PlanCommand).IsAssignableFrom(type))
            .ToArray();
        var registered = typeof(PlanCommand).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.DerivedType)
            .ToArray();

        concrete.ShouldNotBeEmpty();
        registered.ShouldBe(concrete, ignoreOrder: true);
    }

    [Fact]
    public void PolymorphicDiscriminatorsAreUniqueStrings()
    {
        var discriminators = typeof(PlanCommand).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.TypeDiscriminator)
            .ToArray();

        discriminators.ShouldNotBeEmpty();
        discriminators.ShouldAllBe(discriminator => discriminator is string);
        discriminators.ShouldBeUnique();
    }
}
