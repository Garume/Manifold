using System.Diagnostics;

namespace Manifold.Samples.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SampleHostsCollectionDefinition
{
    public const string Name = "sample-hosts";
}

[Collection(SampleHostsCollectionDefinition.Name)]
public sealed class SampleHostSmokeTests
{
    [Fact]
    public async Task Cli_sample_host_executes_a_command()
    {
        string sampleDllPath = await BuildSampleAsync(
            @"samples\Manifold.Samples.CliHost\Manifold.Samples.CliHost.csproj",
            TestContext.Current.CancellationToken);

        using Process process = StartDotNetProcess(sampleDllPath, ["math", "add", "2", "3"]);

        string stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, $"CLI sample host exited with code {process.ExitCode}.{Environment.NewLine}{stderr}");
        Assert.Equal("5", stdout.Trim());
    }

    [Fact]
    public async Task Mcp_http_sample_host_serves_root_endpoint()
    {
        string sampleDllPath = await BuildSampleAsync(
            @"samples\Manifold.Samples.McpHttpHost\Manifold.Samples.McpHttpHost.csproj",
            TestContext.Current.CancellationToken);

        using Process process = StartDotNetProcess(sampleDllPath, []);
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            string response = await WaitForHttpRootAsync(client, process, TestContext.Current.CancellationToken);

            Assert.Contains("Manifold MCP sample host is running.", response, StringComparison.Ordinal);
        }
        finally
        {
            await StopProcessAsync(process);
        }
    }

    [Fact]
    public async Task Mcp_stdio_sample_host_starts_and_stays_alive()
    {
        string sampleDllPath = await BuildSampleAsync(
            @"samples\Manifold.Samples.McpStdioHost\Manifold.Samples.McpStdioHost.csproj",
            TestContext.Current.CancellationToken);

        using Process process = StartDotNetProcess(sampleDllPath, []);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), TestContext.Current.CancellationToken);

            if (process.HasExited)
            {
                throw new Xunit.Sdk.XunitException(await CreateUnexpectedExitMessageAsync(process));
            }
        }
        finally
        {
            await StopProcessAsync(process);
        }
    }

    private static async Task<string> BuildSampleAsync(string relativeProjectPath, CancellationToken cancellationToken)
    {
        string repositoryRoot = GetRepositoryRoot();
        string projectPath = Path.Combine(repositoryRoot, relativeProjectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not resolve project directory for '{projectPath}'.");
        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        string outputDllPath = Path.Combine(projectDirectory, "bin", "Debug", "net10.0", projectName + ".dll");

        ProcessStartInfo startInfo = new(GetDotNetHostPath())
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start dotnet build for '{projectPath}'.");

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"Building sample '{relativeProjectPath}' failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }

        if (!File.Exists(outputDllPath))
        {
            throw new Xunit.Sdk.XunitException($"Expected sample output '{outputDllPath}' was not produced.");
        }

        return outputDllPath;
    }

    private static Process StartDotNetProcess(string dllPath, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new(GetDotNetHostPath())
        {
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(dllPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start sample host '{dllPath}'.");
    }

    private static async Task<string> WaitForHttpRootAsync(HttpClient client, Process process, CancellationToken cancellationToken)
    {
        string endpoint = "http://127.0.0.1:38474/";
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new Xunit.Sdk.XunitException(await CreateUnexpectedExitMessageAsync(process));
            }

            try
            {
                return await client.GetStringAsync(endpoint, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                await Task.Delay(200, cancellationToken);
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"HTTP sample host did not respond on '{endpoint}' within the timeout.{Environment.NewLine}{lastError}");
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static async Task<string> CreateUnexpectedExitMessageAsync(Process process)
    {
        string stdout = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        string stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
        return $"Sample host exited unexpectedly with code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}";
    }

    private static string GetDotNetHostPath()
    {
        string? dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(dotnetHostPath) ? "dotnet" : dotnetHostPath;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string solutionPath = Path.Combine(directory.FullName, "Manifold.slnx");
            if (File.Exists(solutionPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
