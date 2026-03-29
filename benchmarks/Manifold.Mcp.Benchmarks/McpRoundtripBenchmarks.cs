using System.Buffers;
using System.Collections.Concurrent;
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
using McpDotNetContent = McpDotNet.Protocol.Types.Content;

namespace Manifold.Mcp.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class McpListToolsRoundtripBenchmarks : McpRoundtripBenchmarkBase
{
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
        InitializeWriter();
        officialTools = DescribeOfficialTools(OfficialToolType);
        mcpToolkitTools = DescribeMcpToolkitTools(McpToolkitServer);
        mcpDotNetTools = DescribeMcpDotNetTools(McpDotNetToolType);
    }

    [Benchmark(Baseline = true)]
    public int Manifold()
    {
        return WriteListToolsResponse(GeneratedMcpCatalog.AsSpan());
    }

    [Benchmark]
    public int ModelContextProtocol()
    {
        return WriteListToolsResponse(officialTools!);
    }

    [Benchmark]
    public int McpToolkit()
    {
        return WriteListToolsResponse(mcpToolkitTools!);
    }

    [Benchmark]
    public int McpDotNet()
    {
        return WriteListToolsResponse(mcpDotNetTools!);
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
                candidate.Method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description,
                candidate.Method.GetParameters()
                    .Select(static parameter => new ToolParameterMetadata(
                        parameter.Name ?? string.Empty,
                        McpRoundtripBenchmarkBase.GetJsonType(parameter.ParameterType),
                        true,
                        parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description))
                    .ToArray()))
            .ToArray();
    }

    private static ToolMetadata[] DescribeMcpToolkitTools(McpServer server)
    {
        return GetMcpToolkitToolMap(server)
            .Values
            .Select(static descriptor => new ToolMetadata(
                NormalizeRoundtripToolkitToolName(descriptor.Tool.Name ?? string.Empty),
                descriptor.Tool.Description,
                GetMcpToolkitParameters(descriptor.Tool.InputSchema)))
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
                candidate.Method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description,
                candidate.Method.GetParameters()
                    .Select(static parameter => new ToolParameterMetadata(
                        parameter.Name ?? string.Empty,
                        McpRoundtripBenchmarkBase.GetJsonType(parameter.ParameterType),
                        true,
                        parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description))
                    .ToArray()))
            .ToArray();
    }

    private static ConcurrentDictionary<string, ToolDescriptor> GetMcpToolkitToolMap(McpServer server)
    {
        return (ConcurrentDictionary<string, ToolDescriptor>)(McpToolkitToolsField.GetValue(server)
               ?? throw new InvalidOperationException("McpToolkit tool registry was not available."));
    }

    private static ToolParameterMetadata[] GetMcpToolkitParameters(JsonElement inputSchema)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object ||
            !inputSchema.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        HashSet<string> required = [];
        if (inputSchema.TryGetProperty("required", out JsonElement requiredProperties) &&
            requiredProperties.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in requiredProperties.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                    required.Add(element.GetString() ?? string.Empty);
            }
        }

        List<ToolParameterMetadata> parameters = [];
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            JsonElement definition = property.Value;
            string jsonType = definition.ValueKind == JsonValueKind.Object &&
                              definition.TryGetProperty("type", out JsonElement typeProperty) &&
                              typeProperty.ValueKind == JsonValueKind.String
                ? typeProperty.GetString() ?? "string"
                : "string";
            string? description = definition.ValueKind == JsonValueKind.Object &&
                                  definition.TryGetProperty("description", out JsonElement descriptionProperty) &&
                                  descriptionProperty.ValueKind == JsonValueKind.String
                ? descriptionProperty.GetString()
                : null;

            parameters.Add(new ToolParameterMetadata(
                property.Name,
                jsonType,
                required.Contains(property.Name),
                description));
        }

        return [.. parameters];
    }

    private static McpServer CreateMcpToolkitServer()
    {
        McpServer server = new();
        server.Tools.Add("math_add_roundtrip_list", "Add two integers.", (int x, int y) => x + y);
        server.Tools.Add("weather_preview_roundtrip_list", "Create a simple weather preview.", (string targetCity, int days, bool metric) =>
            string.Concat(targetCity, ":", days.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", metric ? "metric" : "imperial"));
        return server;
    }

    private static string NormalizeRoundtripToolkitToolName(string toolName)
    {
        return toolName switch
        {
            "math_add_roundtrip_list" => "math_add",
            "weather_preview_roundtrip_list" => "weather_preview",
            _ => toolName
        };
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class McpCallToolRoundtripBenchmarks : McpRoundtripBenchmarkBase
{
    private static readonly FieldInfo McpToolkitToolsField =
        typeof(McpServer).GetField("tools", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("McpToolkit tools field was not found.");
    private static readonly JsonElement MathArguments =
        JsonSerializer.Deserialize<JsonElement>("{\"x\":4,\"y\":5}");
    private static readonly McpServer McpToolkitServer = CreateMathOnlyMcpToolkitServer();

    private GeneratedMcpInvoker? manifoldInvoker;
    private OfficialMcpTools? officialTools;
    private Func<JsonElement?, CancellationToken, ValueTask<Content[]>>? mcpToolkitMathInvoker;
    private McpDotNetCallToolRequestParams? mcpDotNetMathRequest;
    private Func<McpDotNetCallToolRequestParams, CancellationToken, Task<McpDotNetCallToolResponse>>? mcpDotNetMathInvoker;

    [GlobalSetup]
    public void Setup()
    {
        InitializeWriter();
        manifoldInvoker = new GeneratedMcpInvoker();
        officialTools = new OfficialMcpTools();

        ConcurrentDictionary<string, ToolDescriptor> mcpToolkitTools =
            (ConcurrentDictionary<string, ToolDescriptor>)(McpToolkitToolsField.GetValue(McpToolkitServer)
            ?? throw new InvalidOperationException("McpToolkit tool registry was not available."));
        mcpToolkitMathInvoker = mcpToolkitTools["math_add_roundtrip_call"].Handler;

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
                    new McpDotNetContent
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

        return WriteCallToolResponse(invocation);
    }

    [Benchmark]
    public int ModelContextProtocol()
    {
        return WriteCallToolResponse(officialTools!.MathAdd(4, 5));
    }

    [Benchmark]
    public int McpToolkit()
    {
        ValueTask<Content[]> pending = mcpToolkitMathInvoker!(MathArguments, CancellationToken.None);
        Content[] response = pending.IsCompletedSuccessfully
            ? pending.Result
            : pending.AsTask().GetAwaiter().GetResult();
        return WriteCallToolResponse(response);
    }

    [Benchmark]
    public int McpDotNet()
    {
        McpDotNetCallToolResponse response = mcpDotNetMathInvoker!(mcpDotNetMathRequest!, CancellationToken.None).GetAwaiter().GetResult();
        return WriteCallToolResponse(response);
    }

    private static McpServer CreateMathOnlyMcpToolkitServer()
    {
        McpServer server = new();
        server.Tools.Add("math_add_roundtrip_call", "Add two integers.", (int x, int y) => x + y);
        return server;
    }
}

public abstract class McpRoundtripBenchmarkBase
{
    private static readonly JsonEncodedText ToolsPropertyName = JsonEncodedText.Encode("tools");
    private static readonly JsonEncodedText NamePropertyName = JsonEncodedText.Encode("name");
    private static readonly JsonEncodedText DescriptionPropertyName = JsonEncodedText.Encode("description");
    private static readonly JsonEncodedText InputSchemaPropertyName = JsonEncodedText.Encode("inputSchema");
    private static readonly JsonEncodedText TypePropertyName = JsonEncodedText.Encode("type");
    private static readonly JsonEncodedText PropertiesPropertyName = JsonEncodedText.Encode("properties");
    private static readonly JsonEncodedText RequiredPropertyName = JsonEncodedText.Encode("required");
    private static readonly JsonEncodedText ContentPropertyName = JsonEncodedText.Encode("content");
    private static readonly JsonEncodedText IsErrorPropertyName = JsonEncodedText.Encode("isError");
    private static readonly JsonEncodedText TextPropertyName = JsonEncodedText.Encode("text");

    private ArrayBufferWriter<byte>? writerBuffer;
    private Utf8JsonWriter? jsonWriter;

    protected void InitializeWriter()
    {
        writerBuffer ??= new ArrayBufferWriter<byte>(512);
        jsonWriter ??= new Utf8JsonWriter(writerBuffer);
    }

    protected int WriteListToolsResponse(ReadOnlySpan<McpToolDescriptor> tools)
    {
        PrepareWriter();
        jsonWriter!.WriteStartObject();
        jsonWriter.WritePropertyName(ToolsPropertyName);
        jsonWriter.WriteStartArray();

        foreach (ref readonly McpToolDescriptor tool in tools)
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString(NamePropertyName, tool.Name);
            if (tool.Description is not null)
                jsonWriter.WriteString(DescriptionPropertyName, tool.Description);

            WriteInputSchema(tool.Parameters);
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();
        return ConsumeWrittenBuffer();
    }

    protected int WriteListToolsResponse(ReadOnlySpan<ToolMetadata> tools)
    {
        PrepareWriter();
        jsonWriter!.WriteStartObject();
        jsonWriter.WritePropertyName(ToolsPropertyName);
        jsonWriter.WriteStartArray();

        foreach (ref readonly ToolMetadata tool in tools)
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString(NamePropertyName, tool.Name);
            if (tool.Description is not null)
                jsonWriter.WriteString(DescriptionPropertyName, tool.Description);

            WriteInputSchema(tool.Parameters);
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();
        return ConsumeWrittenBuffer();
    }

    protected int WriteCallToolResponse(FastMcpInvocationResult invocation)
    {
        PrepareWriter();
        McpTextContentResponseWriter.WriteCallToolResponse(writerBuffer!, invocation);
        return ConsumeWrittenBuffer();
    }

    protected int WriteCallToolResponse(int value)
    {
        PrepareWriter();
        jsonWriter!.WriteStartObject();
        jsonWriter.WriteBoolean(IsErrorPropertyName, false);
        jsonWriter.WritePropertyName(ContentPropertyName);
        jsonWriter.WriteStartArray();
        jsonWriter.WriteStartObject();
        jsonWriter.WriteString(TypePropertyName, "text");
        jsonWriter.WriteString(TextPropertyName, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        jsonWriter.WriteEndObject();
        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();
        return ConsumeWrittenBuffer();
    }

    protected int WriteCallToolResponse(Content[] contents)
    {
        PrepareWriter();
        jsonWriter!.WriteStartObject();
        jsonWriter.WriteBoolean(IsErrorPropertyName, false);
        jsonWriter.WritePropertyName(ContentPropertyName);
        jsonWriter.WriteStartArray();

        foreach (Content? content in contents)
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString(TypePropertyName, "text");
            jsonWriter.WriteString(TextPropertyName, content?.Text ?? string.Empty);
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();
        return ConsumeWrittenBuffer();
    }

    protected int WriteCallToolResponse(McpDotNetCallToolResponse response)
    {
        PrepareWriter();
        jsonWriter!.WriteStartObject();
        jsonWriter.WriteBoolean(IsErrorPropertyName, response.IsError);
        jsonWriter.WritePropertyName(ContentPropertyName);
        jsonWriter.WriteStartArray();

        foreach (McpDotNetContent? content in response.Content)
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString(TypePropertyName, content?.Type ?? "text");
            jsonWriter.WriteString(TextPropertyName, content?.Text ?? string.Empty);
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();
        return ConsumeWrittenBuffer();
    }

    private void WriteInputSchema(ReadOnlySpan<McpParameterDescriptor> parameters)
    {
        jsonWriter!.WritePropertyName(InputSchemaPropertyName);
        jsonWriter.WriteStartObject();
        jsonWriter.WriteString(TypePropertyName, "object");
        jsonWriter.WritePropertyName(PropertiesPropertyName);
        jsonWriter.WriteStartObject();

        foreach (ref readonly McpParameterDescriptor parameter in parameters)
        {
            jsonWriter.WritePropertyName(parameter.Name);
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString(TypePropertyName, GetJsonType(parameter.ParameterType));
            if (parameter.Description is not null)
                jsonWriter.WriteString(DescriptionPropertyName, parameter.Description);
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndObject();
        jsonWriter.WritePropertyName(RequiredPropertyName);
        jsonWriter.WriteStartArray();
        foreach (ref readonly McpParameterDescriptor parameter in parameters)
        {
            if (parameter.Required)
                jsonWriter.WriteStringValue(parameter.Name);
        }

        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
    }

    private void WriteInputSchema(ReadOnlySpan<ToolParameterMetadata> parameters)
    {
        jsonWriter!.WritePropertyName(InputSchemaPropertyName);
        jsonWriter.WriteStartObject();
        jsonWriter.WriteString(TypePropertyName, "object");
        jsonWriter.WritePropertyName(PropertiesPropertyName);
        jsonWriter.WriteStartObject();

        foreach (ref readonly ToolParameterMetadata parameter in parameters)
        {
            jsonWriter.WritePropertyName(parameter.Name);
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString(TypePropertyName, parameter.JsonType);
            if (parameter.Description is not null)
                jsonWriter.WriteString(DescriptionPropertyName, parameter.Description);
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndObject();
        jsonWriter.WritePropertyName(RequiredPropertyName);
        jsonWriter.WriteStartArray();
        foreach (ref readonly ToolParameterMetadata parameter in parameters)
        {
            if (parameter.Required)
                jsonWriter.WriteStringValue(parameter.Name);
        }

        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
    }

    protected internal static string GetJsonType(Type parameterType)
    {
        if (parameterType == typeof(bool))
            return "boolean";

        if (parameterType == typeof(int) ||
            parameterType == typeof(long) ||
            parameterType == typeof(short) ||
            parameterType == typeof(byte))
        {
            return "integer";
        }

        if (parameterType == typeof(float) ||
            parameterType == typeof(double) ||
            parameterType == typeof(decimal))
        {
            return "number";
        }

        return "string";
    }

    private void PrepareWriter()
    {
        writerBuffer!.Clear();
        jsonWriter!.Reset(writerBuffer);
    }

    private int ConsumeWrittenBuffer()
    {
        ReadOnlySpan<byte> written = writerBuffer!.WrittenSpan;
        return written.Length == 0
            ? 0
            : written.Length + written[0] + written[^1];
    }
}

public readonly record struct ToolMetadata(string Name, string? Description, ToolParameterMetadata[] Parameters);

public readonly record struct ToolParameterMetadata(string Name, string JsonType, bool Required, string? Description);
