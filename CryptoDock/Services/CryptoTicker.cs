using System.Globalization;

namespace CryptoDock;

public sealed record CryptoTicker(
    MarketKind Market,
    string Symbol,
    decimal LastPrice,
    decimal PriceChangePercent,
    decimal HighPrice,
    decimal LowPrice,
    decimal Volume,
    DateTimeOffset UpdatedAt,
    bool HasLogo = false,
    string? LogoUrl = null,
    string? FullName = null)
{
    public string MarketLabel => Market == MarketKind.Futures ? "Futures" : "Spot";

    public string ShortMarketLabel => Market == MarketKind.Futures ? "F" : "S";

    public string Pair => Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
        ? Symbol[..^4]
        : Symbol;

    public string PriceText => LastPrice >= 100
        ? LastPrice.ToString("#,0.##", CultureInfo.InvariantCulture)
        : LastPrice.ToString("#,0.####", CultureInfo.InvariantCulture);

    public string ChangeText
    {
        get
        {
            string sign = PriceChangePercent > 0 ? "+" : string.Empty;
            return $"{sign}{PriceChangePercent:0.##}%";
        }
    }

    public string DirectionIcon => PriceChangePercent switch
    {
        > 0 => "\uE74A",
        < 0 => "\uE74B",
        _ => "\uE74D",
    };

    public string DirectionArrow => PriceChangePercent switch
    {
        > 0 => "▲",
        < 0 => "▼",
        _ => "→",
    };

    public string Summary => $"{Pair} [{ShortMarketLabel}] {PriceText} ({ChangeText})";

    public string DetailsText =>
        $"{Pair}\nMarket: {MarketLabel}\nPrice: {PriceText} USDT\n24h: {ChangeText}\nHigh: {HighPrice:#,0.####}\nLow: {LowPrice:#,0.####}\nVolume: {Volume:#,0.##}\nUpdated: {UpdatedAt:HH:mm:ss}";
}
