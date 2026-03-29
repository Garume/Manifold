using Manifold.Generated;

namespace Manifold.Mcp.Tests;

public sealed class GeneratedMcpCatalogTests
{
    [Fact]
    public void Tools_exposes_generated_mcp_metadata()
    {
        Assert.Equal(4, GeneratedMcpCatalog.Tools.Count);
        Assert.Collection(
            GeneratedMcpCatalog.Tools.Select(static tool => tool.Name),
            static name => Assert.Equal("forecast_preview", name),
            static name => Assert.Equal("math.sum", name),
            static name => Assert.Equal("sample_class_hello_instance", name),
            static name => Assert.Equal("sample_hello", name));
    }

    [Fact]
    public void TryFind_returns_parameter_and_result_metadata()
    {
        bool found = GeneratedMcpCatalog.TryFind("sample_hello", out McpToolDescriptor descriptor);

        Assert.True(found);
        Assert.Equal("Say hello.", descriptor.Description);

        McpParameterDescriptor parameter = Assert.Single(descriptor.Parameters);
        Assert.Equal("targetName", parameter.Name);
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.True(parameter.Required);
        Assert.Equal("User name", parameter.Description);
    }

    [Fact]
    public void AsSpan_returns_same_tools_without_additional_projection()
    {
        ReadOnlySpan<McpToolDescriptor> tools = GeneratedMcpCatalog.AsSpan();

        Assert.Equal(4, tools.Length);
        Assert.Equal("forecast_preview", tools[0].Name);
        Assert.Equal("math.sum", tools[1].Name);
        Assert.Equal("sample_class_hello_instance", tools[2].Name);
        Assert.Equal("sample_hello", tools[3].Name);
    }
}
