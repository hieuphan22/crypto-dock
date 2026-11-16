using Xunit;

namespace CryptoDock.Tests;

public sealed class WatchedSymbolTests
{
    [Fact]
    public void NormalizeSymbol_TrimsStripsSlashAndUppercases()
    {
        Assert.Equal("BTCUSDT", WatchedSymbol.NormalizeSymbol("  btc/usdt  "));
        Assert.Equal("ETHUSDT", WatchedSymbol.NormalizeSymbol("EthUsdt"));
    }

    [Theory]
    [InlineData("spot:btc/usdt", MarketKind.Spot, "BTCUSDT", "spot:BTCUSDT", "BTC", "S", "Spot")]
    [InlineData("FUTURES:ethusdt", MarketKind.Futures, "ETHUSDT", "futures:ETHUSDT", "ETH", "F", "Futures")]
    [InlineData("solusdt", MarketKind.Spot, "SOLUSDT", "spot:SOLUSDT", "SOL", "S", "Spot")]
    public void Parse_HandlesPrefixedAndLegacyFormats(
        string raw,
        MarketKind expectedMarket,
        string expectedSymbol,
        string expectedKey,
        string expectedDisplay,
        string expectedShortLabel,
        string expectedLabel)
    {
        WatchedSymbol parsed = WatchedSymbol.Parse(raw);

        Assert.Equal(expectedMarket, parsed.Market);
        Assert.Equal(expectedSymbol, parsed.Symbol);
        Assert.Equal(expectedKey, parsed.Key);
        Assert.Equal(expectedDisplay, parsed.DisplaySymbol);
        Assert.Equal(expectedShortLabel, parsed.ShortMarketLabel);
        Assert.Equal(expectedLabel, parsed.MarketLabel);
    }

    [Theory]
    [InlineData(MarketKind.Spot, "BTCUSDT", "https://www.binance.com/en/trade/BTC_USDT")]
    [InlineData(MarketKind.Futures, "BTCUSDT", "https://www.binance.com/en/futures/BTCUSDT")]
    [InlineData(MarketKind.Spot, "USDTDAI", "https://www.binance.com/en/trade/USDTDAI")]
    [InlineData(MarketKind.Spot, "ETHBTC", "https://www.binance.com/en/trade/ETHBTC")]
    public void BinanceUrl_GeneratesExpectedMarketUrls(MarketKind market, string symbol, string expectedUrl)
    {
        var watched = new WatchedSymbol(market, symbol);
        Assert.Equal(expectedUrl, watched.BinanceUrl);
    }
}
