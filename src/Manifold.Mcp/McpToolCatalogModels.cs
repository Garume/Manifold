namespace Manifold.Mcp;

public readonly record struct McpParameterDescriptor(
    string Name,
    Type ParameterType,
    bool Required,
    string? Description = null);

public readonly record struct McpToolDescriptor(
    string Name,
    string? Description,
    McpParameterDescriptor[] Parameters);
