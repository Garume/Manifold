namespace Manifold;

public interface IResultFormatter<in TResult>
{
    public string? FormatText(TResult result, OperationContext context);
}


