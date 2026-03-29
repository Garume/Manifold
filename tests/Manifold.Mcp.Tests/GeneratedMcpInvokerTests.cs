using System.Text.Json;
using Manifold.Generated;
using Manifold.Mcp.Tests.Samples;
using Microsoft.Extensions.DependencyInjection;

namespace Manifold.Mcp.Tests;

public sealed class GeneratedMcpInvokerTests
{
    [Fact]
    public async Task Generated_invoker_can_invoke_static_and_instance_tools_from_json()
    {
        ServiceCollection services = new();
        services.AddSingleton<IGreetingPrefixProvider>(new ConstantGreetingPrefixProvider("Hello, "));
        services.AddTransient<SampleClassHelloOperation>();
        services.AddTransient<ForecastPreviewOperation>();
        IServiceProvider serviceProvider = services.BuildServiceProvider();
        GeneratedMcpInvoker invoker = new();

        JsonElement helloArguments = JsonSerializer.Deserialize<JsonElement>("{\"targetName\":\"Alice\"}");
        JsonElement classHelloArguments = JsonSerializer.Deserialize<JsonElement>("{\"targetName\":\"Bob\"}");
        JsonElement sumArguments = JsonSerializer.Deserialize<JsonElement>("{\"x\":4,\"y\":5}");

        Assert.True(invoker.TryInvoke("sample_hello", helloArguments, serviceProvider, TestContext.Current.CancellationToken, out ValueTask<OperationInvocationResult> helloInvocation));
        Assert.True(invoker.TryInvoke("sample_class_hello_instance", classHelloArguments, serviceProvider, CancellationToken.None, out ValueTask<OperationInvocationResult> classHelloInvocation));
        Assert.True(invoker.TryInvoke("math.sum", sumArguments, serviceProvider, CancellationToken.None, out ValueTask<OperationInvocationResult> sumInvocation));

        OperationInvocationResult hello = await helloInvocation;
        OperationInvocationResult classHello = await classHelloInvocation;
        OperationInvocationResult sum = await sumInvocation;

        Assert.Equal("Hello, Alice:True", hello.Result);
        Assert.Equal("Hello, Bob:Mcp", classHello.Result);
        Assert.Equal(9, sum.Result);
    }

    [Fact]
    public async Task Generated_fast_invoker_can_invoke_static_and_instance_tools_without_boxing_scalar_results()
    {
        ServiceCollection services = new();
        services.AddSingleton<IGreetingPrefixProvider>(new ConstantGreetingPrefixProvider("Hello, "));
        services.AddTransient<SampleClassHelloOperation>();
        services.AddTransient<ForecastPreviewOperation>();
        IServiceProvider serviceProvider = services.BuildServiceProvider();
        GeneratedMcpInvoker invoker = new();

        JsonElement helloArguments = JsonSerializer.Deserialize<JsonElement>("{\"targetName\":\"Alice\"}");
        JsonElement sumArguments = JsonSerializer.Deserialize<JsonElement>("{\"x\":4,\"y\":5}");

        Assert.True(invoker.TryInvokeFast("sample_hello", helloArguments, serviceProvider, TestContext.Current.CancellationToken, out ValueTask<FastMcpInvocationResult> helloInvocation));
        Assert.True(invoker.TryInvokeFast("math.sum", sumArguments, serviceProvider, CancellationToken.None, out ValueTask<FastMcpInvocationResult> sumInvocation));

        FastMcpInvocationResult hello = await helloInvocation;
        FastMcpInvocationResult sum = await sumInvocation;

        Assert.Equal(FastMcpInvocationKind.Text, hello.Kind);
        Assert.Equal("Hello, Alice:True", hello.Text);
        Assert.Equal(FastMcpInvocationKind.Number, sum.Kind);
        Assert.Equal(9, sum.Number);
        Assert.Null(sum.StructuredValue);
    }

