namespace Manifold;

public interface IOperation<in TRequest, TResult>
{
    public ValueTask<TResult> ExecuteAsync(TRequest request, OperationContext context);
}


