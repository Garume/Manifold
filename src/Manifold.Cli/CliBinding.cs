using System.Globalization;
using System.Runtime.CompilerServices;

namespace Manifold.Cli;

public static class CliBinding
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReservedGlobalFlag(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length != 6 ||
            token[0] != '-' ||
            token[1] != '-')
        {
            return false;
        }

        return MatchesFlag(token, 'j', 's', 'o', 'n') ||
               MatchesFlag(token, 'h', 'e', 'l', 'p');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ContainsReservedGlobalFlag(IReadOnlyList<string> commandTokens, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(commandTokens);

        if (commandTokens is string[] array)
            return ContainsReservedGlobalFlag(array, startIndex);

        for (int index = startIndex; index < commandTokens.Count; index++)
        {
            if (IsReservedGlobalFlag(commandTokens[index]))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ContainsReservedGlobalFlag(string[] commandTokens, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(commandTokens);

        for (int index = startIndex; index < commandTokens.Length; index++)
        {
            if (IsReservedGlobalFlag(commandTokens[index]))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ParseRequiredOptionValue(
        IReadOnlyList<string> commandTokens,
        ref int index,
        string optionName)
    {
        ArgumentNullException.ThrowIfNull(commandTokens);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);

        if (commandTokens is string[] array)
            return ParseRequiredOptionValue(array, ref index, optionName);

        if (++index >= commandTokens.Count ||
            IsLongOptionToken(commandTokens[index]) ||
            IsAllWhiteSpace(commandTokens[index]))
        {
            throw new ArgumentException($"The --{optionName} option requires a non-empty value.");
        }

        return commandTokens[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ParseRequiredOptionValue(
        string[] commandTokens,
        ref int index,
        string optionName)
    {
        ArgumentNullException.ThrowIfNull(commandTokens);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);

        if (++index >= commandTokens.Length ||
            IsLongOptionToken(commandTokens[index]) ||
            IsAllWhiteSpace(commandTokens[index]))
        {
            throw new ArgumentException($"The --{optionName} option requires a non-empty value.");
        }

        return commandTokens[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ParseString(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return text;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ParseBoolean(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (bool.TryParse(text, out bool parsedBool))
            return parsedBool;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ParseInt32(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
            return parsedInt;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ParseInt64(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedLong))
            return parsedLong;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ParseDouble(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsedDouble))
            return parsedDouble;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal ParseDecimal(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedDecimal))
            return parsedDecimal;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ParseGuid(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (Guid.TryParse(text, out Guid parsedGuid))
            return parsedGuid;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Uri ParseUri(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out Uri? parsedUri))
            return parsedUri;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset ParseDateTimeOffset(string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsedDateTimeOffset))
            return parsedDateTimeOffset;

        throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
    }

    public static string GetRequiredArgument(
        IReadOnlyList<string> arguments,
        int position,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (position >= 0 && position < arguments.Count && !string.IsNullOrWhiteSpace(arguments[position]))
            return arguments[position];

        throw new ArgumentException($"Missing required argument '{displayName}'.");
    }

    public static bool TryFindOptionValue(
        IReadOnlyDictionary<string, string> options,
        string name,
        IReadOnlyList<string>? aliases,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (options.TryGetValue(name, out value))
            return true;

        if (aliases is not null)
        {
            foreach (string alias in aliases)
            {
                if (options.TryGetValue(alias, out value))
                    return true;
            }
        }

        value = null;
        return false;
    }

    public static TService GetRequiredService<TService>(IServiceProvider? services)
        where TService : class
    {
        return (TService)GetRequiredService(services, typeof(TService));
    }

    public static object GetRequiredService(IServiceProvider? services, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        object? service = services?.GetService(serviceType);
        if (service is not null)
            return service;

        throw new InvalidOperationException($"Required service '{serviceType.FullName}' was not available.");
    }

    public static TService GetRequiredServiceOrThrow<TService>(IServiceProvider? services)
        where TService : class
    {
        object? service = services?.GetService(typeof(TService));
        if (service is TService typedService)
            return typedService;

        throw new InvalidOperationException($"Required service '{typeof(TService).FullName}' was not available.");
    }

    public static object? ConvertValue(Type targetType, string text, string displayName)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (targetType == typeof(string))
            return ParseString(text, displayName);

        Type? nullableUnderlyingType = Nullable.GetUnderlyingType(targetType);
        if (nullableUnderlyingType is not null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return ConvertValue(nullableUnderlyingType, text, displayName);
        }

        if (targetType.IsArray)
        {
            Type elementType = targetType.GetElementType()
                               ?? throw new InvalidOperationException($"Array type '{targetType.FullName}' was missing an element type.");
            string[] segments = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Array values = Array.CreateInstance(elementType, segments.Length);
            for (int index = 0; index < segments.Length; index++)
                values.SetValue(ConvertValue(elementType, segments[index], displayName), index);

            return values;
        }

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, text, ignoreCase: true, out object? enumValue))
                return enumValue;

            throw new ArgumentException($"The value '{text}' is not valid for '{displayName}'.");
        }

        if (targetType == typeof(bool))
            return ParseBoolean(text, displayName);

        if (targetType == typeof(int))
            return ParseInt32(text, displayName);

        if (targetType == typeof(long))
            return ParseInt64(text, displayName);

        if (targetType == typeof(double))
            return ParseDouble(text, displayName);

        if (targetType == typeof(decimal))
            return ParseDecimal(text, displayName);

        if (targetType == typeof(Guid))
            return ParseGuid(text, displayName);

        if (targetType == typeof(Uri))
            return ParseUri(text, displayName);

        if (targetType == typeof(DateTimeOffset))
            return ParseDateTimeOffset(text, displayName);

        throw new InvalidOperationException($"The CLI runtime does not support parameter type '{targetType.FullName}'.");
    }

    public static string? FormatDefaultText<T>(T value)
    {
        if (value is null)
            return null;

        if (value is string text)
            return text;

        return value.ToString();
    }

    public static string? FormatDefaultText(object? value)
    {
        return FormatDefaultText<object?>(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesFlag(string token, char first, char second, char third, char fourth)
    {
        return AsciiEqualsIgnoreCase(token[2], first) &&
               AsciiEqualsIgnoreCase(token[3], second) &&
               AsciiEqualsIgnoreCase(token[4], third) &&
               AsciiEqualsIgnoreCase(token[5], fourth);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLongOptionToken(string token)
    {
        return token.Length > 1 &&
               token[0] == '-' &&
               token[1] == '-';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAllWhiteSpace(string token)
    {
        if (token.Length == 0)
            return true;

        if (!char.IsWhiteSpace(token[0]))
            return false;

        for (int index = 1; index < token.Length; index++)
        {
            if (!char.IsWhiteSpace(token[index]))
                return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AsciiEqualsIgnoreCase(char left, char right)
    {
        return left == right || (left | (char)0x20) == right;
    }
}


