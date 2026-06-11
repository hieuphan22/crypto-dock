namespace CryptoDock;

public enum MarketKind
{
    Spot,
    Futures,
}

public enum MarketSource
{
    Spot,
    Futures,
    Both,
}

public sealed record WatchedSymbol(MarketKind Market, string Symbol)
{
    public string Key => $"{Market.ToString().ToLowerInvariant()}:{Symbol}";

    public string MarketLabel => Market == MarketKind.Futures ? "Futures" : "Spot";

    public string ShortMarketLabel => Market == MarketKind.Futures ? "F" : "S";

    public string DisplaySymbol => Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
        ? Symbol[..^4]
        : Symbol;

    public static WatchedSymbol Spot(string symbol) => new(MarketKind.Spot, NormalizeSymbol(symbol));

    public static WatchedSymbol Futures(string symbol) => new(MarketKind.Futures, NormalizeSymbol(symbol));

    public static WatchedSymbol Parse(string value)
    {
        string trimmed = value.Trim();
        string[] parts = trimmed.Split(':', 2);
        if (parts.Length == 2)
        {
            MarketKind market = parts[0].Equals("futures", StringComparison.OrdinalIgnoreCase)
                ? MarketKind.Futures
                : MarketKind.Spot;
            return new WatchedSymbol(market, NormalizeSymbol(parts[1]));
        }

        return Spot(trimmed);
    }

    public static string NormalizeSymbol(string symbol)
    {
        return symbol.Trim().Replace("/", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}
