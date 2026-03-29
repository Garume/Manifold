namespace Manifold.Mcp.Tests.Samples;

internal interface IGreetingPrefixProvider
{
    public string Prefix { get; }
}

internal sealed class ConstantGreetingPrefixProvider(string prefix) : IGreetingPrefixProvider
{
    public string Prefix { get; } = prefix;
}

[Operation("sample.class-hello", Description = "Say hello from an instance operation.")]
[McpTool("sample_class_hello_instance")]
internal sealed class SampleClassHelloOperation(IGreetingPrefixProvider provider) : IOperation<SampleClassHelloOperation.Request, string>
{
    public ValueTask<string> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult($"{provider.Prefix}{request.Name}:{context.Surface}");
    }

    internal sealed class Request
    {
        [Option("name", Description = "User name")]
        [McpName("targetName")]
        public string Name { get; init; } = string.Empty;
    }
}

[Operation("forecast.preview", Description = "Preview the forecast.")]
[McpTool("forecast_preview")]
internal sealed class ForecastPreviewOperation : IOperation<ForecastPreviewOperation.Request, int>
{
    public ValueTask<int> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult(request.Days <= 0 ? 2 : request.Days);
    }

    internal sealed class Request
    {
        [Option("days", Description = "Number of days", Required = false)]
        public int Days { get; init; }
    }
}

internal static class SampleMcpOperations
{
    [Operation("sample.hello", Description = "Say hello.")]
    [McpTool("sample_hello")]
    public static string Hello(
        [Option("name", Description = "User name")]
        [McpName("targetName")]
        string name,
        [FromServices] IGreetingPrefixProvider provider,
        CancellationToken cancellationToken = default)
    {
        return $"{provider.Prefix}{name}:{cancellationToken.CanBeCanceled}";
    }

    [Operation("math.sum", Description = "Add two integers.")]
    [McpOnly]
    public static ValueTask<int> SumAsync(
        [Argument(0, Name = "x", Description = "Left operand")] int x,
        [Argument(1, Name = "y", Description = "Right operand")] int y)
    {
        return ValueTask.FromResult(x + y);
    }

    [Operation("internal.cli-only")]
    [CliOnly]
    [CliCommand("internal", "cli-only")]
    public static string CliOnly()
    {
        return "nope";
    }
}


