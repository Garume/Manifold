namespace Manifold;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class OperationAttribute(string operationId) : Attribute
{
    public string OperationId { get; } = string.IsNullOrWhiteSpace(operationId)
        ? throw new ArgumentException("Operation id must be non-empty.", nameof(operationId))
        : operationId.Trim();

    public string? Description { get; set; }

    public string? Summary { get; set; }

    public bool Hidden { get; set; }
}


