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

    private static readonly string[] SmallPriceFormats =
    [
        "0.####",
        "0.#####",
        "0.######",
        "0.#######",
        "0.########",
        "0.#########",
        "0.##########",
    ];

    public string PriceText => FormatPrice(LastPrice);

    public static string FormatPrice(decimal price)
    {
        if (price <= 0m)
        {
            return "0";
        }

        if (price >= 100m)
        {
            return price.ToString("#,0.##", CultureInfo.InvariantCulture);
        }

        if (price >= 1m)
        {
            return price.ToString("#,0.####", CultureInfo.InvariantCulture);
        }

        int leadingZeroAdjustedDecimals = (int)Math.Ceiling(-Math.Log10((double)price)) + 3;
        int decimals = Math.Clamp(leadingZeroAdjustedDecimals, 4, 10);
        return price.ToString(SmallPriceFormats[decimals - 4], CultureInfo.InvariantCulture);
    }

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
        $"{Pair}\nMarket: {MarketLabel}\nPrice: {PriceText} USDT\n24h: {ChangeText}\nHigh: {FormatPrice(HighPrice)}\nLow: {FormatPrice(LowPrice)}\nVolume: {Volume:#,0.##}\nUpdated: {UpdatedAt:HH:mm:ss}";
}
