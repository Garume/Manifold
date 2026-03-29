namespace Manifold.Mcp;

public static partial class McpBinding
{
    public static TService GetRequiredServiceOrThrow<TService>(IServiceProvider? services)
        where TService : class
    {
        object? service = services?.GetService(typeof(TService));
        if (service is TService typedService)
            return typedService;

        throw new InvalidOperationException($"Required service '{typeof(TService).FullName}' was not available.");
    }

    public static TService GetRequiredService<TService>(IServiceProvider? services)
        where TService : class
    {
        return (TService)GetRequiredService(services, typeof(TService));
    }

    public static object GetRequiredService(IServiceProvider? services, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        object? service = services?.GetService(serviceType);
        if (service is not null)
            return service;

        throw new InvalidOperationException($"Required service '{serviceType.FullName}' was not available.");
    }
}


