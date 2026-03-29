namespace Manifold;

public sealed class OperationContext
{
    public OperationContext(
        string? operationId,
        InvocationSurface surface,
        IServiceProvider? services = null,
        object? state = null,
        CancellationToken cancellationToken = default)
    {
        OperationId = string.IsNullOrWhiteSpace(operationId) ? null : operationId.Trim();
        Surface = surface;
        Services = services;
        CancellationToken = cancellationToken;
        State = state;
    }

    public string? OperationId { get; }

    public InvocationSurface Surface { get; }

    public IServiceProvider? Services { get; }

    public CancellationToken CancellationToken { get; }

    public object? State { get; }

    public static OperationContext ForCli(
        string? operationId = null,
        IServiceProvider? services = null,
        object? state = null,
        CancellationToken cancellationToken = default)
    {
        return new OperationContext(operationId, InvocationSurface.Cli, services, state, cancellationToken);
    }

    public static OperationContext ForMcp(
        string? operationId = null,
        IServiceProvider? services = null,
        object? state = null,
        CancellationToken cancellationToken = default)
    {
        return new OperationContext(operationId, InvocationSurface.Mcp, services, state, cancellationToken);
    }

    public static OperationContext ForProtocol(
        string? operationId = null,
        IServiceProvider? services = null,
        object? state = null,
        CancellationToken cancellationToken = default)
    {
        return new OperationContext(operationId, InvocationSurface.Protocol, services, state, cancellationToken);
    }

    public TService? GetService<TService>()
        where TService : class
    {
        return Services?.GetService(typeof(TService)) as TService;
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return Services?.GetService(serviceType);
    }

    public TService GetRequiredService<TService>()
        where TService : class
    {
        return (TService)GetRequiredService(typeof(TService));
    }

    public object GetRequiredService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        object? service = Services?.GetService(serviceType);
        if (service is not null)
            return service;

        throw new InvalidOperationException($"Required service '{serviceType.FullName}' was not available.");
    }
}


