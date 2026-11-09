using System.Reflection;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>Domain and application types must not reference any Couchbase type (layering rule of Part A).</summary>
public sealed class DomainPurityTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void Domain_and_application_types_do_not_reference_Couchbase_types()
    {
        var assembly = typeof(ShipmentScan).Assembly;
        var guarded = assembly.GetTypes()
            .Where(t => t.Namespace is "ApexPost.Courier.Offline.Domain" or "ApexPost.Courier.Offline.Application")
            .ToList();

        Assert.NotEmpty(guarded);

        var offenders = guarded
            .SelectMany(t => ReferencedTypes(t).Where(IsCouchbase).Select(r => $"{t.FullName} -> {r.FullName}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_infrastructure_namespace_is_the_only_place_that_uses_Couchbase()
    {
        var assembly = typeof(ShipmentScan).Assembly;
        var usesCouchbase = assembly.GetTypes()
            .Where(t => ReferencedTypes(t).Any(IsCouchbase))
            .Select(t => t.Namespace)
            .Distinct()
            .ToList();

        Assert.All(usesCouchbase, ns => Assert.Equal("ApexPost.Courier.Offline.Infrastructure.Couchbase", ns));
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return contract;
        }

        foreach (var field in type.GetFields(AllDeclared))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(AllDeclared))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(AllDeclared))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var constructor in type.GetConstructors(AllDeclared))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static bool IsCouchbase(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("Couchbase", StringComparison.Ordinal) == true
        || type.GetGenericArguments().Any(IsCouchbase)
        || (type.HasElementType && type.GetElementType() is { } element && IsCouchbase(element));
}
