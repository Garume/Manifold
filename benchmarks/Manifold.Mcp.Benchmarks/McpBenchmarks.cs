using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Manifold.Generated;
using Manifold.Mcp.Benchmarks.Samples;
using McpToolkit;
using McpToolkit.Server;
using McpDotNetCallToolRequestParams = McpDotNet.Protocol.Types.CallToolRequestParams;
using McpDotNetCallToolResponse = McpDotNet.Protocol.Types.CallToolResponse;

namespace Manifold.Mcp.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class McpDiscoveryBenchmarks
{
    private static readonly Type ManifoldToolType = typeof(GeneratedMcpTools);
    private static readonly Type OfficialToolType = typeof(OfficialMcpTools);
    private static readonly Type McpDotNetToolType = typeof(McpDotNetTools);
    private static readonly FieldInfo McpToolkitToolsField =
        typeof(McpServer).GetField("tools", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("McpToolkit tools field was not found.");
    private static readonly McpServer McpToolkitServer = CreateMcpToolkitServer();

    private ToolMetadata[]? officialTools;
    private ToolMetadata[]? mcpToolkitTools;
    private ToolMetadata[]? mcpDotNetTools;

    [GlobalSetup]
    public void Setup()
    {
        officialTools = DescribeOfficialTools(OfficialToolType);
        mcpToolkitTools = DescribeMcpToolkitTools(McpToolkitServer);
        mcpDotNetTools = DescribeMcpDotNetTools(McpDotNetToolType);
    }

    [Benchmark(Baseline = true)]
    public int Manifold()
    {
        return Consume(GeneratedMcpCatalog.AsSpan());
    }

    [Benchmark]
    public int ModelContextProtocol()
    {
        return Consume(officialTools!);
    }

    [Benchmark]
    public int McpToolkit()
    {
        return Consume(mcpToolkitTools!);
    }

    [Benchmark]
    public int McpDotNet()
    {
        return Consume(mcpDotNetTools!);
    }

    private static ToolMetadata[] DescribeOfficialTools(Type toolType)
    {
        return toolType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static method => new
            {
                Method = method,
                Attribute = method.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>()
            })
            .Where(static candidate => candidate.Attribute is not null)
            .Select(static candidate => new ToolMetadata(
                candidate.Attribute!.Name ?? string.Empty,
                candidate.Method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                candidate.Method.GetParameters().Length))
            .ToArray();
    }

    private static ToolMetadata[] DescribeMcpToolkitTools(McpServer server)
    {
        return GetMcpToolkitToolMap(server)
            .Values
            .Select(static descriptor => new ToolMetadata(
                descriptor.Tool.Name ?? string.Empty,
                descriptor.Tool.Description,
                CountMcpToolkitParameters(descriptor.Tool.InputSchema)))
            .ToArray();
    }

