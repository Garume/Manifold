using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using ConsoleAppFramework;
using Manifold.Cli;
using Manifold.Generated;
using Manifold.Benchmarks.Samples;
using System.CommandLine;

namespace Manifold.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public abstract class CliBenchmarkBase
{
    protected static readonly string[] AddArguments = ["math", "add", "4", "5"];
    protected static readonly string[] WeatherArguments = ["weather", "preview", "--city", "Tokyo", "--days", "3"];

    private TextWriter? nullWriter;
    private RootCommand? rootCommand;
    private ConsoleApp.ConsoleAppBuilder? consoleApp;
    private CliApplication? manifoldApp;

    protected int NumberSink { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        nullWriter = TextWriter.Null;
        manifoldApp = new CliApplication(
            GeneratedOperationRegistry.Operations,
            new GeneratedCliInvoker(),
            NullServiceProvider.Instance);
        rootCommand = CreateSystemCommandLine();
        consoleApp = CreateConsoleAppFramework();
    }

    protected int RunManifold(string[] arguments)
    {
        BenchmarkSink.NumberValue = 0;
        return manifoldApp!.ExecuteAsync(arguments, nullWriter!, nullWriter!, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    protected int RunSystemCommandLine(string[] arguments)
    {
        return rootCommand!.Parse(arguments).Invoke();
    }

    protected int RunConsoleAppFramework(string[] arguments)
    {
        consoleApp!.Run(arguments, disposeServiceProvider: false, CancellationToken.None);
        return 0;
    }

    private static RootCommand CreateSystemCommandLine()
    {
        RootCommand root = [];

        Command math = new("math");
        Command add = new("add");
        Argument<int> xArgument = new("x");
        Argument<int> yArgument = new("y");
        add.Add(xArgument);
        add.Add(yArgument);
        add.SetAction(parseResult =>
        {
            BenchmarkSink.NumberValue = parseResult.GetValue(xArgument) + parseResult.GetValue(yArgument);
            return 0;
        });
        math.Add(add);
        root.Add(math);

        Command weather = new("weather");
        Command preview = new("preview");
        Option<string> cityOption = new("--city");
        Option<int> daysOption = new("--days");
        preview.Add(cityOption);
        preview.Add(daysOption);
        preview.SetAction(parseResult =>
        {
            string city = parseResult.GetValue(cityOption) ?? string.Empty;
            int days = parseResult.GetValue(daysOption);
            BenchmarkSink.NumberValue = city.Length + days;
            return 0;
        });
        weather.Add(preview);
        root.Add(weather);

        return root;
    }

    private static ConsoleApp.ConsoleAppBuilder CreateConsoleAppFramework()
    {
        ConsoleApp.ConsoleAppBuilder app = ConsoleApp.Create();
        app.Add("math add", ([ConsoleAppFramework.Argument] int x, [ConsoleAppFramework.Argument] int y) => BenchmarkSink.NumberValue = x + y);
        app.Add("weather preview", (string city, int days) =>
        {
            BenchmarkSink.NumberValue = city.Length + days;
        });

        return app;
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

public class CliPositionalBenchmarks : CliBenchmarkBase
{
    [Benchmark(Baseline = true)]
    public int Manifold()
    {
        return RunManifold(AddArguments);
    }

    [Benchmark]
    public int SystemCommandLine()
    {
        return RunSystemCommandLine(AddArguments);
    }

    [Benchmark]
    public int ConsoleAppFramework()
    {
        return RunConsoleAppFramework(AddArguments);
    }
}

public class CliOptionBenchmarks : CliBenchmarkBase
{
    [Benchmark(Baseline = true)]
    public int Manifold()
    {
        return RunManifold(WeatherArguments);
    }

    [Benchmark]
    public int SystemCommandLine()
    {
        return RunSystemCommandLine(WeatherArguments);
    }

    [Benchmark]
    public int ConsoleAppFramework()
    {
        return RunConsoleAppFramework(WeatherArguments);
    }
}
