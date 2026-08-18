using Intropy.Topology.Validation;

namespace Intropy.Topology.Test.Validation;

public class NameRulesTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("product-distribution")]
    [InlineData("int201")]
    [InlineData("0abc9")]
    public void IsValidLabel_WithValidLabel_ShouldReturnTrue(string value)
    {
        Assert.True(NameRules.IsValidLabel(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Product")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a_b")]
    [InlineData("a.b")]
    [InlineData("a b")]
    public void IsValidLabel_WithInvalidLabel_ShouldReturnFalse(string value)
    {
        Assert.False(NameRules.IsValidLabel(value));
    }

    [Fact]
    public void IsValidLabel_WithLabelOver63Characters_ShouldReturnFalse()
    {
        Assert.False(NameRules.IsValidLabel(new string('a', 64)));
        Assert.True(NameRules.IsValidLabel(new string('a', 63)));
    }

    [Theory]
    [InlineData("order.pim")]
    [InlineData("iss-idempotency-service")]
    [InlineData("a.b.c")]
    public void IsValidSubdomain_WithValidSubdomain_ShouldReturnTrue(string value)
    {
        Assert.True(NameRules.IsValidSubdomain(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("A.b")]
    [InlineData("a.-b")]
    public void IsValidSubdomain_WithInvalidSubdomain_ShouldReturnFalse(string value)
    {
        Assert.False(NameRules.IsValidSubdomain(value));
    }

    [Fact]
    public void IsValidSubdomain_WithNameOver253Characters_ShouldReturnFalse()
    {
        var longName = string.Join('.', Enumerable.Repeat(new string('a', 62), 5));
        Assert.False(NameRules.IsValidSubdomain(longName));
    }
}
