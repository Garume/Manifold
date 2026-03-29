using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Manifold.Mcp;

public static class McpTextContentResponseWriter
{
    private static ReadOnlySpan<byte> ResponsePrefix => "{\"isError\":false,\"content\":[{\"type\":\"text\",\"text\":\""u8;
    private static ReadOnlySpan<byte> ResponseSuffix => "\"}]}"u8;

    public static void WriteCallToolResponse(IBufferWriter<byte> writer, in FastMcpInvocationResult invocation)
    {
        ArgumentNullException.ThrowIfNull(writer);

        switch (invocation.Kind)
        {
            case FastMcpInvocationKind.None:
                WriteEmpty(writer);
                return;
            case FastMcpInvocationKind.Boolean:
                WriteBoolean(writer, invocation.Boolean);
                return;
            case FastMcpInvocationKind.Number:
                WriteInt32(writer, invocation.Number);
                return;
            case FastMcpInvocationKind.LargeNumber:
                WriteInt64(writer, invocation.LargeNumber);
                return;
        }

        WriteSlow(writer, in invocation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteEmpty(IBufferWriter<byte> writer)
    {
        WriteUtf8(writer, ResponsePrefix);
        WriteUtf8(writer, ResponseSuffix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBoolean(IBufferWriter<byte> writer, bool value)
    {
        WriteUtf8(writer, ResponsePrefix);
        WriteUtf8(writer, value ? "true"u8 : "false"u8);
        WriteUtf8(writer, ResponseSuffix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteInt32(IBufferWriter<byte> writer, int value)
    {
        WriteUtf8(writer, ResponsePrefix);
        WriteFormatted(writer, value);
        WriteUtf8(writer, ResponseSuffix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteInt64(IBufferWriter<byte> writer, long value)
    {
        WriteUtf8(writer, ResponsePrefix);
        WriteFormatted(writer, value);
        WriteUtf8(writer, ResponseSuffix);
    }

    private static void WriteSlow(IBufferWriter<byte> writer, in FastMcpInvocationResult invocation)
    {
        using Utf8JsonWriter jsonWriter = new(writer);
        jsonWriter.WriteStartObject();
        jsonWriter.WriteBoolean("isError", false);
        jsonWriter.WritePropertyName("content");
        jsonWriter.WriteStartArray();
        jsonWriter.WriteStartObject();
        jsonWriter.WriteString("type", "text");
        jsonWriter.WriteString("text", GetTextContent(in invocation));
        jsonWriter.WriteEndObject();
        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();
    }

    private static string GetTextContent(in FastMcpInvocationResult invocation)
    {
        return invocation.Kind switch
        {
            FastMcpInvocationKind.None => string.Empty,
            FastMcpInvocationKind.Text => invocation.Text ?? string.Empty,
            FastMcpInvocationKind.Boolean => invocation.Boolean ? "true" : "false",
            FastMcpInvocationKind.Number => invocation.Number.ToString(CultureInfo.InvariantCulture),
            FastMcpInvocationKind.LargeNumber => invocation.LargeNumber.ToString(CultureInfo.InvariantCulture),
            FastMcpInvocationKind.RealNumber => invocation.RealNumber.ToString(CultureInfo.InvariantCulture),
            FastMcpInvocationKind.PreciseNumber => invocation.PreciseNumber.ToString(CultureInfo.InvariantCulture),
            FastMcpInvocationKind.Identifier => invocation.Identifier.ToString(),
            FastMcpInvocationKind.Timestamp => invocation.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            FastMcpInvocationKind.Structured => invocation.StructuredValue is null
                ? string.Empty
                : JsonSerializer.Serialize(invocation.StructuredValue, invocation.StructuredValueType ?? invocation.StructuredValue.GetType()),
            _ => throw new InvalidOperationException($"Unsupported MCP invocation result kind '{invocation.Kind}'.")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUtf8(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        Span<byte> destination = writer.GetSpan(value.Length);
        value.CopyTo(destination);
        writer.Advance(value.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFormatted(IBufferWriter<byte> writer, int value)
    {
        Span<byte> destination = writer.GetSpan(11);
        if (!Utf8Formatter.TryFormat(value, destination, out int written))
            throw new InvalidOperationException("Failed to format MCP int response.");

        writer.Advance(written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFormatted(IBufferWriter<byte> writer, long value)
    {
        Span<byte> destination = writer.GetSpan(20);
        if (!Utf8Formatter.TryFormat(value, destination, out int written))
            throw new InvalidOperationException("Failed to format MCP long response.");

        writer.Advance(written);
    }
}
