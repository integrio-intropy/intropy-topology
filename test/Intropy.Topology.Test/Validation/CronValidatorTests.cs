using Intropy.Topology.Validation;

namespace Intropy.Topology.Test.Validation;

public class CronValidatorTests
{
    [Theory]
    [InlineData("* * * * *")]
    [InlineData("*/5 * * * *")]
    [InlineData("0 9 * * 1-5")]
    [InlineData("0 0,12 1 */2 *")]
    [InlineData("  0 9 * * *  ")]
    public void IsValid_WithFiveFieldExpression_ShouldReturnTrue(string expression) =>
        Assert.True(CronValidator.IsValid(expression));

    [Theory]
    [InlineData("@hourly")]
    [InlineData("@daily")]
    [InlineData("@weekly")]
    [InlineData("@monthly")]
    [InlineData("@yearly")]
    [InlineData("@annually")]
    [InlineData("@midnight")]
    [InlineData("@DAILY")]
    public void IsValid_WithKnownMacro_ShouldReturnTrue(string expression) =>
        Assert.True(CronValidator.IsValid(expression));

    [Theory]
    [InlineData("@fortnightly")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("* * ? * *")]
    [InlineData("0 9 * * ?!")]
    public void IsValid_WithInvalidExpression_ShouldReturnFalse(string expression) =>
        Assert.False(CronValidator.IsValid(expression));

    [Fact]
    public void IsValid_IsDeliberatelyShallow_FiveLetterFieldsPass()
    {
        // The validator only catches obvious typos; semantically absurd but syntactically
        // clean expressions pass Build() and fail in the run backend / Kubernetes instead.
        Assert.True(CronValidator.IsValid("not a cron but valid"));
    }
}
