namespace Manifold.Cli;

public interface ICliInvoker
{
    public bool TryInvoke(
        string operationId,
        IReadOnlyDictionary<string, string> options,
        IReadOnlyList<string> arguments,
        IServiceProvider? services,
        bool jsonRequested,
        CancellationToken cancellationToken,
        out ValueTask<CliInvocationResult> invocation);
}


