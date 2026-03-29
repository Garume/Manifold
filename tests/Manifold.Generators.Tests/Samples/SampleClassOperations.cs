namespace Manifold.Generators.Tests.Samples;

[Operation("sample.class-hello", Description = "Say hello from a class.", Summary = "Returns a class greeting.")]
[CliCommand("sample", "class-hello")]
[McpTool("sample_class_hello")]
internal sealed class SampleClassHelloOperation : IOperation<SampleClassHelloOperation.Request, string>
{
    public ValueTask<string> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult($"Hello, {request.Name}:{context.Surface}");
    }

    internal sealed class Request
    {
        [Option("name", Description = "User name")]
        [CliName("person")]
        [McpName("targetName")]
        [Alias("n", "username")]
        public string Name { get; init; } = string.Empty;
    }
}
