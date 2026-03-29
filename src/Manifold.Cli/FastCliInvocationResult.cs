using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Manifold.Cli;

public readonly struct FastCliInvocationResult
{
    public static FastCliInvocationResult None { get; } = new(FastCliInvocationKind.None);

    private readonly FastCliInvocationValue value;
    private readonly string? text;

    private FastCliInvocationResult(
        FastCliInvocationKind kind,
        FastCliInvocationValue value = default,
        string? text = null)
    {
        Kind = kind;
        this.value = value;
        this.text = text;
    }

    public FastCliInvocationKind Kind { get; }

    public string? Text => text;

    public bool Boolean => value.Boolean;

    public int Number => value.Number;

    public long LargeNumber => value.LargeNumber;

    public double RealNumber => value.RealNumber;

    public decimal PreciseNumber => value.PreciseNumber;

    public Guid Identifier => value.Identifier;

    public DateTimeOffset Timestamp => value.Timestamp;

    public static FastCliInvocationResult FromText(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? None
            : new FastCliInvocationResult(FastCliInvocationKind.Text, text: text);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationResult FromBoolean(bool value)
    {
        return new FastCliInvocationResult(
            FastCliInvocationKind.Boolean,
            value: FastCliInvocationValue.FromBoolean(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationResult FromNumber(int value)
    {
        return new FastCliInvocationResult(
            FastCliInvocationKind.Number,
            value: FastCliInvocationValue.FromNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationResult FromLargeNumber(long value)
    {
        return new FastCliInvocationResult(
            FastCliInvocationKind.LargeNumber,
            value: FastCliInvocationValue.FromLargeNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationResult FromRealNumber(double value)
    {
        return new FastCliInvocationResult(
            FastCliInvocationKind.RealNumber,
            value: FastCliInvocationValue.FromRealNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationResult FromPreciseNumber(decimal value)
    {
        return new FastCliInvocationResult(
            FastCliInvocationKind.PreciseNumber,
            value: FastCliInvocationValue.FromPreciseNumber(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationResult FromIdentifier(Guid value)
    {
        return new FastCliInvocationResult(
            FastCliInvocationKind.Identifier,
            value: FastCliInvocationValue.FromIdentifier(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationResult FromTimestamp(DateTimeOffset value)
    {
        return new FastCliInvocationResult(
            FastCliInvocationKind.Timestamp,
            value: FastCliInvocationValue.FromTimestamp(value));
    }
}

public enum FastCliInvocationKind
{
    None = 0,
    Text = 1,
    Boolean = 2,
    Number = 3,
    LargeNumber = 4,
    RealNumber = 5,
    PreciseNumber = 6,
    Identifier = 7,
    Timestamp = 8
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly struct FastCliInvocationValue
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
    public static FastCliInvocationValue FromBoolean(bool value)
    {
        return new FastCliInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationValue FromNumber(int value)
    {
        return new FastCliInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationValue FromLargeNumber(long value)
    {
        return new FastCliInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationValue FromRealNumber(double value)
    {
        return new FastCliInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationValue FromPreciseNumber(decimal value)
    {
        return new FastCliInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationValue FromIdentifier(Guid value)
    {
        return new FastCliInvocationValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastCliInvocationValue FromTimestamp(DateTimeOffset value)
    {
        return new FastCliInvocationValue(value);
    }

    private FastCliInvocationValue(bool value)
    {
        this = default;
        boolean = value;
    }

    private FastCliInvocationValue(int value)
    {
        this = default;
        number = value;
    }

    private FastCliInvocationValue(long value)
    {
        this = default;
        largeNumber = value;
    }

    private FastCliInvocationValue(double value)
    {
        this = default;
        realNumber = value;
    }

    private FastCliInvocationValue(decimal value)
    {
        this = default;
        preciseNumber = value;
    }

    private FastCliInvocationValue(Guid value)
    {
        this = default;
        identifier = value;
    }

    private FastCliInvocationValue(DateTimeOffset value)
    {
        this = default;
        timestamp = value;
    }
}
