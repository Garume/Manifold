namespace Manifold;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class OptionAttribute(string name) : Attribute
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Option name must be non-empty.", nameof(name))
        : name.Trim();

    public string? Description { get; set; }

    public bool Required { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ArgumentAttribute(int position) : Attribute
{
    public int Position { get; } = position < 0
        ? throw new ArgumentOutOfRangeException(nameof(position), "Argument position must be zero or greater.")
        : position;

    public string? Name { get; set; }

    public string? Description { get; set; }

    public bool Required { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class AliasAttribute(params string[] aliases) : Attribute
{
    public string[] Aliases { get; } = Normalize(aliases);

    private static string[] Normalize(string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        string[] normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0
            ? throw new ArgumentException("At least one alias is required.", nameof(values))
            : normalized;
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CliNameAttribute(string name) : Attribute
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("CLI name must be non-empty.", nameof(name))
        : name.Trim();
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class McpNameAttribute(string name) : Attribute
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("MCP name must be non-empty.", nameof(name))
        : name.Trim();
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CliCommandAttribute(params string[] pathSegments) : Attribute
{
    public string[] PathSegments { get; } = pathSegments
        .Where(static segment => !string.IsNullOrWhiteSpace(segment))
        .Select(static segment => segment.Trim())
        .ToArray() is { Length: > 0 } normalized
            ? normalized
            : throw new ArgumentException("At least one CLI path segment is required.", nameof(pathSegments));
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpToolAttribute(string name) : Attribute
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("MCP tool name must be non-empty.", nameof(name))
        : name.Trim();
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CliOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromServicesAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ResultFormatterAttribute(Type formatterType) : Attribute
{
    public Type FormatterType { get; } = formatterType ?? throw new ArgumentNullException(nameof(formatterType));
}


