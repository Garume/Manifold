using System.ComponentModel;
using System.Reflection;
using Manifold.Generated;
using Manifold.Mcp.Tests.Samples;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Manifold.Mcp.Tests;

public sealed class GeneratedMcpToolsTests
{
    [Fact]
    public void Generated_tools_type_exposes_only_mcp_visible_operations()
    {
        Type generatedType = typeof(GeneratedMcpTools);

        Assert.NotNull(generatedType.GetCustomAttributes(typeof(McpServerToolTypeAttribute), inherit: false).SingleOrDefault());

        Dictionary<string, string?> toolMethods = generatedType
            .GetMethods()
            .Select(static method => new
            {
                Method = method,
                Tool = method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
                    .Cast<McpServerToolAttribute>()
                    .SingleOrDefault()
            })
            .Where(static candidate => candidate.Tool is not null)
            .ToDictionary(
                static candidate => candidate.Method.Name,
                static candidate => candidate.Tool!.Name,
                StringComparer.Ordinal);

        Assert.Equal(4, toolMethods.Count);
        Assert.Contains(toolMethods, static candidate => candidate.Value == "forecast_preview");
        Assert.Contains(toolMethods, static candidate => candidate.Value == "sample_hello");
        Assert.Contains(toolMethods, static candidate => candidate.Value == "sample_class_hello_instance");
        Assert.Contains(toolMethods, static candidate => candidate.Value == "math.sum");
        Assert.DoesNotContain(toolMethods, static candidate => candidate.Value == "internal.cli-only");
    }

    [Fact]
    public async Task Generated_tools_can_invoke_bound_methods()
    {
        ServiceCollection services = new();
        services.AddSingleton<IGreetingPrefixProvider>(new ConstantGreetingPrefixProvider("Hello, "));
        services.AddTransient<SampleClassHelloOperation>();
        IServiceProvider serviceProvider = services.BuildServiceProvider();

        GeneratedMcpTools tools = new(serviceProvider);

        string hello = await tools.SampleHelloAsync("Alice", TestContext.Current.CancellationToken);
        string classHello = await tools.SampleClassHelloAsync("Bob");
        int sum = await tools.MathSumAsync(4, 5);

        Assert.Equal("Hello, Alice:True", hello);
        Assert.Equal("Hello, Bob:Mcp", classHello);
        Assert.Equal(9, sum);
    }

    [Fact]
    public void Generated_tools_preserve_method_and_parameter_descriptions()
    {
        MethodInfo method = typeof(GeneratedMcpTools).GetMethod(nameof(GeneratedMcpTools.SampleHelloAsync))
                            ?? throw new InvalidOperationException("Missing generated MCP tool method.");
        DescriptionAttribute description = Assert.Single(method.GetCustomAttributes<DescriptionAttribute>());

        Assert.Equal("Say hello.", description.Description);

        Assert.DoesNotContain(method.GetParameters(), static parameter => parameter.Name == "name");
        ParameterInfo renamedParameter = method.GetParameters().First(static parameter => parameter.Name == "targetName");
        DescriptionAttribute parameterDescription = Assert.Single(renamedParameter.GetCustomAttributes<DescriptionAttribute>());

        Assert.Equal("User name", parameterDescription.Description);
    }

    [Fact]
    public void AddGeneratedMcpServer_registers_generated_tools()
    {
        ServiceCollection services = new();

        IMcpServerBuilder builder = services.AddGeneratedMcpServer();

        Assert.NotNull(builder);
    }
}