    [Fact]
    public void Generated_fast_sync_invoker_can_invoke_sync_tools_without_value_task_overhead()
    {
        ServiceCollection services = new();
        services.AddSingleton<IGreetingPrefixProvider>(new ConstantGreetingPrefixProvider("Hello, "));
        services.AddTransient<SampleClassHelloOperation>();
        services.AddTransient<ForecastPreviewOperation>();
        IServiceProvider serviceProvider = services.BuildServiceProvider();
        GeneratedMcpInvoker invoker = new();

        JsonElement helloArguments = JsonSerializer.Deserialize<JsonElement>("{\"targetName\":\"Alice\"}");
        JsonElement sumArguments = JsonSerializer.Deserialize<JsonElement>("{\"x\":4,\"y\":5}");

        Assert.True(invoker.TryInvokeFastSync("sample_hello", helloArguments, serviceProvider, TestContext.Current.CancellationToken, out FastMcpInvocationResult helloInvocation));
        Assert.False(invoker.TryInvokeFastSync("math.sum", sumArguments, serviceProvider, CancellationToken.None, out FastMcpInvocationResult sumInvocation));

        Assert.Equal(FastMcpInvocationKind.Text, helloInvocation.Kind);
        Assert.Equal("Hello, Alice:True", helloInvocation.Text);
        Assert.Equal(default, sumInvocation);
    }

    [Fact]
    public async Task Generated_invoker_supports_optional_non_nullable_value_type_options_on_class_based_tools()
    {
        ServiceCollection services = new();
        services.AddTransient<ForecastPreviewOperation>();
        IServiceProvider serviceProvider = services.BuildServiceProvider();
        GeneratedMcpInvoker invoker = new();

        JsonElement noArguments = JsonSerializer.Deserialize<JsonElement>("{}");
        JsonElement providedArguments = JsonSerializer.Deserialize<JsonElement>("{\"days\":5}");

        Assert.True(invoker.TryInvoke(
            "forecast_preview",
            noArguments,
            serviceProvider,
            TestContext.Current.CancellationToken,
            out ValueTask<OperationInvocationResult> defaultObjectInvocation));

        Assert.True(invoker.TryInvoke(
            "forecast_preview",
            providedArguments,
            serviceProvider,
            TestContext.Current.CancellationToken,
            out ValueTask<OperationInvocationResult> providedObjectInvocation));

        Assert.True(invoker.TryInvokeFast(
            "forecast_preview",
            noArguments,
            serviceProvider,
            TestContext.Current.CancellationToken,
            out ValueTask<FastMcpInvocationResult> defaultInvocation));

        Assert.True(invoker.TryInvokeFast(
            "forecast_preview",
            providedArguments,
            serviceProvider,
            TestContext.Current.CancellationToken,
            out ValueTask<FastMcpInvocationResult> providedInvocation));

        OperationInvocationResult defaultObjectResult = await defaultObjectInvocation;
        OperationInvocationResult providedObjectResult = await providedObjectInvocation;
        FastMcpInvocationResult defaultResult = await defaultInvocation;
        FastMcpInvocationResult providedResult = await providedInvocation;

        Assert.Equal(2, Assert.IsType<int>(defaultObjectResult.Result));
        Assert.Equal(5, Assert.IsType<int>(providedObjectResult.Result));
        Assert.Equal(FastMcpInvocationKind.Number, defaultResult.Kind);
        Assert.Equal(2, defaultResult.Number);
        Assert.Equal(FastMcpInvocationKind.Number, providedResult.Kind);
        Assert.Equal(5, providedResult.Number);
    }

    [Fact]
    public void Generated_invoker_returns_false_for_unknown_tool()
    {
        GeneratedMcpInvoker invoker = new();

        bool found = invoker.TryInvoke("missing_tool", null, services: null, CancellationToken.None, out ValueTask<OperationInvocationResult> invocation);

        Assert.False(found);
        Assert.Equal(default, invocation);
    }

    [Fact]
    public void Generated_fast_invoker_returns_false_for_unknown_tool()
    {
        GeneratedMcpInvoker invoker = new();

        bool found = invoker.TryInvokeFast("missing_tool", null, services: null, CancellationToken.None, out ValueTask<FastMcpInvocationResult> invocation);

        Assert.False(found);
        Assert.Equal(default, invocation);
    }

    [Fact]
    public void Generated_fast_sync_invoker_returns_false_for_unknown_tool()
    {
        GeneratedMcpInvoker invoker = new();

        bool found = invoker.TryInvokeFastSync("missing_tool", null, services: null, CancellationToken.None, out FastMcpInvocationResult invocation);

        Assert.False(found);
        Assert.Equal(default, invocation);
    }
}
