using Manifold.Generated;

namespace Manifold.Generators.Tests;

public sealed class GeneratedOperationRegistryTests
{
    [Fact]
    public void Operations_exposes_generated_descriptors()
    {
        Assert.Equal(4, GeneratedOperationRegistry.Operations.Count);
        Assert.Collection(
            GeneratedOperationRegistry.Operations.Select(static operation => operation.OperationId),
            static operationId => Assert.Equal("math.add", operationId),
            static operationId => Assert.Equal("sample.class-hello", operationId),
            static operationId => Assert.Equal("sample.hello", operationId),
            static operationId => Assert.Equal("weather.get", operationId));
    }

    [Fact]
    public void TryFind_returns_expected_method_metadata()
    {
        bool found = GeneratedOperationRegistry.TryFind("sample.hello", out OperationDescriptor? descriptor);

        Assert.True(found);
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(Samples.SampleOperations), descriptor!.DeclaringType);
        Assert.Equal("Hello", descriptor.MethodName);
        Assert.Equal(typeof(string), descriptor.ResultType);
        Assert.Equal(OperationVisibility.Both, descriptor.Visibility);
        Assert.Equal("Say hello.", descriptor.Description);
        Assert.Equal("Returns a greeting.", descriptor.Summary);
        Assert.True(descriptor.Hidden);
        Assert.Equal(["sample", "hello"], descriptor.CliCommandPath);
        Assert.Equal("sample_hello", descriptor.McpToolName);
    }

    [Fact]
    public void Parameters_capture_option_alias_service_and_cancellation_token_metadata()
    {
        bool found = GeneratedOperationRegistry.TryFind("math.add", out OperationDescriptor? addDescriptor);

        Assert.True(found);
        Assert.NotNull(addDescriptor);
        Assert.Equal(OperationVisibility.CliOnly, addDescriptor!.Visibility);
        Assert.Equal(typeof(int), addDescriptor.ResultType);
        Assert.NotNull(addDescriptor.CliCommandAliases);
        Assert.Collection(
            addDescriptor.CliCommandAliases!,
            static aliasPath => Assert.Equal(["math", "sum"], aliasPath),
            static aliasPath => Assert.Equal(["calc", "plus"], aliasPath));
        Assert.Collection(
            addDescriptor.Parameters,
            static parameter =>
            {
                Assert.Equal("x", parameter.Name);
                Assert.Equal(typeof(int), parameter.ParameterType);
                Assert.Equal(ParameterSource.Argument, parameter.Source);
                Assert.True(parameter.Required);
                Assert.Equal(0, parameter.Position);
                Assert.Equal("Left operand", parameter.Description);
            },
            static parameter =>
            {
                Assert.Equal("y", parameter.Name);
                Assert.Equal(typeof(int), parameter.ParameterType);
                Assert.Equal(ParameterSource.Argument, parameter.Source);
                Assert.True(parameter.Required);
                Assert.Equal(1, parameter.Position);
                Assert.Equal("Right operand", parameter.Description);
            },
            static parameter =>
            {
                Assert.Equal("services", parameter.Name);
                Assert.Equal(typeof(IServiceProvider), parameter.ParameterType);
                Assert.Equal(ParameterSource.Service, parameter.Source);
                Assert.False(parameter.Required);
                Assert.Null(parameter.Position);
            },
            static parameter =>
            {
                Assert.Equal("cancellationToken", parameter.Name);
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.Equal(ParameterSource.CancellationToken, parameter.Source);
                Assert.False(parameter.Required);
                Assert.Null(parameter.Position);
            });
    }

    [Fact]
    public void Parameters_capture_aliases_and_optional_mcp_only_metadata()
    {
        bool classHelloFound = GeneratedOperationRegistry.TryFind("sample.class-hello", out OperationDescriptor? classHelloDescriptor);
        bool helloFound = GeneratedOperationRegistry.TryFind("sample.hello", out OperationDescriptor? helloDescriptor);
        bool weatherFound = GeneratedOperationRegistry.TryFind("weather.get", out OperationDescriptor? weatherDescriptor);

        Assert.True(classHelloFound);
        Assert.True(helloFound);
        Assert.True(weatherFound);
        Assert.NotNull(classHelloDescriptor);
        Assert.NotNull(helloDescriptor);
        Assert.NotNull(weatherDescriptor);

        Assert.Equal(typeof(Samples.SampleClassHelloOperation), classHelloDescriptor!.DeclaringType);
        Assert.Equal("ExecuteAsync", classHelloDescriptor.MethodName);
        Assert.Equal(typeof(string), classHelloDescriptor.ResultType);
        Assert.Equal(["sample", "class-hello"], classHelloDescriptor.CliCommandPath);
        Assert.Equal("sample_class_hello", classHelloDescriptor.McpToolName);

        ParameterDescriptor classHelloParameter = Assert.Single(classHelloDescriptor.Parameters);
        Assert.Equal("name", classHelloParameter.Name);
        Assert.Equal("person", classHelloParameter.CliName);
        Assert.Equal("targetName", classHelloParameter.McpName);
        Assert.Equal(typeof(string), classHelloParameter.ParameterType);
        Assert.Equal(["n", "username"], classHelloParameter.Aliases);

        ParameterDescriptor helloParameter = Assert.Single(
            helloDescriptor!.Parameters,
            static parameter => parameter.Source == ParameterSource.Option);
        Assert.Equal("name", helloParameter.Name);
        Assert.Equal("person", helloParameter.CliName);
        Assert.Equal("targetName", helloParameter.McpName);
        Assert.Equal(typeof(string), helloParameter.ParameterType);
        Assert.Equal(["n", "username"], helloParameter.Aliases);

        Assert.Equal(OperationVisibility.McpOnly, weatherDescriptor!.Visibility);
        Assert.Equal(typeof(string), weatherDescriptor.ResultType);
        Assert.Null(weatherDescriptor.CliCommandPath);
        Assert.Equal("weather_fetch", weatherDescriptor.McpToolName);

        ParameterDescriptor weatherParameter = Assert.Single(weatherDescriptor.Parameters);
        Assert.Equal("city", weatherParameter.Name);
        Assert.Equal(typeof(string), weatherParameter.ParameterType);
        Assert.False(weatherParameter.Required);
        Assert.Equal("Target city", weatherParameter.Description);
    }
}


