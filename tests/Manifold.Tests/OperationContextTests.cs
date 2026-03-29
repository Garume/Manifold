namespace Manifold.Tests;

public sealed class OperationContextTests
{
    [Fact]
    public void ForCli_CreatesCliContext()
    {
        OperationContext context = OperationContext.ForCli("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello", context.OperationId);
        Assert.Equal(InvocationSurface.Cli, context.Surface);
    }

    [Fact]
    public void GetService_ReturnsRegisteredService()
    {
        TestService expected = new();
        OperationContext context = new(
            "hello",
            InvocationSurface.Mcp,
            new DictionaryServiceProvider(new Dictionary<Type, object>
            {
                [typeof(TestService)] = expected
            }));

        TestService? actual = context.GetService<TestService>();

        Assert.Same(expected, actual);
    }

    [Fact]
    public void GetRequiredService_ThrowsWhenMissing()
    {
        OperationContext context = OperationContext.ForMcp("hello", cancellationToken: TestContext.Current.CancellationToken);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            context.GetRequiredService<TestService>());

        string expectedName = typeof(TestService).FullName ?? nameof(TestService);
        Assert.Contains(expectedName, exception.Message, StringComparison.Ordinal);
    }

    private sealed class TestService;

    private sealed class DictionaryServiceProvider(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return services.TryGetValue(serviceType, out object? service) ? service : null;
        }
    }
}


