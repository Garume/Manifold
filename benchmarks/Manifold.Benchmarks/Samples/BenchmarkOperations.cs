namespace Manifold.Benchmarks.Samples;

internal static class BenchmarkSink
{
    public static int NumberValue;
}

internal static class BenchmarkOperations
{
    [Operation("math.add", Description = "Add two integers.")]
    [CliCommand("math", "add")]
    [McpTool("math_add")]
    public static void MathAdd(
        [Argument(0, Name = "x", Description = "Left operand")] int x,
        [Argument(1, Name = "y", Description = "Right operand")] int y)
    {
        BenchmarkSink.NumberValue = x + y;
    }

    [Operation("weather.preview", Description = "Create a simple weather preview.")]
    [CliCommand("weather", "preview")]
    [McpTool("weather_preview")]
    public static void WeatherPreview(
        [Option("city", Description = "City name")]
        [McpName("targetCity")]
        string city,
        [Option("days", Description = "Forecast span")] int days)
    {
        BenchmarkSink.NumberValue = city.Length + days;
    }
}
