namespace Manifold.Cli;

public interface IFastSyncCliInvoker
{
    public bool TryInvokeFastSync(
        string[] commandTokens,
        IServiceProvider? services,
        CancellationToken cancellationToken,
        out FastCliInvocationResult invocation);
}

public interface IFastCliInvoker
{
    public bool TryInvokeFast(
        string[] commandTokens,
        IServiceProvider? services,
        CancellationToken cancellationToken,
        out ValueTask<FastCliInvocationResult> invocation);
}
