using System.Reflection;

namespace Manifold.Tests;

[Operation("session.status", Description = "Gets session status.")]
[CliCommand("session", "status")]
[McpTool("get_session_status")]
public sealed class SampleClassBasedOperation : IOperation<SampleClassBasedOperation.Request, string>
{
    public ValueTask<string> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult("ok");
    }

    public sealed record Request;
}

public sealed class ClassBasedOperationTests
{
    [Fact]
    public void OperationAttributes_CanBeApplied_ToOperationClasses()
    {
        Type operationType = typeof(SampleClassBasedOperation);

        Assert.NotNull(operationType.GetCustomAttribute<OperationAttribute>());
        Assert.NotNull(operationType.GetCustomAttribute<CliCommandAttribute>());
        Assert.NotNull(operationType.GetCustomAttribute<McpToolAttribute>());
    }

    [Fact]
    public async Task IOperation_SupportsInstanceBasedExecution()
    {
        SampleClassBasedOperation operation = new();

        string result = await operation.ExecuteAsync(
            new SampleClassBasedOperation.Request(),
            OperationContext.ForCli("session.status", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("ok", result);
    }
}



