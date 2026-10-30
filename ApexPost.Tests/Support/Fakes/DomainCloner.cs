using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace ApexPost.Tests.Support.Fakes;

/// <summary>
/// Deep copies domain objects field by field (private fields included), so every fake "DbContext
/// scope" works on its own copies, exactly like EF Core materialising fresh entities per context.
/// Also produces a fingerprint used to detect whether a tracked entity was modified.
/// </summary>
internal static class DomainCloner
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static T Clone<T>(T source) where T : class => (T)Clone(source, new Dictionary<object, object>(ReferenceEqualityComparer.Instance))!;

    public static string Fingerprint(object source)
    {
        var builder = new StringBuilder();
        Write(source, builder, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return builder.ToString();
    }

    private static object? Clone(object? source, Dictionary<object, object> seen)
    {
        if (source is null || IsImmutable(source.GetType()))
        {
            return source;
        }

        if (seen.TryGetValue(source, out var existing))
        {
            return existing;
        }

        var type = source.GetType();
        if (source is IList list && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var copy = (IList)Activator.CreateInstance(type)!;
            seen[source] = copy;
            foreach (var item in list)
            {
                copy.Add(Clone(item, seen));
            }

            return copy;
        }

        var clone = RuntimeHelpers.GetUninitializedObject(type);
        seen[source] = clone;
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(Fields | BindingFlags.DeclaredOnly))
            {
                field.SetValue(clone, Clone(field.GetValue(source), seen));
            }
        }

        return clone;
    }

    private static void Write(object? value, StringBuilder builder, HashSet<object> seen)
    {
        if (value is null)
        {
            builder.Append("null;");
            return;
        }

        var type = value.GetType();
        if (IsImmutable(type))
        {
            builder.Append(value).Append(';');
            return;
        }

        if (!seen.Add(value))
        {
            builder.Append("ref;");
            return;
        }

        if (value is IEnumerable items and not string)
        {
            builder.Append('[');
            foreach (var item in items)
            {
                Write(item, builder, seen);
            }

            builder.Append(']');
            return;
        }

        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(Fields | BindingFlags.DeclaredOnly))
            {
                // Navigations to other aggregates are not part of this entity's state.
                if (field.FieldType.Namespace == "ApexPost.Models.Domain" && field.FieldType.IsClass && !field.Name.Contains("_lines", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(field.Name).Append('=');
                Write(field.GetValue(value), builder, seen);
            }
        }
    }

    private static bool IsImmutable(Type type) =>
        type.IsPrimitive || type.IsEnum || type.IsValueType || type == typeof(string)
        || type.Namespace == "ApexPost.Models.ValueObjects"; // value objects (Address is an immutable record)
}
