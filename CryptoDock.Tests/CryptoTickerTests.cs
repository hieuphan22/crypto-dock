using Xunit;

namespace CryptoDock.Tests;

public sealed class CryptoTickerTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(-12.34, "0")]
    [InlineData(95000.5, "95,000.5")]
    [InlineData(100, "100")]
    [InlineData(1234.567, "1,234.57")]
    [InlineData(1, "1")]
    [InlineData(15.6789, "15.6789")]
    [InlineData(0.5432, "0.5432")]
    [InlineData(0.09912, "0.09912")]
    [InlineData(0.01234, "0.01234")]
    [InlineData(0.00001234, "0.00001234")]
    [InlineData(0.0000005432, "0.0000005432")]
    [InlineData(0.000000001234, "0.0000000012")]
    public void FormatPrice_FormatsAcrossAllPriceScales(decimal price, string expected)
    {
        string actual = CryptoTicker.FormatPrice(price);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TickerProperties_FormatSpotAndFuturesCorrectly()
    {
        var updated = new DateTimeOffset(2026, 11, 13, 12, 30, 45, TimeSpan.Zero);

        var spotUp = new CryptoTicker(MarketKind.Spot, "BTCUSDT", 95000.5m, 2.15m, 96000m, 93000m, 12345.678m, updated);
        Assert.Equal("Spot", spotUp.MarketLabel);
        Assert.Equal("S", spotUp.ShortMarketLabel);
        Assert.Equal("BTC", spotUp.Pair);
        Assert.Equal("95,000.5", spotUp.PriceText);
        Assert.Equal("+2.15%", spotUp.ChangeText);
        Assert.Equal("▲", spotUp.DirectionArrow);
        Assert.Equal("\uE74A", spotUp.DirectionIcon);
        Assert.Equal("BTC [S] 95,000.5 (+2.15%)", spotUp.Summary);
        Assert.Contains("High: 96,000", spotUp.DetailsText);
        Assert.Contains("Low: 93,000", spotUp.DetailsText);

        var futuresDown = new CryptoTicker(MarketKind.Futures, "PEPEUSDT", 0.00001234m, -4.5m, 0.00001350m, 0.00001100m, 1000m, updated);
        Assert.Equal("Futures", futuresDown.MarketLabel);
        Assert.Equal("F", futuresDown.ShortMarketLabel);
        Assert.Equal("PEPE", futuresDown.Pair);
        Assert.Equal("0.00001234", futuresDown.PriceText);
        Assert.Equal("-4.5%", futuresDown.ChangeText);
        Assert.Equal("▼", futuresDown.DirectionArrow);
        Assert.Equal("\uE74B", futuresDown.DirectionIcon);
        Assert.Contains("High: 0.0000135", futuresDown.DetailsText);
        Assert.Contains("Low: 0.000011", futuresDown.DetailsText);

        var flat = new CryptoTicker(MarketKind.Spot, "ETHBTC", 0.05m, 0m, 0.05m, 0.05m, 10m, updated);
        Assert.Equal("ETHBTC", flat.Pair);
        Assert.Equal("0%", flat.ChangeText);
        Assert.Equal("→", flat.DirectionArrow);
        Assert.Equal("\uE74D", flat.DirectionIcon);
    }
}
