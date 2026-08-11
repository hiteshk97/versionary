using System.Text;

namespace Versionary;

/// <summary>
/// Renders contract types for humans.
/// </summary>
internal static class TypeName
{
    /// <summary>
    /// Renders <paramref name="type"/> at a level of qualification: level 0 is the bare name, each
    /// level after that prepends one more declaring type, and once those run out each further level
    /// prepends one more namespace segment from the right.
    /// </summary>
    /// <remarks>
    /// Declaring types come first because nesting contracts in a per-version static class is the
    /// commoner layout, and it is the cheaper thing to print.
    /// </remarks>
    public static string AtLevel(Type type, int level)
    {
        var nesting = DeclaringDepth(type);

        if (level <= nesting)
        {
            return Nested(type, level);
        }

        var name = Nested(type, nesting);
        if (string.IsNullOrEmpty(type.Namespace))
        {
            return name;
        }

        var segments = type.Namespace!.Split('.');
        var take = Math.Min(level - nesting, segments.Length);

        return $"{string.Join('.', segments[^take..])}.{name}";
    }

    /// <summary>The highest level <see cref="AtLevel"/> can render <paramref name="type"/> at.</summary>
    public static int MaxLevel(Type type)
        => DeclaringDepth(type)
            + (string.IsNullOrEmpty(type.Namespace) ? 0 : type.Namespace!.Split('.').Length);

    /// <summary>
    /// The everyday rendering, for messages that have no wider set of types to disambiguate
    /// against: fully qualified by declaring type, but never by namespace.
    /// </summary>
    public static string Short(Type type) => Nested(type, DeclaringDepth(type));

    private static int DeclaringDepth(Type type)
    {
        var depth = 0;
        for (var declaring = type.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
        {
            depth++;
        }

        return depth;
    }

    private static string Nested(Type type, int levels)
    {
        var name = Bare(type);
        var declaring = type.DeclaringType;

        for (var i = 0; i < levels && declaring is not null; i++, declaring = declaring.DeclaringType)
        {
            name = $"{Bare(declaring)}.{name}";
        }

        return name;
    }

    /// <summary>Strips the arity marker and renders type arguments, so lists read as lists.</summary>
    private static string Bare(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        var builder = new StringBuilder(name).Append('<');
        var arguments = type.GetGenericArguments();

        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Short(arguments[i]));
        }

        return builder.Append('>').ToString();
    }
}
