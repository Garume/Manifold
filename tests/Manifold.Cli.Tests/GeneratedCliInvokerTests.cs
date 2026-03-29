using Manifold.Cli.Tests.Samples;
using Manifold.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Manifold.Cli.Tests;

public sealed class GeneratedCliInvokerTests
{
    [Fact]
    public async Task Generated_fast_invoker_can_invoke_async_operations()
    {
        IServiceProvider serviceProvider = CreateServices();
        GeneratedCliInvoker invoker = new();

        Assert.True(invoker.TryInvokeFast(["math", "add", "4", "5"], serviceProvider, TestContext.Current.CancellationToken, out ValueTask<FastCliInvocationResult> invocation));

        FastCliInvocationResult result = await invocation;

        Assert.Equal(FastCliInvocationKind.Number, result.Kind);
        Assert.Equal(16, result.Number);
    }

    [Fact]
    public void Generated_fast_sync_invoker_can_invoke_sync_operations_without_value_task_overhead()
    {
        IServiceProvider serviceProvider = CreateServices();
        GeneratedCliInvoker invoker = new();

        Assert.True(invoker.TryInvokeFastSync(["sample", "hello", "--person", "Alice"], serviceProvider, TestContext.Current.CancellationToken, out FastCliInvocationResult helloInvocation));
        Assert.False(invoker.TryInvokeFastSync(["math", "add", "4", "5"], serviceProvider, TestContext.Current.CancellationToken, out FastCliInvocationResult addInvocation));

        Assert.Equal(FastCliInvocationKind.Text, helloInvocation.Kind);
        Assert.Equal("Hello, Alice", helloInvocation.Text);
        Assert.Equal(default, addInvocation);
    }

    [Fact]
    public async Task Generated_invoker_supports_optional_non_nullable_value_type_options_on_class_based_operations()
    {
        IServiceProvider serviceProvider = CreateServices();
        GeneratedCliInvoker invoker = new();

        Dictionary<string, string> noOptions = [];
        List<string> noArguments = [];
        Dictionary<string, string> providedOptions = new(StringComparer.Ordinal)
        {
            ["count"] = "5"
        };

        Assert.True(invoker.TryInvoke(
            "counter.preview",
            noOptions,
            noArguments,
            serviceProvider,
            jsonRequested: false,
            TestContext.Current.CancellationToken,
            out ValueTask<CliInvocationResult> defaultInvocation));

        Assert.True(invoker.TryInvoke(
            "counter.preview",
            providedOptions,
            noArguments,
            serviceProvider,
            jsonRequested: false,
            TestContext.Current.CancellationToken,
            out ValueTask<CliInvocationResult> providedInvocation));

        CliInvocationResult defaultResult = await defaultInvocation;
        CliInvocationResult providedResult = await providedInvocation;

        Assert.Equal(2, Assert.IsType<int>(defaultResult.Result));
        Assert.Equal(5, Assert.IsType<int>(providedResult.Result));
    }

    private static ServiceProvider CreateServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<IMathOffsetProvider>(new ConstantMathOffsetProvider(7));
        services.AddSingleton<WeatherPreviewFormatter>();
        services.AddTransient<MathScaleOperation>();
        services.AddTransient<CounterPreviewOperation>();
        return services.BuildServiceProvider();
    }
}
