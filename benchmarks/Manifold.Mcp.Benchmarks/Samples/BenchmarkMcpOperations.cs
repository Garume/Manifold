using System.ComponentModel;
using System.Globalization;

namespace Manifold.Mcp.Benchmarks.Samples;

internal static class BenchmarkMcpOperations
{
    [Operation("math.add", Description = "Add two integers.")]
    [McpTool("math_add")]
    public static int MathAdd(
        [Argument(0, Name = "x", Description = "Left operand")] int x,
        [Argument(1, Name = "y", Description = "Right operand")] int y)
    {
        return x + y;
    }

    [Operation("weather.preview", Description = "Create a simple weather preview.")]
    [McpTool("weather_preview")]
    public static string WeatherPreview(
        [Option("city", Description = "City name")]
        [McpName("targetCity")]
        string targetCity,
        [Option("days", Description = "Forecast span")] int days,
        [Option("metric", Description = "Metric flag")] bool metric)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{targetCity}:{days}:{(metric ? "metric" : "imperial")}");
    }
}

[ModelContextProtocol.Server.McpServerToolType]
internal sealed class OfficialMcpTools
{
    [ModelContextProtocol.Server.McpServerTool(Name = "math_add")]
    [Description("Add two integers.")]
    public int MathAdd(
        [Description("Left operand")] int x,
        [Description("Right operand")] int y)
    {
        return x + y;
    }

    [ModelContextProtocol.Server.McpServerTool(Name = "weather_preview")]
    [Description("Create a simple weather preview.")]
    public string WeatherPreview(
        [Description("City name")] string targetCity,
        [Description("Forecast span")] int days,
        [Description("Metric flag")] bool metric)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{targetCity}:{days}:{(metric ? "metric" : "imperial")}");
    }
}

[McpDotNet.Server.McpToolType]
internal sealed class McpDotNetTools
{
    [McpDotNet.Server.McpTool("math_add")]
    [Description("Add two integers.")]
    public int MathAdd(
        [Description("Left operand")] int x,
        [Description("Right operand")] int y)
    {
        return x + y;
    }

    [McpDotNet.Server.McpTool("weather_preview")]
    [Description("Create a simple weather preview.")]
    public string WeatherPreview(
        [Description("City name")] string targetCity,
        [Description("Forecast span")] int days,
        [Description("Metric flag")] bool metric)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{targetCity}:{days}:{(metric ? "metric" : "imperial")}");
    }
}
