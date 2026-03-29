using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Manifold.Generators.Tests;

public sealed class OperationDescriptorGeneratorDiagnosticsTests
{
    [Fact]
    public void Run_generates_diagnostic_for_conflicting_visibility()
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator("""
            using Manifold;

            public static class SampleOperations
            {
                [Operation("sample.conflict")]
                [CliOnly]
                [McpOnly]
                public static string Conflict([Argument(0)] string name)
                {
                    return name;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, static candidate => candidate.Id == "DMCF001");
        Assert.Contains("both [CliOnly] and [McpOnly]", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_generates_diagnostic_for_conflicting_parameter_binding()
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator("""
            using Manifold;

            public static class SampleOperations
            {
                [Operation("sample.conflict-parameter")]
                public static string Conflict([Option("name")][Argument(0)] string name)
                {
                    return name;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, static candidate => candidate.Id == "DMCF002");
        Assert.Contains("both [Option] and [Argument]", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_generates_diagnostic_for_unbound_parameter()
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator("""
            using Manifold;

            public static class SampleOperations
            {
                [Operation("sample.unbound")]
                public static string Unbound(string name)
                {
                    return name;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, static candidate => candidate.Id == "DMCF003");
        Assert.Contains("must be bound", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_generates_diagnostic_for_invalid_operation_class_shape()
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator("""
            using Manifold;

            [Operation("sample.invalid-class")]
            public sealed class InvalidOperation
            {
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, static candidate => candidate.Id == "DMCF004");
        Assert.Contains("must implement IOperation", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_generates_diagnostic_for_read_only_request_property()
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator("""
            using Manifold;

            [Operation("sample.invalid-request")]
            public sealed class InvalidRequestOperation : IOperation<InvalidRequestOperation.Request, string>
            {
                public ValueTask<string> ExecuteAsync(Request request, OperationContext context)
                {
                    return ValueTask.FromResult(request.Name);
                }

                public sealed class Request
                {
                    [Option("name")]
                    public string Name => "blocked";
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, static candidate => candidate.Id == "DMCF005");
        Assert.Contains("must be writable", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "Manifold.Generators.Diagnostics.Tests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::Manifold.Generators.OperationDescriptorGenerator());
        GeneratorDriverRunResult runResult = driver.RunGenerators(compilation).GetRunResult();
        return [.. runResult.Diagnostics];
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        string trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                                         ?? throw new InvalidOperationException("Could not resolve trusted platform assemblies.");
        HashSet<string> paths = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        paths.Add(typeof(OperationAttribute).Assembly.Location);

        foreach (string path in paths)
            yield return MetadataReference.CreateFromFile(path);
    }
}



