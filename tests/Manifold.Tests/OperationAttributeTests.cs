namespace Manifold.Tests;

public sealed class OperationAttributeTests
{
    [Fact]
    public void Constructor_StoresOperationId()
    {
        OperationAttribute attribute = new("math.add")
        {
            Description = "Add two numbers.",
            Summary = "Adds numbers.",
            Hidden = true
        };

        Assert.Equal("math.add", attribute.OperationId);
        Assert.Equal("Add two numbers.", attribute.Description);
        Assert.Equal("Adds numbers.", attribute.Summary);
        Assert.True(attribute.Hidden);
    }

    [Fact]
    public void Constructor_RejectsEmptyOperationId()
    {
        Assert.Throws<ArgumentException>(() => new OperationAttribute(" "));
    }
}


