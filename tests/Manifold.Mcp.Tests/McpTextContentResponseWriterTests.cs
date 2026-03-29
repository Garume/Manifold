using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Manifold.Mcp.Tests;

public sealed class McpTextContentResponseWriterTests
{
    [Fact]
    public void Number_result_writes_expected_call_tool_response()
    {
        ArrayBufferWriter<byte> buffer = new();

        McpTextContentResponseWriter.WriteCallToolResponse(buffer, FastMcpInvocationResult.FromNumber(9));

        string json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        Assert.Equal("{\"isError\":false,\"content\":[{\"type\":\"text\",\"text\":\"9\"}]}", json);
    }

    [Fact]
    public void Text_result_escapes_content_correctly()
    {
        ArrayBufferWriter<byte> buffer = new();

        McpTextContentResponseWriter.WriteCallToolResponse(buffer, FastMcpInvocationResult.FromText("Hello \"Alice\""));

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("isError").GetBoolean());
        Assert.Equal("text", root.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("Hello \"Alice\"", root.GetProperty("content")[0].GetProperty("text").GetString());
    }
}
