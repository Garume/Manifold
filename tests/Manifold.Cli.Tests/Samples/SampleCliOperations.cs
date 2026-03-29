namespace Manifold.Cli.Tests.Samples;

internal interface IMathOffsetProvider
{
    public int Offset { get; }
}

internal sealed class ConstantMathOffsetProvider(int offset) : IMathOffsetProvider
{
    public int Offset { get; } = offset;
}

internal sealed record WeatherPreview(string City, int Temperature);

internal sealed class WeatherPreviewFormatter : IResultFormatter<WeatherPreview>
{
    public string? FormatText(WeatherPreview result, OperationContext context)
    {
        return $"{result.City}:{result.Temperature}:{context.Surface}";
    }
}

[Operation("math.scale", Description = "Scale an integer using an injected offset.")]
[CliCommand("math", "scale")]
internal sealed class MathScaleOperation(IMathOffsetProvider offsets) : IOperation<MathScaleOperation.Request, int>
{
    public ValueTask<int> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult(request.Value * offsets.Offset);
    }

    internal sealed class Request
    {
        [Argument(0, Name = "value", Description = "Input value")]
        public int Value { get; init; }
    }
}

[Operation("counter.preview", Description = "Preview a counter value.")]
[CliCommand("counter", "preview")]
internal sealed class CounterPreviewOperation : IOperation<CounterPreviewOperation.Request, int>
{
    public ValueTask<int> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult(request.Count <= 0 ? 2 : request.Count);
    }

    internal sealed class Request
    {
        [Option("count", Description = "Preview count", Required = false)]
        public int Count { get; init; }
    }
}

internal static class SampleCliOperations
{
    [Operation("math.add", Description = "Add two integers.")]
    [CliCommand("math", "add")]
    [Alias("sum", "calc plus")]
    public static Task<int> AddAsync(
        [Argument(0, Name = "x", Description = "Left operand")] int x,
        [Argument(1, Name = "y", Description = "Right operand")] int y,
        [FromServices] IMathOffsetProvider offsets,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(x + y + offsets.Offset + (cancellationToken.CanBeCanceled ? 0 : 0));
    }

    [Operation("sample.hello", Description = "Say hello.", Hidden = true)]
    [CliCommand("sample", "hello")]
    public static string Hello(
        [Option("name", Description = "User name")]
        [CliName("person")]
        [Alias("n", "username")]
        string name)
    {
        return $"Hello, {name}";
    }

    [Operation("weather.preview", Description = "Preview the weather.")]
    [CliCommand("weather", "preview")]
    [ResultFormatter(typeof(WeatherPreviewFormatter))]
    public static ValueTask<WeatherPreview> PreviewAsync(
        [Option("city", Description = "Target city")] string city,
        [Option("temperature", Description = "Degrees", Required = false)] int? temperature = null)
    {
        return ValueTask.FromResult(new WeatherPreview(city, temperature ?? 20));
    }

    [Operation("internal.ping")]
    [McpOnly]
    [CliCommand("internal", "ping")]
    public static string Ping()
    {
        return "pong";
    }
}



