using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Manifold.Mcp;

public readonly struct FastMcpInvocationResult
{
    public static FastMcpInvocationResult None { get; } = new(FastMcpInvocationKind.None);

    private readonly FastMcpInvocationValue value;
    private readonly object? reference;
    private readonly Type? structuredValueType;

    private FastMcpInvocationResult(
        FastMcpInvocationKind kind,
        FastMcpInvocationValue value = default,
        object? reference = null,
        Type? structuredValueType = null)
    {
        Kind = kind;
        this.value = value;
        this.reference = reference;
        this.structuredValueType = structuredValueType;
    }

    public FastMcpInvocationKind Kind { get; }

    public string? Text => reference as string;

    public bool Boolean => value.Boolean;

    public int Number => value.Number;

    public long LargeNumber => value.LargeNumber;

    public double RealNumber => value.RealNumber;

    public decimal PreciseNumber => value.PreciseNumber;

    public Guid Identifier => value.Identifier;

    public DateTimeOffset Timestamp => value.Timestamp;

    public object? StructuredValue => reference;

    public Type? StructuredValueType => structuredValueType;

    public static FastMcpInvocationResult FromText(string? text)
    {
        return text is null
            ? None
            : new FastMcpInvocationResult(FastMcpInvocationKind.Text, reference: text);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationResult FromBoolean(bool value)
    {
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.Boolean,
            value: FastMcpInvocationValue.FromBoolean(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationResult FromNumber(int value)
    {
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.Number,
            value: FastMcpInvocationValue.FromNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationResult FromLargeNumber(long value)
    {
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.LargeNumber,
            value: FastMcpInvocationValue.FromLargeNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationResult FromRealNumber(double value)
    {
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.RealNumber,
            value: FastMcpInvocationValue.FromRealNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationResult FromPreciseNumber(decimal value)
    {
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.PreciseNumber,
            value: FastMcpInvocationValue.FromPreciseNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationResult FromIdentifier(Guid value)
    {
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.Identifier,
            value: FastMcpInvocationValue.FromIdentifier(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationResult FromTimestamp(DateTimeOffset value)
    {
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.Timestamp,
            value: FastMcpInvocationValue.FromTimestamp(value));
    }

    public static FastMcpInvocationResult FromStructured(object? value, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        return new FastMcpInvocationResult(
            FastMcpInvocationKind.Structured,
            reference: value,
            structuredValueType: resultType);
    }
}

public enum FastMcpInvocationKind
{
    None = 0,
    Text = 1,
    Boolean = 2,
    Number = 3,
    LargeNumber = 4,
    RealNumber = 5,
    PreciseNumber = 6,
    Identifier = 7,
    Timestamp = 8,
    Structured = 9
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly struct FastMcpInvocationValue
{
    [FieldOffset(0)]
    private readonly bool boolean;

    [FieldOffset(0)]
    private readonly int number;

    [FieldOffset(0)]
    private readonly long largeNumber;

    [FieldOffset(0)]
    private readonly double realNumber;

    [FieldOffset(0)]
    private readonly decimal preciseNumber;

    [FieldOffset(0)]
    private readonly Guid identifier;

    [FieldOffset(0)]
    private readonly DateTimeOffset timestamp;

    public bool Boolean => boolean;

    public int Number => number;

    public long LargeNumber => largeNumber;

    public double RealNumber => realNumber;

    public decimal PreciseNumber => preciseNumber;

    public Guid Identifier => identifier;

    public DateTimeOffset Timestamp => timestamp;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationValue FromBoolean(bool value)
    {
        return new FastMcpInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationValue FromNumber(int value)
    {
        return new FastMcpInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationValue FromLargeNumber(long value)
    {
        return new FastMcpInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationValue FromRealNumber(double value)
    {
        return new FastMcpInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationValue FromPreciseNumber(decimal value)
    {
        return new FastMcpInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationValue FromIdentifier(Guid value)
    {
        return new FastMcpInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastMcpInvocationValue FromTimestamp(DateTimeOffset value)
    {
        return new FastMcpInvocationValue(value);
    }

    private FastMcpInvocationValue(bool value)
    {
        this = default;
        boolean = value;
    }

    private FastMcpInvocationValue(int value)
    {
        this = default;
        number = value;
    }

    private FastMcpInvocationValue(long value)
    {
        this = default;
        largeNumber = value;
    }

    private FastMcpInvocationValue(double value)
    {
        this = default;
        realNumber = value;
    }

    private FastMcpInvocationValue(decimal value)
    {
        this = default;
        preciseNumber = value;
    }

    private FastMcpInvocationValue(Guid value)
    {
        this = default;
        identifier = value;
    }

    private FastMcpInvocationValue(DateTimeOffset value)
    {
        this = default;
        timestamp = value;
    }
}
