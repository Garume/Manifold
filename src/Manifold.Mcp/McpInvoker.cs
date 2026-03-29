using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Manifold.Mcp;

public interface IMcpToolInvoker
{
    public bool TryInvoke(
        string toolName,
        JsonElement? arguments,
        IServiceProvider? services,
        CancellationToken cancellationToken,
        out ValueTask<OperationInvocationResult> invocation);
}

public interface IFastMcpToolInvoker
{
    public bool TryInvokeFast(
        string toolName,
        JsonElement? arguments,
        IServiceProvider? services,
        CancellationToken cancellationToken,
        out ValueTask<FastMcpInvocationResult> invocation);
}

public interface IFastSyncMcpToolInvoker
{
    public bool TryInvokeFastSync(
        string toolName,
        JsonElement? arguments,
        IServiceProvider? services,
        CancellationToken cancellationToken,
        out FastMcpInvocationResult invocation);
}

public static partial class McpBinding
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetObject(JsonElement? arguments, out JsonElement value)
    {
        if (arguments is null)
        {
            value = default;
            return false;
        }

        JsonElement element = arguments.Value;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            value = default;
            return false;
        }

        if (element.ValueKind is not JsonValueKind.Object)
            throw new ArgumentException("MCP arguments must be a JSON object.");

        value = element;
        return true;
    }

    public static JsonElement GetRequiredProperty(JsonElement? arguments, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (TryGetProperty(arguments, name, out JsonElement value))
            return value;

        throw new ArgumentException($"Missing required MCP argument '{name}'.");
    }

    public static JsonElement GetRequiredProperty(JsonElement? arguments, ReadOnlySpan<byte> utf8Name, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (TryGetProperty(arguments, utf8Name, out JsonElement value))
            return value;

        throw new ArgumentException($"Missing required MCP argument '{displayName}'.");
    }

    public static bool TryGetProperty(JsonElement? arguments, string name, out JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!TryGetObject(arguments, out JsonElement element))
        {
            value = default;
            return false;
        }

        return element.TryGetProperty(name, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetProperty(JsonElement? arguments, ReadOnlySpan<byte> utf8Name, out JsonElement value)
    {
        if (!TryGetObject(arguments, out JsonElement element))
        {
            value = default;
            return false;
        }

        return element.TryGetProperty(utf8Name, out value);
    }

    public static string ParseString(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ParseBoolean(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException($"The value is not valid for '{displayName}'.")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ParseInt32(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.TryGetInt32(out int parsed))
            return parsed;

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ParseInt64(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.TryGetInt64(out long parsed))
            return parsed;

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ParseDouble(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.TryGetDouble(out double parsed))
            return parsed;

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal ParseDecimal(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.TryGetDecimal(out decimal parsed))
            return parsed;

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }


    public static Guid ParseGuid(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.ValueKind == JsonValueKind.String &&
            Guid.TryParse(value.GetString(), out Guid parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }


    public static Uri ParseUri(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(value.GetString(), UriKind.RelativeOrAbsolute, out Uri? parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }


    public static DateTimeOffset ParseDateTimeOffset(JsonElement value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"The value is not valid for '{displayName}'.");
    }

    public static object? ConvertValue(Type targetType, JsonElement value, string displayName)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (targetType == typeof(string))
            return ParseString(value, displayName);

        Type? nullableUnderlyingType = Nullable.GetUnderlyingType(targetType);
        if (nullableUnderlyingType is not null)
        {
            if (value.ValueKind == JsonValueKind.Null)
                return null;

            return ConvertValue(nullableUnderlyingType, value, displayName);
        }

        if (targetType.IsEnum)
        {
            string text = value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.GetRawText();
            if (Enum.TryParse(targetType, text, ignoreCase: true, out object? enumValue))
                return enumValue;

            throw new ArgumentException($"The value is not valid for '{displayName}'.");
        }

        if (targetType == typeof(bool))
            return ParseBoolean(value, displayName);

        if (targetType == typeof(int))
            return ParseInt32(value, displayName);

        if (targetType == typeof(long))
            return ParseInt64(value, displayName);

        if (targetType == typeof(double))
            return ParseDouble(value, displayName);

        if (targetType == typeof(decimal))
            return ParseDecimal(value, displayName);

        if (targetType == typeof(Guid))
            return ParseGuid(value, displayName);

        if (targetType == typeof(Uri))
            return ParseUri(value, displayName);

        if (targetType == typeof(DateTimeOffset))
            return ParseDateTimeOffset(value, displayName);

        return JsonSerializer.Deserialize(value, targetType);
    }
}
