using System.Diagnostics.CodeAnalysis;
using Manifold.Cli.Tests.Samples;
using Manifold.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Manifold.Cli.Tests;

public sealed class CliPerformanceTests
{
    [Fact]
    public void TryFindOptionValue_primary_name_does_not_allocate()
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Alice"
        };

        _ = CliBinding.TryFindOptionValue(options, "name", aliases: null, out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool found = CliBinding.TryFindOptionValue(options, "name", aliases: null, out string? value);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(found);
        Assert.Equal("Alice", value);
        Assert.Equal(0, after - before);
    }

    [Fact]
    [SuppressMessage(
        "xUnit",
        "xUnit1031:Test methods should not use blocking task operations",
        Justification = "Allocation measurement must stay on the current thread.")]
    public void ExecuteAsync_common_path_stays_under_allocation_budget()
    {
        CliApplication application = CreateApplication();

        _ = application.ExecuteAsync(["math", "add", "4", "5"], TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();

        long before = GC.GetAllocatedBytesForCurrentThread();
        int exitCode = application.ExecuteAsync(["math", "add", "4", "5"], TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.InRange(after - before, 0, 1024);
    }

    [Fact]
    [SuppressMessage(
        "xUnit",
        "xUnit1031:Test methods should not use blocking task operations",
        Justification = "Allocation measurement must stay on the current thread.")]
    public void ExecuteAsync_option_path_stays_under_allocation_budget()
    {
        CliApplication application = CreateApplication();

        _ = application.ExecuteAsync(
                ["weather", "preview", "--city", "Tokyo", "--temperature", "24"],
                TextWriter.Null,
                TextWriter.Null,
                TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();

        long before = GC.GetAllocatedBytesForCurrentThread();
        int exitCode = application.ExecuteAsync(
                ["weather", "preview", "--city", "Tokyo", "--temperature", "24"],
                TextWriter.Null,
                TextWriter.Null,
                TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.InRange(after - before, 0, 2 * 1024);
    }

    private static CliApplication CreateApplication()
    {
        return new CliApplication(
            GeneratedOperationRegistry.Operations,
            new GeneratedCliInvoker(),
            CreateServices());
    }

    private static ServiceProvider CreateServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<IMathOffsetProvider>(new ConstantMathOffsetProvider(7));
        services.AddSingleton<WeatherPreviewFormatter>();
        services.AddTransient<MathScaleOperation>();
        return services.BuildServiceProvider();
    }
}
