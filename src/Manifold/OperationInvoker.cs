namespace Manifold;

public readonly record struct OperationInvocationResult(
    object? Result,
    Type ResultType,
    string? DisplayText = null);

public interface IOperationInvoker
{
    public bool TryInvoke(
        string operationId,
        object? request,
        IServiceProvider? services,
        InvocationSurface surface,
        CancellationToken cancellationToken,
        out ValueTask<OperationInvocationResult> invocation);
}
