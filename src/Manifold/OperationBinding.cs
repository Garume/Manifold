namespace Manifold;

public static class OperationBinding
{
    public static TService GetRequiredService<TService>(IServiceProvider? services)
        where TService : class
    {
        object? service = services?.GetService(typeof(TService));
        if (service is TService typedService)
            return typedService;

        throw new InvalidOperationException($"Required service '{typeof(TService).FullName}' was not available.");
    }
}
