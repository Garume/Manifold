namespace Manifold.Tests;

public sealed class ParameterAttributeTests
{
    [Fact]
    public void OptionAttribute_StoresConfiguredValues()
    {
        OptionAttribute attribute = new("name")
        {
            Description = "Target name.",
            Required = false
        };

        Assert.Equal("name", attribute.Name);
        Assert.Equal("Target name.", attribute.Description);
        Assert.False(attribute.Required);
    }

    [Fact]
    public void AliasAttribute_NormalizesValues()
    {
        AliasAttribute attribute = new(" name ", "NAME", "nickname");

        Assert.Equal(["name", "nickname"], attribute.Aliases);
    }

    [Fact]
    public void CliNameAndMcpName_StoreConfiguredValues()
    {
        CliNameAttribute cliAttribute = new("person");
        McpNameAttribute mcpAttribute = new("targetName");

        Assert.Equal("person", cliAttribute.Name);
        Assert.Equal("targetName", mcpAttribute.Name);
    }

    [Fact]
    public void ArgumentAttribute_RejectsNegativePosition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArgumentAttribute(-1));
    }
}



