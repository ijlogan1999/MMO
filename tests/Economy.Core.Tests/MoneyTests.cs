using Economy.Core.Currency;
using Xunit;

namespace Economy.Core.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(0, "0c")]
    [InlineData(57, "57c")]
    [InlineData(2000, "20s 0c")]
    [InlineData(123456, "12g 34s 56c")]
    [InlineData(-150, "-1s 50c")]
    public void FormatsGoldSilverCopper(long copper, string expected) => Assert.Equal(expected, Money.Format(copper));
}