    private static ToolMetadata[] DescribeMcpDotNetTools(Type toolType)
    {
        return toolType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static method => new
            {
                Method = method,
                Attribute = method.GetCustomAttribute<McpDotNet.Server.McpToolAttribute>()
            })
            .Where(static candidate => candidate.Attribute is not null)
            .Select(static candidate => new ToolMetadata(
                candidate.Attribute!.Name ?? string.Empty,
                candidate.Method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                candidate.Method.GetParameters().Length))
            .ToArray();
    }

    private static ConcurrentDictionary<string, ToolDescriptor> GetMcpToolkitToolMap(McpServer server)
    {
        return (ConcurrentDictionary<string, ToolDescriptor>)(McpToolkitToolsField.GetValue(server)
               ?? throw new InvalidOperationException("McpToolkit tool registry was not available."));
    }

    private static int CountMcpToolkitParameters(JsonElement inputSchema)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object ||
            !inputSchema.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        int count = 0;
        foreach (JsonProperty _ in properties.EnumerateObject())
            count++;

        return count;
    }

    private static McpServer CreateMcpToolkitServer()
    {
        McpServer server = new();
        server.Tools.Add("math_add", "Add two integers.", (int x, int y) => x + y);
        server.Tools.Add("weather_preview", "Create a simple weather preview.", (string targetCity, int days, bool metric) =>
            string.Concat(targetCity, ":", days.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", metric ? "metric" : "imperial"));
        return server;
    }

    private static int Consume(ReadOnlySpan<McpToolDescriptor> tools)
    {
        int total = 0;
        foreach (ref readonly McpToolDescriptor tool in tools)
        {
            total += tool.Name.Length;
            total += tool.Description?.Length ?? 0;
            total += tool.Parameters.Length;
        }

        return total;
    }

    private static int Consume(ReadOnlySpan<ToolMetadata> tools)
    {
        int total = 0;
        foreach (ref readonly ToolMetadata tool in tools)
        {
            total += tool.Name.Length;
            total += tool.Description?.Length ?? 0;
            total += tool.ParameterCount;
        }

        return total;
    }

    private readonly record struct ToolMetadata(string Name, string? Description, int ParameterCount);
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class McpInvocationBenchmarks
{
    private static readonly FieldInfo McpToolkitToolsField =
        typeof(McpServer).GetField("tools", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("McpToolkit tools field was not found.");
    private static readonly JsonElement MathArguments =
        JsonSerializer.Deserialize<JsonElement>("{\"x\":4,\"y\":5}");
    private static readonly McpServer McpToolkitInvocationServer = CreateMathOnlyMcpToolkitServer();

    private GeneratedMcpInvoker? manifoldInvoker;
    private OfficialMcpTools? officialTools;
    private Func<JsonElement?, CancellationToken, ValueTask<McpToolkit.Content[]>>? mcpToolkitMathInvoker;
    private McpDotNetCallToolRequestParams? mcpDotNetMathRequest;
    private Func<McpDotNetCallToolRequestParams, CancellationToken, Task<McpDotNetCallToolResponse>>? mcpDotNetMathInvoker;

    [GlobalSetup]
    public void Setup()
    {
        manifoldInvoker = new GeneratedMcpInvoker();
        officialTools = new OfficialMcpTools();

        ConcurrentDictionary<string, ToolDescriptor> mcpToolkitTools =
            (ConcurrentDictionary<string, ToolDescriptor>)(McpToolkitToolsField.GetValue(McpToolkitInvocationServer)
            ?? throw new InvalidOperationException("McpToolkit tool registry was not available."));
        mcpToolkitMathInvoker = mcpToolkitTools["math_add_invocation"].Handler;

        mcpDotNetMathRequest = new McpDotNetCallToolRequestParams
        {
            Name = "math_add",
            Arguments = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["x"] = 4,
                ["y"] = 5
            }
        };
        mcpDotNetMathInvoker = static (request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, object> arguments = request.Arguments ?? throw new InvalidOperationException("Missing MCP arguments.");
            int x = Convert.ToInt32(arguments["x"], System.Globalization.CultureInfo.InvariantCulture);
            int y = Convert.ToInt32(arguments["y"], System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(new McpDotNetCallToolResponse
            {
                IsError = false,
                Content =
                [
                    new McpDotNet.Protocol.Types.Content
                    {
                        Type = "text",
                        Text = (x + y).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                ]
            });
        };
    }

    [Benchmark(Baseline = true)]
    public int Manifold()
    {
        bool found = manifoldInvoker!.TryInvokeFastSync("math_add", MathArguments, NullServiceProvider.Instance, CancellationToken.None, out FastMcpInvocationResult invocation);
        if (!found)
            throw new InvalidOperationException("Generated MCP invoker did not resolve math_add.");

        return invocation.Kind switch
        {
            FastMcpInvocationKind.Number => invocation.Number,
            FastMcpInvocationKind.LargeNumber => checked((int)invocation.LargeNumber),
            _ => throw new InvalidOperationException($"Unexpected fast MCP result kind '{invocation.Kind}'.")
        };
    }

    [Benchmark]
    public int ModelContextProtocol()
    {
        return officialTools!.MathAdd(4, 5);
    }

    [Benchmark]
    public string McpToolkit()
    {
        ValueTask<McpToolkit.Content[]> pending = mcpToolkitMathInvoker!(MathArguments, CancellationToken.None);
        McpToolkit.Content[] response = pending.IsCompletedSuccessfully
            ? pending.Result
            : pending.AsTask().GetAwaiter().GetResult();
        return response[0]?.Text ?? string.Empty;
    }

    [Benchmark]
    public string McpDotNet()
    {
        McpDotNetCallToolResponse response = mcpDotNetMathInvoker!(mcpDotNetMathRequest!, CancellationToken.None).GetAwaiter().GetResult();
        return response.Content[0]?.Text ?? string.Empty;
    }

    private static McpServer CreateMathOnlyMcpToolkitServer()
    {
        McpServer server = new();
        server.Tools.Add("math_add_invocation", "Add two integers.", (int x, int y) => x + y);
        return server;
    }
}

internal sealed class NullServiceProvider : IServiceProvider
{
    public static NullServiceProvider Instance { get; } = new();

    private NullServiceProvider()
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
