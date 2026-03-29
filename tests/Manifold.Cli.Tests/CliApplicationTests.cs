using Manifold.Cli.Tests.Samples;
using Manifold.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Manifold.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public void GetUsage_excludes_hidden_and_mcp_only_operations()
    {
        CliApplication application = CreateApplication();

        string usage = application.GetUsage("dalamudmcp");

        Assert.Contains("dalamudmcp math add <x> <y>", usage);
        Assert.Contains("Aliases: math sum, calc plus", usage);
        Assert.Contains("dalamudmcp weather preview --city <value>", usage);
        Assert.DoesNotContain("sample hello", usage);
        Assert.DoesNotContain("internal ping", usage);
    }

    [Fact]
    public async Task ExecuteAsync_invokes_argument_and_service_bound_operation()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["math", "add", "4", "5"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal("16" + Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_invokes_instance_operation_bound_from_services()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["math", "scale", "4"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal("28" + Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_supports_method_level_cli_aliases()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int firstExitCode = await application.ExecuteAsync(["math", "sum", "4", "5"], output, error, TestContext.Current.CancellationToken);
        int secondExitCode = await application.ExecuteAsync(["calc", "plus", "1", "2"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, firstExitCode);
        Assert.Equal(CliExitCodes.Success, secondExitCode);
        Assert.Equal($"16{Environment.NewLine}10{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_supports_hidden_command_and_option_alias()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["sample", "hello", "--n", "Alice"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal("Hello, Alice" + Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_supports_parameter_level_cli_name_override()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["sample", "hello", "--person", "Bob"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal("Hello, Bob" + Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_supports_formatter_and_json_output()
    {
        CliApplication application = CreateApplication();
        StringWriter textOutput = new();
        StringWriter textError = new();

        int textExitCode = await application.ExecuteAsync(["weather", "preview", "--city", "Tokyo"], textOutput, textError, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, textExitCode);
        Assert.Equal("Tokyo:20:Cli" + Environment.NewLine, textOutput.ToString());
        Assert.Equal(string.Empty, textError.ToString());

        StringWriter jsonOutput = new();
        StringWriter jsonError = new();
        int jsonExitCode = await application.ExecuteAsync(["weather", "preview", "--city", "Tokyo", "--json"], jsonOutput, jsonError, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, jsonExitCode);
        Assert.Contains("\"city\": \"Tokyo\"", jsonOutput.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"temperature\": 20", jsonOutput.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, jsonError.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_returns_usage_error_for_missing_required_argument()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["math", "add", "7"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Missing required argument 'y'.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_returns_usage_error_for_mcp_only_operation()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["internal", "ping"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Unknown command 'internal ping'.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_returns_command_help()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["weather", "preview", "--help"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage: app weather preview --city <value> [--temperature <value>]", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_returns_command_help_with_aliases()
    {
        CliApplication application = CreateApplication();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["math", "add", "--help"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Aliases: math sum, calc plus", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_returns_unavailable_when_generated_invoker_does_not_cover_operation()
    {
        CliApplication application = CreateApplication(new NullCliInvoker());
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["math", "add", "2", "3"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Unavailable, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("No generated CLI invoker was available for operation 'math.add'.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_writes_raw_json_payload_without_reserializing()
    {
        await using MemoryStream rawOutput = new();
        CliApplication application = CreateApplication(new RawJsonCliInvoker(), rawOutput);
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await application.ExecuteAsync(["math", "add", "2", "3", "--json"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal("{\"sum\":5}\n", System.Text.Encoding.UTF8.GetString(rawOutput.ToArray()));
        Assert.Equal(string.Empty, error.ToString());
    }

    private static CliApplication CreateApplication(ICliInvoker? cliInvoker = null, Stream? rawOutput = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<IMathOffsetProvider>(new ConstantMathOffsetProvider(7));
        services.AddSingleton<WeatherPreviewFormatter>();
        services.AddTransient<MathScaleOperation>();

        return new CliApplication(
            GeneratedOperationRegistry.Operations,
            cliInvoker ?? new GeneratedCliInvoker(),
            services.BuildServiceProvider(),
            rawOutput);
    }

    private sealed class NullCliInvoker : ICliInvoker
    {
        public bool TryInvoke(
            string operationId,
            IReadOnlyDictionary<string, string> options,
            IReadOnlyList<string> arguments,
            IServiceProvider? services,
            bool jsonRequested,
            CancellationToken cancellationToken,
            out ValueTask<CliInvocationResult> invocation)
        {
            invocation = default;
            return false;
        }
    }

    private sealed class RawJsonCliInvoker : ICliInvoker
    {
        public bool TryInvoke(
            string operationId,
            IReadOnlyDictionary<string, string> options,
            IReadOnlyList<string> arguments,
            IServiceProvider? services,
            bool jsonRequested,
            CancellationToken cancellationToken,
            out ValueTask<CliInvocationResult> invocation)
        {
            invocation = ValueTask.FromResult(new CliInvocationResult(null, typeof(object), null, "{\"sum\":5}"u8.ToArray()));
            return true;
        }
    }
}


