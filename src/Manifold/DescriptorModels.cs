namespace Manifold;

public enum InvocationSurface
{
    Unknown = 0,
    Cli = 1,
    Mcp = 2,
    Protocol = 3
}

public enum OperationVisibility
{
    Both = 0,
    CliOnly = 1,
    McpOnly = 2
}

public enum ParameterSource
{
    Option = 0,
    Argument = 1,
    Service = 2,
    CancellationToken = 3
}

public sealed record ParameterDescriptor(
    string Name,
    Type ParameterType,
    ParameterSource Source,
    bool Required,
    int? Position = null,
    string? Description = null,
    IReadOnlyList<string>? Aliases = null,
    string? CliName = null,
    string? McpName = null,
    string? RequestPropertyName = null);

public sealed record OperationDescriptor(
    string OperationId,
    Type DeclaringType,
    string MethodName,
    Type ResultType,
    OperationVisibility Visibility,
    IReadOnlyList<ParameterDescriptor> Parameters,
    string? Description = null,
    string? Summary = null,
    IReadOnlyList<string>? CliCommandPath = null,
    IReadOnlyList<IReadOnlyList<string>>? CliCommandAliases = null,
    string? McpToolName = null,
    bool Hidden = false,
    Type? RequestType = null);


