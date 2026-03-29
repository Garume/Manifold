namespace Manifold.Samples.Operations;

public static class SampleOperations
{
    [Operation("math.add", Description = "Add two integers.", Summary = "Returns the sum of x and y.")]
    [CliCommand("math", "add")]
    [McpTool("math_add")]
    public static int Add(
        [Argument(0, Name = "x", Description = "Left operand")] int x,
        [Argument(1, Name = "y", Description = "Right operand")] int y)
    {
        return x + y;
    }
}

[Operation("weather.preview", Description = "Return a pretend weather summary.", Summary = "Returns a short fake forecast.")]
[CliCommand("weather", "preview")]
[McpTool("weather_preview")]
public sealed class WeatherPreviewOperation : IOperation<WeatherPreviewOperation.Request, string>
{
    public ValueTask<string> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        int days = request.Days <= 0 ? 1 : request.Days;
        string city = string.IsNullOrWhiteSpace(request.City) ? "unknown" : request.City.Trim();
        string summary = $"Forecast for {city}: mild for the next {days} day(s). Surface={context.Surface}.";
        return ValueTask.FromResult(summary);
    }

    public sealed class Request
    {
        [Option("city", Description = "Target city")]
        public string City { get; init; } = string.Empty;

        [Option("days", Description = "Number of forecast days")]
        public int Days { get; init; } = 3;
    }
}
