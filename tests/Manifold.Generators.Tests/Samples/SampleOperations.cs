namespace Manifold.Generators.Tests.Samples;

internal static class SampleOperations
{
    [Operation("sample.hello", Description = "Say hello.", Summary = "Returns a greeting.", Hidden = true)]
    [CliCommand("sample", "hello")]
    [McpTool("sample_hello")]
    public static string Hello(
        [Option("name", Description = "User name")]
        [CliName("person")]
        [McpName("targetName")]
        [Alias("n", "username")]
        string name,
        CancellationToken cancellationToken = default)
    {
        return $"Hello, {name}{cancellationToken.CanBeCanceled}";
    }

    [Operation("math.add", Description = "Add two integers.")]
    [CliOnly]
    [CliCommand("math", "add")]
    [Alias("sum", "calc plus")]
    public static Task<int> AddAsync(
        [Argument(0, Name = "x", Description = "Left operand")] int x,
        [Argument(1, Name = "y", Description = "Right operand")] int y,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(x + y + services.GetHashCode() + cancellationToken.GetHashCode());
    }

    [Operation("weather.get")]
    [McpOnly]
    [McpName("weather_fetch")]
    public static ValueTask<string> FetchAsync(
        [Option("city", Description = "Target city", Required = false)] string? city = null)
    {
        return ValueTask.FromResult(city ?? "unknown");
    }
}


